using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>Mobius <c>AttackableThinkTaskManager</c>: per-NPC <c>thinkActive</c> on a fixed interval in player-active regions.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>Combat-tick test hook: the same prep + think the region stage runs in production.</summary>
    internal void RunNpcThinkTick(long now)
    {
        RefreshPlayerActiveRegions();
        TickWalkingRoutes(now);
        TickBoats(now);
        if (_byId.CountOf(ActiveSets.Thinking) == 0)
            return;
        var work = new RegionWorkCollector();
        _byId.Collect(ActiveSets.Thinking, work);
        foreach (var batch in work.Drain())
            ThinkActiveInRegion(batch.Region, batch.ObjectIds, now);
    }

    void CollectNpcThink(TickContext tick, RegionWorkCollector work)
    {
        RefreshPlayerActiveRegions();
        TickWalkingRoutes(tick.NowUnixMs);
        TickBoats(tick.NowUnixMs);
        if (_byId.CountOf(ActiveSets.Thinking) == 0)
            return;
        _byId.Collect(ActiveSets.Thinking, work);
    }

    void ThinkActiveInRegion(RegionId region, IReadOnlyList<int> npcIds, long now)
    {
        int drift = ZoneNpcSettings.MaxDriftRange;
        long driftSq = (long)drift * drift;
        foreach (int id in npcIds)
        {
            if (!_byId.TryGetValue(id, out var npc))
                continue;
            if (npc.Kind != ZoneEntityKind.Npc || npc.CurHp <= 0 || !npc.HomeSet)
            {
                _byId.Leave(npc, ActiveSets.Thinking);
                continue;
            }
            if (npc.WalkingRouteName != null || npc.IsBoat)
                continue;
            if (!ZoneCombat.IsCombatNpcType(npc.NpcType) || IsTreasureBox(npc.NpcId))
                continue;

            if (now < npc.NextThinkMs)
                continue;
            npc.NextThinkMs = now + RandomWalkThinkIntervalMs;

            ThinkActiveNpc(npc, now, drift, driftSq);
        }
    }

    void ThinkActiveNpc(ZoneEntity npc, long now, int drift, long driftSq)
    {
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

        public bool Parallel => true;

        public void Collect(TickContext tick, RegionWorkCollector work) => world.CollectNpcThink(tick, work);

        public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds) =>
            world.ThinkActiveInRegion(region, objectIds, tick.NowUnixMs);
    }
}