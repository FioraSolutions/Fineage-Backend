using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>WorldRegion</c> activation. Fineage keeps its 32768-unit simulation tiles for ownership and work
/// partitioning, but activation follows Mobius' 2048-unit regions (<c>World.SHIFT_BY = 11</c>): the microregion a live
/// player stands in is active immediately, and its 1-ring neighbours after <see cref="NeighborActivationGraceMs"/>.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>World.SHIFT_BY</c>: one 32768-unit map tile is 16x16 regions of 2048 units.</summary>
    internal const int MicroRegionShift = 11;
    internal const int MicroRegionSize = 1 << MicroRegionShift;

    /// <summary>Mobius <c>World.OFFSET_X = abs(((TILE_X_MIN - TILE_ZERO_COORD_X) * TILE_SIZE) &gt;&gt; SHIFT_BY)</c>.</summary>
    internal const int MicroRegionOffsetX = 64;

    /// <summary>Mobius <c>World.OFFSET_Y = abs(((TILE_Y_MIN - TILE_ZERO_COORD_Y) * TILE_SIZE) &gt;&gt; SHIFT_BY)</c>.</summary>
    internal const int MicroRegionOffsetY = 128;

    /// <summary>Mobius default <c>GridNeighborTurnOnTime</c> (seconds): neighbours wake after self has stayed active.</summary>
    internal const int NeighborActivationGraceMs = 1000;

    /// <summary>Currently active microregions (instance + Mobius region coordinates).</summary>
    readonly HashSet<RegionId> _playerActiveRegions = [];

    /// <summary>Microregions directly occupied by a live player, with the time that occupancy started.</summary>
    readonly Dictionary<RegionId, long> _playerMicroRegionSinceMs = [];

    // Per-refresh scratch, reused so an idle world does not allocate in the AI stage.
    readonly HashSet<RegionId> _desiredRegions = [];
    readonly HashSet<RegionId> _nextDirectRegions = [];
    readonly List<RegionId> _retiredRegions = [];
    readonly List<RegionId> _newRegions = [];
    readonly List<RegionId> _leftDirectRegions = [];
    readonly List<int> _microRegionQuery = [];
    readonly List<int> _microRegionMembers = [];

    readonly Queue<int> _regionWakeQueue = [];
    readonly HashSet<int> _regionWakeQueued = [];

    /// <summary>Active microregions (test/diagnostics).</summary>
    internal IReadOnlyCollection<RegionId> PlayerActiveRegions => _playerActiveRegions;

    /// <summary>NPCs waiting for a budgeted <see cref="TrackThink"/> after their microregion woke (tests/diagnostics).</summary>
    internal int PendingRegionWakeCount => _regionWakeQueue.Count;

    /// <summary>Mobius <c>World.getRegion(x, y)</c>: 2048-unit microregion with the map-tile origin offset.</summary>
    internal static RegionId MicroRegionAt(int instanceId, int x, int y) =>
        new(instanceId, FloorDiv(x, MicroRegionSize) + MicroRegionOffsetX,
            FloorDiv(y, MicroRegionSize) + MicroRegionOffsetY);

    /// <summary>Recomputes Mobius-style active regions from live player positions; call once per AI stage.</summary>
    void RefreshPlayerActiveRegions()
    {
        long now = NowUnixMs();

        _nextDirectRegions.Clear();
        // No copy: this refresh never adds or removes players.
        foreach (var player in _inWorldPlayers)
        {
            if (player.Session is not { InWorld: true } || player.CurHp <= 0)
                continue;
            _nextDirectRegions.Add(MicroRegionAt(player.InstanceId, player.X, player.Y));
        }

        // Re-entering a microregion starts a new grace; staying in it keeps the original start.
        _leftDirectRegions.Clear();
        foreach (var id in _playerMicroRegionSinceMs.Keys)
        {
            if (!_nextDirectRegions.Contains(id))
                _leftDirectRegions.Add(id);
        }
        foreach (var id in _leftDirectRegions)
            _playerMicroRegionSinceMs.Remove(id);
        foreach (var id in _nextDirectRegions)
            _playerMicroRegionSinceMs.TryAdd(id, now);

        _desiredRegions.Clear();
        foreach (var id in _nextDirectRegions)
        {
            _desiredRegions.Add(id);
            if (now - _playerMicroRegionSinceMs[id] < NeighborActivationGraceMs)
                continue;
            AddOneRingNeighbours(_desiredRegions, id);
        }

        // Retire before adding so a microregion that is still desired never churns.
        _retiredRegions.Clear();
        foreach (var id in _playerActiveRegions)
        {
            if (!_desiredRegions.Contains(id))
                _retiredRegions.Add(id);
        }
        foreach (var id in _retiredRegions)
        {
            _playerActiveRegions.Remove(id);
            OnRegionDeactivated(id);
        }

        _newRegions.Clear();
        foreach (var id in _desiredRegions)
        {
            if (!_playerActiveRegions.Contains(id))
                _newRegions.Add(id);
        }
        _newRegions.Sort();
        foreach (var id in _newRegions)
        {
            _playerActiveRegions.Add(id);
            OnRegionActivated(id);
        }
    }

    static void AddOneRingNeighbours(HashSet<RegionId> regions, RegionId center)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                regions.Add(new RegionId(center.InstanceId, center.X + dx, center.Y + dy));
            }
        }
    }

    bool IsPlayerRegionActive(RegionId microRegion) => _playerActiveRegions.Contains(microRegion);

    void OnRegionActivated(RegionId microRegion)
    {
        CollectMicroRegionMembers(microRegion);
        foreach (int id in _microRegionMembers)
        {
            if (_byId.TryGetValue(id, out var entity))
                QueueRegionWake(entity);
        }
    }

    /// <summary>
    /// Exact entities standing in <paramref name="microRegion"/>. The region directory indexes by its own 3000-unit
    /// cells, so query the overlapping cells and keep only the coordinates that map to this microregion.
    /// </summary>
    void CollectMicroRegionMembers(RegionId microRegion)
    {
        _microRegionMembers.Clear();
        int minX = (microRegion.X - MicroRegionOffsetX) * MicroRegionSize;
        int minY = (microRegion.Y - MicroRegionOffsetY) * MicroRegionSize;
        int maxX = minX + MicroRegionSize - 1;
        int maxY = minY + MicroRegionSize - 1;
        int cell = _byId.CellSize;

        _microRegionQuery.Clear();
        _byId.InCells(microRegion.InstanceId,
            SpatialCells.Of(minX, cell), SpatialCells.Of(minY, cell),
            SpatialCells.Of(maxX, cell), SpatialCells.Of(maxY, cell), _microRegionQuery);

        foreach (int id in _microRegionQuery)
        {
            if (!_byId.TryGetValue(id, out var entity))
                continue;
            if (MicroRegionAt(entity.InstanceId, entity.X, entity.Y) == microRegion)
                _microRegionMembers.Add(id);
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
        int budget = _regionWakeBudget;
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

        NoteActivationBacklog();
    }

    void OnRegionDeactivated(RegionId microRegion)
    {
        CollectMicroRegionMembers(microRegion);
        foreach (int id in _microRegionMembers)
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc)
                continue;
            ReleaseThink(npc);
            if (npc.MoveActive && !NpcRegionAwake(npc))
                SuspendIdleNpcMove(npc);
            if (npc.PathfindPending && !NpcRegionAwake(npc))
            {
                npc.PathfindPending = false;
                DequeueNpcPathfind(npc.ObjectId);
            }
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

    static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        if (value < 0 && value % divisor != 0)
            quotient--;
        return quotient;
    }
}
