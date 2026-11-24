using Fineage.Protocol.Telemetry;
using Fineage.Zone.Geo;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Geo pathfinding deferred off the movement hot loop (ticket #971 step 5). A blocked straight move asks for an A*
/// route; the search runs in the <c>npc.paths</c> stage under a per-step budget.
///
/// Each step the stage takes requests in priority order:
/// <list type="number">
/// <item><b>P1 players and fighting NPCs</b> (chasing or returning home): a blocked player or a missing hit cannot wait.</item>
/// <item><b>P2 NPCs a player is watching</b>: a late route shows as a stutter.</item>
/// <item><b>P3 everything else</b> (walking routes, boats, idle wanderers).</item>
/// </list>
/// Within one priority the oldest request goes first. A search that does not fit the budget keeps its place and resumes on
/// the next step, so the cost of one long route is spread over steps instead of spiking one.
///
/// The budget counts expanded geo nodes and line-of-sight probes, not wall time, so a step costs the same work on any host
/// and the run stays reproducible. A search that exhausts <see cref="GeoEngine.PathMaxNodes"/> gives up and the mover keeps
/// the truncated straight line — what Mobius does when <c>CellNodeBuffer</c> runs out of iterations.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Expanded geo cells the <c>npc.paths</c> stage may spend per step.</summary>
    internal const int PathfindNodeBudgetPerStep = 2400;

    /// <summary>
    /// Line-of-sight smoothing probes the stage may spend per step, in geo cells walked. A probe walks a whole straight
    /// leg, so this has to be measured like a route, not like a single node: one leg across a village is already ~25
    /// cells, and a budget below that would starve every probe and degrade the route to per-cell legs. Shared with the
    /// node budget in the same "geo cell work" unit: 2400 expansions (up to 8 <c>CanStep</c> probes each) against 512
    /// cells of raycast.
    /// </summary>
    internal const int PathfindProbeBudgetPerStep = 512;

    const int NpcPathCacheCapacity = 4096;
    static readonly int GeoPathQuantize = Math.Max(128, GridRegionPartition.MapTileSize / 4);

    /// <summary>How urgent a route is for the entity that asked for it; see the stage contract above.</summary>
    public enum PathPriority
    {
        P1 = 1,
        P2 = 2,
        P3 = 3,
    }

    /// <summary>One queued or running route request; it survives across steps while its search runs or its route smooths.</summary>
    sealed class PathRequest
    {
        public required int ObjectId { get; init; }
        public int WantX { get; set; }
        public int WantY { get; set; }
        public int WantZ { get; set; }
        public required long QueuedAtMs { get; init; }
        public PathPriority Priority { get; set; }
        public required int Sequence { get; init; }
        public GeoPathSearch? Search { get; set; }

        /// <summary>Route legs accepted so far; smoothing appends to it across steps.</summary>
        public List<(int X, int Y, int Z)> Path { get; } = [];

        /// <summary>The position <see cref="Search"/> started from; a mover that left it needs a new search.</summary>
        public int OriginX { get; set; }
        public int OriginY { get; set; }
    }

    readonly List<PathRequest> _pathRequests = [];
    readonly Dictionary<int, PathRequest> _pathRequestsById = [];
    readonly Dictionary<NpcPathCacheKey, List<(int X, int Y, int Z)>> _npcPathCache = [];
    readonly Queue<NpcPathCacheKey> _npcPathCacheEvict = [];
    int _pathRequestSequence;
    long _pathfindNodesLastStep;
    int _pathfindQueuedLastStep;

    readonly record struct NpcPathCacheKey(int FromQx, int FromQy, int ToQx, int ToQy);

    /// <summary>
    /// Requests a route to (<paramref name="wantX"/>, <paramref name="wantY"/>, <paramref name="wantZ"/>). A request already
    /// pending for the entity keeps its queue position, takes the newest destination and the most urgent priority seen, and
    /// keeps a search that is already running, so a chase that keeps re-targeting does not restart its work.
    /// </summary>
    void RequestNpcGeoPath(ZoneEntity npc, int wantX, int wantY, int wantZ)
    {
        npc.PathWantX = wantX;
        npc.PathWantY = wantY;
        npc.PathWantZ = wantZ;
        npc.PathfindPending = true;
        EnqueueNpcPathfind(npc);
    }

    /// <summary>
    /// The request's urgency from what the entity is doing right now. Mobius has no such queue; this is the Fineage budget
    /// policy, and it is a pure function of entity state so that it stays reproducible.
    /// </summary>
    PathPriority PathPriorityOf(ZoneEntity npc)
    {
        if (npc.Kind == ZoneEntityKind.Player)
            return PathPriority.P1;
        // A mob in combat must reach its target: a late route is a missing hit.
        if (npc.AggroTargetId > 0 || npc.ReturningHome)
            return PathPriority.P1;
        if (npc.WalkingRouteName != null || npc.IsBoat)
            return PathPriority.P3;
        return AnyLivePlayerWatching(npc.ObjectId) ? PathPriority.P2 : PathPriority.P3;
    }

    void EnqueueNpcPathfind(ZoneEntity npc)
    {
        var priority = PathPriorityOf(npc);
        if (_pathRequestsById.TryGetValue(npc.ObjectId, out var existing))
        {
            if (existing.WantX == npc.PathWantX && existing.WantY == npc.PathWantY && existing.WantZ == npc.PathWantZ)
            {
                // Same destination: keep the search and the smoothed legs it already has.
                existing.Priority = (PathPriority)Math.Min((int)existing.Priority, (int)priority);
                return;
            }

            RemovePathRequest(existing);
            existing.WantX = npc.PathWantX;
            existing.WantY = npc.PathWantY;
            existing.WantZ = npc.PathWantZ;
            existing.Priority = (PathPriority)Math.Min((int)existing.Priority, (int)priority);
            // A new destination invalidates the search and the legs; the next step starts over from the entity's position.
            existing.Search = null;
            existing.Path.Clear();
            InsertPathRequest(existing);
            return;
        }

        var request = new PathRequest
        {
            ObjectId = npc.ObjectId,
            WantX = npc.PathWantX,
            WantY = npc.PathWantY,
            WantZ = npc.PathWantZ,
            QueuedAtMs = NowUnixMs(),
            Priority = priority,
            Sequence = ++_pathRequestSequence,
        };
        _pathRequestsById[npc.ObjectId] = request;
        InsertPathRequest(request);
    }

    void DequeueNpcPathfind(int objectId)
    {
        if (!_pathRequestsById.Remove(objectId, out var request))
            return;
        RemovePathRequest(request);
    }

    /// <summary>Keeps the list ordered by priority, then arrival, so a step always spends its budget on the most urgent work.</summary>
    void InsertPathRequest(PathRequest request)
    {
        int index = _pathRequests.Count;
        while (index > 0 && ComparePathRequests(_pathRequests[index - 1], request) > 0)
            index--;
        _pathRequests.Insert(index, request);
    }

    void RemovePathRequest(PathRequest request) => _pathRequests.Remove(request);

    static int ComparePathRequests(PathRequest left, PathRequest right)
    {
        int byPriority = ((int)left.Priority).CompareTo((int)right.Priority);
        return byPriority != 0 ? byPriority : left.Sequence.CompareTo(right.Sequence);
    }

    void TickNpcPathfind(long now)
    {
        if (_pathRequests.Count == 0)
        {
            _pathfindNodesLastStep = 0;
            _pathfindQueuedLastStep = 0;
            return;
        }

        int nodesLeft = PathfindNodeBudgetPerStep;
        int probesLeft = PathfindProbeBudgetPerStep;
        long nodesSpent = 0;

        // Iterate by index: a finished request removes itself from the same list, so the element at this index is the next
        // one and a request that still had budget left is revisited in the same step.
        int index = 0;
        while (index < _pathRequests.Count)
        {
            if (nodesLeft <= 0 && probesLeft <= 0)
                break;

            var request = _pathRequests[index];
            if (!_byId.TryGetValue(request.ObjectId, out var npc) || !npc.PathfindPending)
            {
                // The entity left the world or was cancelled: nothing to move.
                DequeueNpcPathfind(request.ObjectId);
                continue;
            }

            if (npc.Kind == ZoneEntityKind.Npc && !NpcRegionAwake(npc))
            {
                // Mobius Attackable AI is suspended in a sleeping region; so is its pending route.
                npc.PathfindPending = false;
                DequeueNpcPathfind(request.ObjectId);
                continue;
            }

            if (request.Search == null || npc.X != request.OriginX || npc.Y != request.OriginY)
            {
                // First step for this request, or the entity moved while it waited: a route from the old origin is worthless.
                request.Search = _geo.BeginPath(npc.X, npc.Y, npc.Z, request.WantX, request.WantY, request.WantZ);
                request.Path.Clear();
                request.OriginX = npc.X;
                request.OriginY = npc.Y;
            }

            if (request.Search.Current == GeoPathSearch.Status.Running && nodesLeft > 0)
            {
                int before = request.Search.Expansions;
                request.Search.Step(nodesLeft);
                int spent = request.Search.Expansions - before;
                nodesLeft -= spent;
                nodesSpent += spent;
            }

            if (request.Search.Current == GeoPathSearch.Status.Running)
            {
                // Out of node budget: keep the queue position and resume next step.
                index++;
                continue;
            }

            var (walked, done) = FinishPathfind(request, npc, now, probesLeft);
            probesLeft = Math.Max(0, probesLeft - walked);
            if (!done)
            {
                // The route is found but not fully smoothed yet; it resumes next step with the rest of the probe budget.
                index++;
                continue;
            }

            // The request removed itself, so the element at this index is now the next one.
        }

        _pathfindNodesLastStep = nodesSpent;
        _pathfindQueuedLastStep = _pathRequests.Count;
        FineageTelemetry.PathfindNodes.Record((int)nodesSpent);
        FineageTelemetry.PathfindQueued.Record(_pathfindQueuedLastStep);
        if (nodesSpent >= PathfindNodeBudgetPerStep)
            FineageTelemetry.PathfindBudgetExhausted.Add(1);
    }

    /// <summary>
    /// Appends finished route legs to the entity and drops the request once the route is fully smoothed. Returns the probe
    /// cells it walked and whether the request is done. A search that found a route but ran out of probe budget keeps its
    /// request (with the search still Found, so the route is not recomputed) and resumes on the next step.
    /// </summary>
    (int Walked, bool Done) FinishPathfind(PathRequest request, ZoneEntity npc, long now, int probesLeft)
    {
        var search = request.Search!;
        if (search.Current != GeoPathSearch.Status.Found)
        {
            DequeueNpcPathfind(request.ObjectId);
            // No route: keep the MoveCheck truncation, like Mobius when the node buffer runs out.
            ResolveNpcPathfindWithoutRoute(npc, now);
            return (0, true);
        }

        int before = search.SmoothingCellsWalked;
        bool complete = search.TrySmooth(probesLeft, request.Path);
        int walked = search.SmoothingCellsWalked - before;
        if (!complete)
        {
            // The route is found and partly smoothed; keep the request and finish the rest next step.
            return (walked, false);
        }

        DequeueNpcPathfind(request.ObjectId);
        npc.PathfindPending = false;
        int originX = request.OriginX, originY = request.OriginY;
        var path = request.Path;
        if (path.Count == 0)
        {
            ResolveNpcPathfindWithoutRoute(npc, now);
            return (walked, true);
        }

        npc.MovePath.Clear();
        int destX = path[0].X, destY = path[0].Y, destZ = path[0].Z;
        for (int i = 1; i < path.Count; i++)
            npc.MovePath.Enqueue(path[i]);
        if (npc.Kind == ZoneEntityKind.Player && npc.Session != null)
            ApplyResolvedPlayerPath(npc, destX, destY, destZ);
        else
            CommitNpcMoveStart(npc, destX, destY, destZ, now);
        RememberNpcPathCache(originX, originY, request.WantX, request.WantY, path);
        return (walked, true);
    }

    /// <summary>No route (or no geo): truncate the straight line with MoveCheck, exactly as before the budget existed.</summary>
    void ResolveNpcPathfindWithoutRoute(ZoneEntity npc, long now)
    {
        npc.PathfindPending = false;
        npc.MovePath.Clear();
        int wantX = npc.PathWantX, wantY = npc.PathWantY, wantZ = npc.PathWantZ;
        int destX = wantX, destY = wantY, destZ = wantZ;
        if (_geo.HasGeo(npc.X, npc.Y))
        {
            npc.Z = _geo.GetHeight(npc.X, npc.Y, npc.Z);
            (destX, destY, destZ) = _geo.MoveCheck(npc.X, npc.Y, npc.Z, wantX, wantY, wantZ, out _);
        }
        else
        {
            destZ = SanitizeDestZ(npc.X, npc.Y, npc.Z, wantX, wantY, wantZ);
        }

        if (npc.Kind == ZoneEntityKind.Player && npc.Session != null)
            ApplyResolvedPlayerPath(npc, destX, destY, destZ);
        else
            CommitNpcMoveStart(npc, destX, destY, destZ, now);
    }

    /// <summary>The budgeted search finished. Replace the MoveCheck clip with the first waypoint.</summary>
    void ApplyResolvedPlayerPath(ZoneEntity player, int destX, int destY, int destZ)
    {
        player.MoveDestX = destX;
        player.MoveDestY = destY;
        player.MoveDestZ = destZ;
        player.MoveCarryX = player.MoveCarryY = 0;
        player.MoveLastTickMs = NowUnixMs();
        SetMoveActive(player, player.X != destX || player.Y != destY);
        if (player.Session != null)
            SyncSession(player, player.Session);
        if (player.Intent.ApproachTargetId > 0)
        {
            float speed = MoveSpeedOf(player);
            player.Intent.ApproachArriveUnixMs = NowUnixMs() + RemainingPathTravelMs(player, speed);
        }
        if (NetBridge is { } bridge)
        {
            bridge.FanoutMoveState(
                [player.ObjectId],
                player.ObjectId,
                player.X, player.Y, player.Z,
                destX, destY, destZ,
                player.Running);
        }
    }

    void RememberNpcPathCache(int ox, int oy, int tx, int ty, List<(int X, int Y, int Z)> path)
    {
        var key = QuantizePathKey(ox, oy, tx, ty);
        if (_npcPathCache.ContainsKey(key))
            return;
        _npcPathCache[key] = path;
        _npcPathCacheEvict.Enqueue(key);
        while (_npcPathCacheEvict.Count > NpcPathCacheCapacity)
            _npcPathCache.Remove(_npcPathCacheEvict.Dequeue());
    }

    NpcPathCacheKey QuantizePathKey(int ox, int oy, int tx, int ty) =>
        new(FloorQuant(ox), FloorQuant(oy), FloorQuant(tx), FloorQuant(ty));

    static int FloorQuant(int v)
    {
        int q = GeoPathQuantize;
        int r = v / q;
        if (v < 0 && v % q != 0)
            r--;
        return r;
    }

    bool TryNpcPathCacheOnly(int ox, int oy, int tx, int ty, out List<(int X, int Y, int Z)> path)
    {
        if (_npcPathCache.TryGetValue(QuantizePathKey(ox, oy, tx, ty), out var hit))
        {
            path = hit;
            return true;
        }

        path = [];
        return false;
    }

    void CommitNpcMoveStart(ZoneEntity npc, int destX, int destY, int destZ, long now)
    {
        if (ClipDoors(npc, ref destX, ref destY, ref destZ))
            npc.MovePath.Clear();

        npc.MoveDestX = destX;
        npc.MoveDestY = destY;
        npc.MoveDestZ = destZ;
        npc.MoveCarryX = npc.MoveCarryY = 0;
        npc.MoveLastTickMs = now;
        SetMoveActive(npc, npc.X != destX || npc.Y != destY);
        StampKnownlistRefreshAnchor(npc);
        BroadcastNpcMove(npc, destX, destY, destZ);
    }

    void TickNpcPathfindStage(TickContext tick) => TickNpcPathfind(tick.NowUnixMs);

    /// <summary>Tests and the perf harness: queue depth, running searches and the node budget the last step spent.</summary>
    internal (int Queued, int Running, long NodesLastStep) PathfindDiagnostics()
    {
        int running = 0;
        foreach (var request in _pathRequests)
        {
            if (request.Search is { Current: GeoPathSearch.Status.Running })
                running++;
        }

        return (_pathfindQueuedLastStep, running, _pathfindNodesLastStep);
    }
}
