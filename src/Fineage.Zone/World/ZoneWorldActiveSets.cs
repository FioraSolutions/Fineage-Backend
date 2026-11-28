using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius-style scheduling registries (ThinkTaskManager / MovementTaskManager analogues), kept per owning region by
/// <see cref="_byId"/>. Hot ticks iterate these sets only — never the full table on an idle world. Mutations happen on the
/// simulation thread with the entity fields they mirror.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Test/diagnostics: sizes of the Mobius-style active registries.</summary>
    internal (int Moving, int Thinking, int AiBusy, int PendingHit, int Decaying, int NeedRegen, int WithAbnormals) ActiveSetCounts =>
        (_byId.CountOf(ActiveSets.Moving), _byId.CountOf(ActiveSets.Thinking), _byId.CountOf(ActiveSets.AiBusy),
            _byId.CountOf(ActiveSets.PendingHit), _byId.CountOf(ActiveSets.Decaying), _byId.CountOf(ActiveSets.NeedRegen),
            _byId.CountOf(ActiveSets.WithAbnormals));

    /// <summary>Counts included in simulation overrun warnings.</summary>
    int _spawnedNpcCount;

    internal int SpawnedNpcCount => _spawnedNpcCount;

    internal void NoteNpcSpawned() => _spawnedNpcCount++;

    internal void NoteNpcDespawned()
    {
        if (_spawnedNpcCount > 0)
            _spawnedNpcCount--;
    }

    internal (int TotalNpcs, int ThinkingNpcs, int PlayerRegions) TickNpcDiagnostics()
    {
        RefreshPlayerActiveRegions();
        return (_spawnedNpcCount, _byId.CountOf(ActiveSets.Thinking), _playerActiveRegions.Count);
    }

    /// <summary>Members of <paramref name="set"/> by owning region, then object id; safe to mutate the set while iterating.</summary>
    int[] Active(ActiveSets set) => _byId.Snapshot(set);

    void Track(ZoneEntity entity, ActiveSets set, bool member)
    {
        if (member)
            _byId.Join(entity, set);
        else
            _byId.Leave(entity, set);
    }

    internal void SetMoveActive(ZoneEntity entity, bool active)
    {
        entity.MoveActive = active;
        Track(entity, ActiveSets.Moving, active);
    }

    internal void SetReturningHome(ZoneEntity npc, bool returning)
    {
        npc.ReturningHome = returning;
        SyncAiBusy(npc);
        if (returning)
            NoteNpcThinkJob(npc);
    }

    internal void SetAggroTarget(ZoneEntity npc, int targetId)
    {
        npc.AggroTargetId = targetId;
        SyncAiBusy(npc);
        if (targetId > 0)
            NoteNpcThinkJob(npc);
        // Mobius setIntention(ATTACK) → setRunning() / ChangeMoveType. Every path that
        // enters combat (damage hate, AddHate payloads, aggro-range scan, clan help)
        // comes through here — not only a melee hit.
        if (targetId > 0 && !npc.Running)
        {
            npc.Running = true;
            FanoutMoveType(npc);
        }
    }

    void SyncAiBusy(ZoneEntity npc)
    {
        if (npc.Kind != ZoneEntityKind.Npc)
            return;
        Track(npc, ActiveSets.AiBusy, npc.AggroTargetId > 0 || npc.ReturningHome);
    }

    internal void SetPendingHit(ZoneEntity entity, PendingSkillHit? hit)
    {
        entity.PendingHit = hit;
        Track(entity, ActiveSets.PendingHit, hit != null);
    }

    internal void SetDecayAtMs(ZoneEntity npc, long decayAtMs)
    {
        npc.DecayAtMs = decayAtMs;
        Track(npc, ActiveSets.Decaying, decayAtMs > 0);
    }

    internal void SyncNeedRegen(ZoneEntity entity)
    {
        bool need = !entity.Dead
            && entity.CurHp > 0
            && (entity.CurHp < entity.MaxHp || entity.CurMp < entity.MaxMp);
        Track(entity, ActiveSets.NeedRegen, need);
    }

    /// <summary>
    /// Mobius awake predicate: player-active microregion (or its 1-ring lease), a live watcher, combat/return home,
    /// or drifted far enough that AI must return. The watcher clause is Fineage's interest proxy for the same thing —
    /// the production <c>GridAoi</c> watch footprint (3×3 cells) covers the Mobius 3×3 microregion lease — and it
    /// only reaches as far as a player can actually see the pawn.
    /// </summary>
    bool NpcRegionAwake(ZoneEntity npc)
    {
        if (npc.Kind != ZoneEntityKind.Npc)
            return true;
        if (AnyLivePlayerWatching(npc.ObjectId))
            return true;
        if (npc.AggroTargetId > 0 || npc.ReturningHome)
            return true;
        // Same threshold as AttackableAI thinkActive returnHome (MaxDriftRange), not 2×.
        if (DriftedBeyondMaxRange(npc))
            return true;
        return IsPlayerRegionActive(MicroRegionAt(npc.InstanceId, npc.X, npc.Y));
    }

    /// <summary>
    /// Mobius <c>AttackableThinkTaskManager.add</c>: only attackables in an awake region or otherwise needing AI.
    /// </summary>
    void TrackThink(ZoneEntity npc)
    {
        if (npc.Kind != ZoneEntityKind.Npc || npc.CurHp <= 0 || !npc.HomeSet)
            return;
        if (!ZoneCombat.IsCombatNpcType(npc.NpcType))
            return;
        if ((npc.ActiveSets & ActiveSets.Thinking) != 0)
            return;
        if (!NpcRegionAwake(npc))
            return;
        // A player entering view used to mark every watched NPC due on this tick. That is the
        // activation spike (thousands of thinks, and with geodata a MoveCheck each, on one step).
        // Idle think keeps a phase inside the interval. A mob that already has a job is due now.
        long now = NowUnixMs();
        if (npc.AggroTargetId > 0 || npc.ReturningHome || npc.LeaderObjectId != 0)
            npc.NextThinkMs = now;
        else if (npc.NextThinkMs <= now)
            ScheduleNextThink(npc, now);
        _byId.Join(npc, ActiveSets.Thinking);
        EnqueueThink(npc);
    }

    /// <summary>
    /// Drops <see cref="ActiveSets.Thinking"/> unless the pawn still has a reason to run AI. Mobius
    /// <c>WorldRegion.switchAI(false)</c> does not consult the fight: the region is retired because no playable is in
    /// its 3x3 neighbourhood, so the target it holds is already out of reach. <paramref name="force"/> is that
    /// unconditional path (region deactivation); everything else keeps the pawn while it fights, walks home or is
    /// seen.
    /// </summary>
    void ReleaseThink(ZoneEntity npc, bool force = false)
    {
        if ((npc.ActiveSets & ActiveSets.Thinking) == 0)
            return;
        if (!force)
        {
            if (npc.AggroTargetId > 0 || npc.ReturningHome || DriftedBeyondMaxRange(npc))
                return;
            // Same Mobius gate as TrackThink: a live watcher or the WorldRegion lease.
            if (AnyLivePlayerWatching(npc.ObjectId))
                return;
        }
        else if (npc.WalkingRouteName != null || npc.IsBoat)
        {
            // Mobius WalkingManager / boat movement is global state: setActive(false) -> switchAI(false) never removes
            // a walker from the walking task, so the region lease must not freeze one either.
            return;
        }

        DequeueThink(npc);
        _byId.Leave(npc, ActiveSets.Thinking);
    }

    bool DriftedBeyondMaxRange(ZoneEntity npc)
    {
        if (!npc.HomeSet)
            return false;
        int limit = _maxDriftRange;
        long dx = npc.X - (long)npc.HomeX;
        long dy = npc.Y - (long)npc.HomeY;
        return dx * dx + dy * dy > (long)limit * limit;
    }
}
