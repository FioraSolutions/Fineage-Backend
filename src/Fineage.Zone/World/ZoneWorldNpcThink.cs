using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>Mobius <c>AttackableThinkTaskManager</c>: per-NPC <c>thinkActive</c> on a fixed interval in player-active regions.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>One slot per simulation step. The idle interval is 1 s, so 16 slots cover a short stall without wrapping onto a live bucket.</summary>
    const int ThinkWheelSlots = 16;
    /// <summary>Sentinel <see cref="ZoneEntity.ThinkWheelSlot"/> for a chase, return, or minion that is due every step.</summary>
    const int ThinkUrgentSlot = -2;

    readonly List<int>[] _thinkWheel = CreateThinkWheel();

    static List<int>[] CreateThinkWheel()
    {
        var wheel = new List<int>[ThinkWheelSlots];
        for (int i = 0; i < wheel.Length; i++)
            wheel[i] = [];
        return wheel;
    }
    readonly List<int> _thinkUrgent = [];
    readonly List<int> _thinkReschedule = [];
    readonly object _thinkRescheduleLock = new();
    long _thinkDrainedThroughMs = long.MinValue;

    /// <summary>Combat-tick test hook: the same prep + think the region stage runs in production.</summary>
    internal void RunNpcThinkTick(long now)
    {
        var work = new RegionWorkCollector();
        CollectNpcThink(new TickContext(0, now, now), work);
        foreach (var batch in work.Drain())
            ThinkActiveInRegion(batch.Region, batch.ObjectIds, now);
    }

    void CollectNpcThink(TickContext tick, RegionWorkCollector work)
    {
        ReapThinkReschedule();
        RefreshPlayerActiveRegions();
        DrainRegionWakeBudget();
        TickWalkingRoutes(tick.NowUnixMs);
        TickBoats(tick.NowUnixMs);
        CollectDueThinks(tick.NowUnixMs, work);
    }

    void ThinkActiveInRegion(RegionId region, IReadOnlyList<int> npcIds, long now)
    {
        int drift = ZoneNpcSettings.MaxDriftRange;
        long driftSq = (long)drift * drift;
        foreach (int id in npcIds)
        {
            if (!_byId.TryGetValue(id, out var npc))
                continue;
            bool reschedule = (npc.ActiveSets & ActiveSets.Thinking) != 0;
            if (npc.Kind != ZoneEntityKind.Npc || npc.CurHp <= 0 || !npc.HomeSet)
            {
                DequeueThink(npc);
                _byId.Leave(npc, ActiveSets.Thinking);
                continue;
            }
            if (npc.WalkingRouteName != null || npc.IsBoat)
            {
                if (reschedule)
                    QueueThinkReschedule(id);
                continue;
            }
            if (!ZoneCombat.IsCombatNpcType(npc.NpcType) || IsTreasureBox(npc.NpcId))
            {
                if (reschedule)
                    QueueThinkReschedule(id);
                continue;
            }

            // The interval gates idle wander. A chase, a walk home, or a minion following its leader
            // is already a job and does not wait for the phase.
            bool idle = !IsThinkJob(npc);
            if (idle && now < npc.NextThinkMs)
            {
                QueueThinkReschedule(id);
                continue;
            }
            if (idle)
                npc.NextThinkMs = now + RandomWalkThinkIntervalMs;

            ThinkActiveNpc(npc, now, drift, driftSq);
            if ((npc.ActiveSets & ActiveSets.Thinking) != 0)
                QueueThinkReschedule(id);
        }
    }

    static bool IsThinkJob(ZoneEntity npc) =>
        npc.AggroTargetId > 0 || npc.Chasing || npc.ReturningHome || npc.LeaderObjectId != 0;

    /// <summary>
    /// A chase, a walk home, or a minion follow is due on the next think, not when its idle bucket comes around.
    /// From a region body this only records the id; the scheduler thread owns the wheel.
    /// </summary>
    /// <summary>Tests: force the next think collection to run AI for this NPC.</summary>
    internal void BumpNpcThinkForTests(ZoneEntity npc)
    {
        using (_affinity.Step())
        {
            npc.NextThinkMs = 0;
            if ((npc.ActiveSets & ActiveSets.Thinking) == 0)
                TrackThink(npc);
            else
                EnqueueThink(npc);
        }
    }

    internal void NoteNpcThinkJob(ZoneEntity npc)
    {
        if (RegionBodyScope.Of(this) != null)
        {
            QueueThinkReschedule(npc.ObjectId);
            return;
        }

        if ((npc.ActiveSets & ActiveSets.Thinking) == 0)
            TrackThink(npc);
        else
            EnqueueThink(npc);
    }

    void QueueThinkReschedule(int objectId)
    {
        lock (_thinkRescheduleLock)
            _thinkReschedule.Add(objectId);
    }

    void ReapThinkReschedule()
    {
        int[] pending;
        lock (_thinkRescheduleLock)
        {
            if (_thinkReschedule.Count == 0)
                return;
            pending = _thinkReschedule.ToArray();
            _thinkReschedule.Clear();
        }

        foreach (int id in pending)
        {
            if (!_byId.TryGetValue(id, out var npc) || (npc.ActiveSets & ActiveSets.Thinking) == 0)
                continue;
            EnqueueThink(npc);
        }
    }

    void EnqueueThink(ZoneEntity npc)
    {
        DequeueThink(npc);
        if ((npc.ActiveSets & ActiveSets.Thinking) == 0)
            return;
        if (IsThinkJob(npc))
        {
            _thinkUrgent.Add(npc.ObjectId);
            npc.ThinkWheelSlot = ThinkUrgentSlot;
            return;
        }

        int slot = ThinkSlot(npc.NextThinkMs);
        _thinkWheel[slot].Add(npc.ObjectId);
        npc.ThinkWheelSlot = slot;
    }

    void DequeueThink(ZoneEntity npc)
    {
        int slot = npc.ThinkWheelSlot;
        if (slot < 0 && slot != ThinkUrgentSlot)
            return;
        if (slot == ThinkUrgentSlot)
            _thinkUrgent.Remove(npc.ObjectId);
        else if ((uint)slot < (uint)ThinkWheelSlots)
            _thinkWheel[slot].Remove(npc.ObjectId);
        npc.ThinkWheelSlot = -1;
    }

    static int ThinkSlot(long unixMs)
    {
        int step = ZoneSimulationSettings.StepMilliseconds;
        if (step < 1)
            step = 1;
        long quot = Math.DivRem(unixMs, step, out long rem);
        if (rem < 0)
            quot--;
        int slot = (int)(quot % ThinkWheelSlots);
        return slot < 0 ? slot + ThinkWheelSlots : slot;
    }

    /// <summary>
    /// Drains the buckets whose window has closed by <paramref name="now"/>, plus every job.
    /// An idle NPC is visited on the step its phase falls in, not on every step the region stays awake.
    /// </summary>
    void CollectDueThinks(long now, RegionWorkCollector work)
    {
        int step = ZoneSimulationSettings.StepMilliseconds;
        if (step < 1)
            step = 1;
        if (_thinkDrainedThroughMs == long.MinValue)
            _thinkDrainedThroughMs = now - step;

        long cursor = _thinkDrainedThroughMs;
        int guard = 0;
        while (cursor + step <= now && guard < ThinkWheelSlots)
        {
            cursor += step;
            guard++;
            DrainThinkSlot(ThinkSlot(cursor - step), now, work);
        }

        if (guard >= ThinkWheelSlots && cursor + step <= now)
        {
            for (int slot = 0; slot < ThinkWheelSlots; slot++)
                DrainThinkSlot(slot, now, work);
            cursor = now;
        }

        _thinkDrainedThroughMs = cursor;
        DrainThinkList(_thinkUrgent, now, work);
    }

    void DrainThinkSlot(int slot, long now, RegionWorkCollector work) =>
        DrainThinkList(_thinkWheel[slot], now, work);

    void DrainThinkList(List<int> slot, long now, RegionWorkCollector work)
    {
        if (slot.Count == 0)
            return;
        var batch = slot.ToArray();
        slot.Clear();
        foreach (int id in batch)
        {
            if (!_byId.TryGetValue(id, out var npc))
                continue;
            npc.ThinkWheelSlot = -1;
            if ((npc.ActiveSets & ActiveSets.Thinking) == 0)
                continue;
            if (!IsThinkJob(npc) && npc.NextThinkMs > now)
            {
                EnqueueThink(npc);
                continue;
            }

            if (_byId.TryOwnerOf(npc.ObjectId, out var owner))
                work.Add(owner.Region, id);
            else
                EnqueueThink(npc);
        }
    }

    void ThinkActiveNpc(ZoneEntity npc, long now, int drift, long driftSq)
    {
        // Privates spawn on the master's own think phase. Doing it in TrackThink put every
        // minion fan-out on the tick the region woke.
        SpawnPrivates(npc);
        if (npc.GlobalAggro != 0)
            npc.GlobalAggro += npc.GlobalAggro < 0 ? 1 : -1;
        if (npc.AggroTargetId > 0 || npc.Chasing || npc.ReturningHome)
            return;
        if (npc.CastingUntilMs > now || IsImmobilized(npc) || IsFeared(npc))
            return;

        long hdx = npc.X - (long)npc.HomeX;
        long hdy = npc.Y - (long)npc.HomeY;
        long homeDistSq = hdx * hdx + hdy * hdy;
        if (homeDistSq > driftSq)
        {
            ReturnHome(npc, now);
            return;
        }

        if (npc.NpcType == "Guard" || npc.SiegeNpc != null)
            return;
        if (TryFollowLeader(npc, now))
            return;
        if (RndBound(npc, RandomWalkRate) != 0)
            return;
        if (TryNpcSelfBuffSkill(npc, now))
            return;

        int x1 = npc.HomeX;
        int y1 = npc.HomeY;
        int z1 = npc.HomeZ;
        if (homeDistSq < driftSq)
        {
            int deltaX = RndBound(npc, drift * 2);
            int deltaY = RndInclusive(npc, deltaX, drift * 2);
            deltaY = (int)Math.Sqrt((double)deltaY * deltaY - (double)deltaX * deltaX);
            x1 = deltaX + npc.HomeX - drift;
            y1 = deltaY + npc.HomeY - drift;
            z1 = npc.Z;
        }

        if (!TryWanderDestination(npc, x1, y1, z1, driftSq, out int destX, out int destY, out int destZ))
            return;
        if (destX == npc.X && destY == npc.Y)
            return;
        StartNpcMove(npc, destX, destY, destZ, now);
    }

    bool TryWanderDestination(ZoneEntity npc, int x1, int y1, int z1, long driftSq, out int destX, out int destY, out int destZ)
    {
        destX = x1;
        destY = y1;
        destZ = z1;
        if (!_geo.HasGeo(npc.X, npc.Y))
            return WithinDrift(npc, destX, destY, driftSq);

        if (npc.TryWanderGeoCache(npc.X, npc.Y, x1, y1, out destX, out destY, out destZ))
            return WithinDrift(npc, destX, destY, driftSq);

        (destX, destY, destZ) = _geo.MoveCheck(npc.X, npc.Y, npc.Z, x1, y1, z1, out _);
        npc.SetWanderGeoCache(npc.X, npc.Y, x1, y1, destX, destY, destZ);
        return WithinDrift(npc, destX, destY, driftSq);
    }

    static bool WithinDrift(ZoneEntity npc, int destX, int destY, long driftSq)
    {
        long fdx = destX - (long)npc.HomeX;
        long fdy = destY - (long)npc.HomeY;
        return fdx * fdx + fdy * fdy <= driftSq;
    }

    sealed class RandomWalkStage(ZoneWorld world) : IRegionStage
    {
        public string Name => "random-walk";

        // The body moves only its own region's NPCs and reads positions the movement stages settled this tick.
        public bool Parallel => true;

        public void Collect(TickContext tick, RegionWorkCollector work) => world.CollectNpcThink(tick, work);

        public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds) =>
            world.ThinkActiveInRegion(region, objectIds, tick.NowUnixMs);
    }
}