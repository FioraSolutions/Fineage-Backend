using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>WorldRegion</c> activation: NPC think/random-walk runs only in tiles that currently have a player
/// nearby (this region and overlapping neighbours), or while a player watches the mob, or it has drifted far from home.
/// </summary>
public sealed partial class ZoneWorld
{
    readonly HashSet<RegionId> _playerActiveRegions = [];
    readonly Queue<int> _regionWakeQueue = [];
    readonly HashSet<int> _regionWakeQueued = [];

    /// <summary>Regions with at least one player in range of their tile (test/diagnostics).</summary>
    internal IReadOnlyCollection<RegionId> PlayerActiveRegions => _playerActiveRegions;

    /// <summary>NPCs waiting for a budgeted <see cref="TrackThink"/> after their tile woke (tests/diagnostics).</summary>
    internal int PendingRegionWakeCount => _regionWakeQueue.Count;

    /// <summary>How far past a tile edge we still activate neighbours (one map tile).</summary>
    const int RegionActivationPad = GridRegionPartition.MapTileSize;

    /// <summary>Recomputes Mobius-style active regions from live player positions; call once per AI stage.</summary>
    void RefreshPlayerActiveRegions()
    {
        var next = new HashSet<RegionId>();
        foreach (var player in Players())
        {
            if (player.Session is not { InWorld: true } || player.CurHp <= 0)
                continue;
            foreach (var id in _regions.RegionsOverlapping(
                         player.InstanceId,
                         player.X - RegionActivationPad,
                         player.Y - RegionActivationPad,
                         player.X + RegionActivationPad,
                         player.Y + RegionActivationPad))
                next.Add(id);
        }

        foreach (var id in next)
        {
            if (_playerActiveRegions.Add(id))
                OnRegionActivated(id);
        }

        var retired = new List<RegionId>();
        foreach (var id in _playerActiveRegions)
        {
            if (!next.Contains(id))
                retired.Add(id);
        }

        foreach (var id in retired)
        {
            _playerActiveRegions.Remove(id);
            OnRegionDeactivated(id);
        }
    }

    bool IsPlayerRegionActive(RegionId region) => _playerActiveRegions.Contains(region);

    void OnRegionActivated(RegionId region)
    {
        if (!_byId.TryGetContainer(region, out var container))
            return;
        foreach (int id in container.Members.ToArray())
        {
            if (_byId.TryGetValue(id, out var entity))
                QueueRegionWake(entity);
        }
    }

    void QueueRegionWake(ZoneEntity npc)
    {
        if (npc.Kind != ZoneEntityKind.Npc || npc.CurHp <= 0 || !npc.HomeSet)
            return;
        if (!ZoneCombat.IsCombatNpcType(npc.NpcType))
            return;
        if ((npc.ActiveSets & ActiveSets.Thinking) != 0)
            return;
        if (!_regionWakeQueued.Add(npc.ObjectId))
            return;
        _regionWakeQueue.Enqueue(npc.ObjectId);
    }

    /// <summary>Drains the wake queue once per combat tick before random-walk collect.</summary>
    void DrainRegionWakeBudget()
    {
        int budget = ZoneSimulationSettings.RegionWakeBudgetPerTick;
        while (budget > 0 && _regionWakeQueue.Count > 0)
        {
            int id = _regionWakeQueue.Dequeue();
            _regionWakeQueued.Remove(id);
            if (!_byId.TryGetValue(id, out var npc))
                continue;
            if ((npc.ActiveSets & ActiveSets.Thinking) != 0)
                continue;
            if (!NpcRegionAwake(npc))
                continue;
            TrackThink(npc);
            budget--;
        }
    }

    void OnRegionDeactivated(RegionId region) => SuspendNpcRegion(region);

    /// <summary>Mobius inactive region: pause think + idle movement; combat / watched mobs stay awake.</summary>
    void SuspendNpcRegion(RegionId region)
    {
        if (!_byId.TryGetContainer(region, out var container))
            return;
        foreach (int id in container.Snapshot(ActiveSets.Thinking))
        {
            if (_byId.TryGetValue(id, out var npc) && npc.Kind == ZoneEntityKind.Npc)
                ReleaseThink(npc);
        }

        foreach (int id in container.Snapshot(ActiveSets.Moving))
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc)
                continue;
            if (NpcRegionAwake(npc))
                continue;
            SuspendIdleNpcMove(npc);
        }

        foreach (int id in container.Members)
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc || !npc.PathfindPending)
                continue;
            if (NpcRegionAwake(npc))
                continue;
            npc.PathfindPending = false;
            DequeueNpcPathfind(npc.ObjectId);
        }
    }

    /// <summary>
    /// One phase in <c>(now, now + interval]</c>. A uniform draw keeps each mob on the same step slice
    /// across later intervals instead of piling onto an activation tick.
    /// </summary>
    void ScheduleNextThink(ZoneEntity npc, long now)
    {
        npc.NextThinkMs = now + 1 + RndBound(npc, RandomWalkThinkIntervalMs);
    }

    internal const int RandomWalkThinkIntervalMs = 1000;
}