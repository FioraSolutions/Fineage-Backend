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
    /// Nodes the P1 band must leave untouched while any P2/P3 request is queued. Priority is still strict — P1 goes
    /// first, and P1 uses the whole budget when nothing lower is waiting — but a sustained P1 inflow cannot consume the
    /// last quarter of the step: about one cross-region route keeps moving per step, so ambient NPCs and boats degrade
    /// instead of stopping. Measured, not guessed: a flat-region route costs ~500 expansions, so 600 is one route's worth.
    /// </summary>
    internal const int PathfindLowerPriorityReserve = 600;

    /// <summary>
    /// Line-of-sight smoothing probes the stage may spend per step, in geo cells walked. A probe walks a whole straight
    /// leg, so this has to be measured like a route, not like a single node: one leg across a village is already ~25
    /// cells, and a budget below that would starve every probe and degrade the route to per-cell legs. Shared with the
    /// node budget in the same "geo cell work" unit: 2400 expansions (up to 8 <c>CanStep</c> probes each) against 512
    /// cells of raycast.
    /// </summary>
    internal const int PathfindProbeBudgetPerStep = 512;

    /// <summary>
    /// The same P1 reserve for the smoothing budget, at the same share (a quarter): a P1 route that already found its
    /// way must not spend every probe cell either, or lower-priority routes sit Found-but-unsmoothed forever.
    /// </summary>
    internal const int PathfindLowerPriorityProbeReserve = 128;

    const int NpcPathCacheCapacity = 4096;
    static readonly int GeoPathQuantize = Math.Max(128, GridRegionPartition.MapTileSize / 4);

    /// <summary>
    /// How many "no route" answers are remembered. A negative entry is one bit of information, so it is kept separately
    /// from the positive cache and can be larger: an unreachable mob is asked about on every approach tick, and the point
    /// is to answer that question without spending the node budget again.
    /// </summary>
    internal const int NpcNoRouteCacheCapacity = 8192;

    /// <summary>
    /// How long a "no route" answer stays valid without a geometry change. A target behind a wall does not become
    /// reachable by waiting, but dynamic blockers and a target that stepped into a doorway do, so an entry expires the
    /// same way Mobius drops a stale <c>CellNodeBuffer</c> result instead of trusting it forever.
    /// </summary>
    internal const long NpcNoRouteCacheTtlMs = 5_000;

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

        /// <summary>The wait this request spent before the stage first advanced it; recorded once, for telemetry.</summary>
        public bool WaitRecorded { get; set; }

        /// <summary>Whether the stage advanced this request in the current step; reset at the top of every step.</summary>
        public bool ServedThisStep { get; set; }

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
    readonly Dictionary<NpcNoRouteKey, long> _npcNoRouteCache = [];
    readonly Queue<(NpcNoRouteKey Key, long StampMs)> _npcNoRouteCacheEvict = [];
    int _pathRequestSequence;
    long _pathfindNodesLastStep;
    long _pathfindP1NodesLastStep;
    int _pathfindQueuedLastStep;

    readonly record struct NpcPathCacheKey(int FromQx, int FromQy, int ToQx, int ToQy);

    /// <summary>
    /// Key of a definitive "no route" answer. Unlike <see cref="NpcPathCacheKey"/> it carries the instance and the
    /// geometry version: whether a path exists is a fact about that geometry in that world, so an entry from older
    /// bytes — or from another instance — must never answer a newer question.
    /// </summary>
    readonly record struct NpcNoRouteKey(int InstanceId, int GeometryVersion, int FromQx, int FromQy, int ToQx, int ToQy);

    /// <summary>
    /// Requests a route to (<paramref name="wantX"/>, <paramref name="wantY"/>, <paramref name="wantZ"/>). A request already
    /// pending for the entity keeps its queue position, takes the newest destination and the most urgent priority seen, and
    /// keeps a search that is already running, so a chase that keeps re-targeting does not restart its work.
    /// <para>
    /// A destination this geometry already answered "no route" for resolves immediately: the answer is a fact, not work,
    /// so it must not spend the shared node budget again. That is what stops an unreachable target from re-running a full
    /// A* on every approach tick — the P1 flood that starved the queue during the #971 soak.
    /// </para>
    /// </summary>
    void RequestNpcGeoPath(ZoneEntity npc, int wantX, int wantY, int wantZ)
    {
        npc.PathWantX = wantX;
        npc.PathWantY = wantY;
        npc.PathWantZ = wantZ;
        npc.PathfindPending = true;
        long now = NowUnixMs();
        if (TryCachedNoRoute(npc, npc.X, npc.Y, wantX, wantY, now))
        {
            // Same clipped straight line a completed search would have produced, without the search.
            DequeueNpcPathfind(npc.ObjectId);
            FineageTelemetry.PathfindNoRouteCached.Add(1);
            ResolveNpcPathfindWithoutRoute(npc, now);
            return;
        }

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
                // Same destination: keep the search and the smoothed legs it already has. A promotion still has to move
                // the request into its new band, or it stays physically queued behind work it now outranks.
                ReprioritizePathRequest(existing, priority);
                return;
            }

            RemovePathRequest(existing);
            existing.WantX = npc.PathWantX;
            existing.WantY = npc.PathWantY;
            existing.WantZ = npc.PathWantZ;
            existing.Priority = MostUrgent(existing.Priority, priority);
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
        // A request dropped while the stage still had it queued is a real wait that produced no work. It is reported on
        // the unserved instrument, not on the served-latency histogram, so one request is never counted by both.
        if (!request.WaitRecorded)
        {
            FineageTelemetry.PathfindUnservedWaitMilliseconds.Record(
                Math.Max(0, NowUnixMs() - request.QueuedAtMs), PriorityTag(request.Priority));
        }
    }

    /// <summary>
    /// Raises a pending request's priority in place. <see cref="_pathRequests"/> is ordered by priority then sequence, and
    /// a <see cref="List{T}"/> does not resort itself, so the request is taken out and inserted again at its new band.
    /// <see cref="PathRequest.Sequence"/> is <c>init</c> and is left alone: a promoted request keeps its arrival order, so
    /// promotions never jump ahead of work that was already at the same priority.
    /// </summary>
    void ReprioritizePathRequest(PathRequest request, PathPriority priority)
    {
        PathPriority promoted = MostUrgent(request.Priority, priority);
        if (promoted == request.Priority)
            return;
        RemovePathRequest(request);
        request.Priority = promoted;
        InsertPathRequest(request);
    }

    /// <summary>Path priorities order by urgency with <c>P1</c> smallest, so the most urgent one is the minimum.</summary>
    static PathPriority MostUrgent(PathPriority left, PathPriority right) =>
        (PathPriority)Math.Min((int)left, (int)right);

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
            _pathfindP1NodesLastStep = 0;
            _pathfindQueuedLastStep = 0;
            return;
        }

        int nodesLeft = PathfindNodeBudgetPerStep;
        int probesLeft = PathfindProbeBudgetPerStep;
        long nodesSpent = 0;
        long p1NodesSpent = 0;

        // Priority stays strict — P1 is still served first — but when anything below it is waiting, the P1 band may not
        // take the whole step. This is the measured mitigation for the starvation the flood test pins: without it a
        // continuous P1 inflow spends every node on chases and P2/P3 never start a search at all.
        bool lowerWaiting = false;
        foreach (var queued in _pathRequests)
        {
            queued.ServedThisStep = false;
            if (queued.Priority != PathPriority.P1)
                lowerWaiting = true;
        }

        int p1NodesLeft = lowerWaiting
            ? PathfindNodeBudgetPerStep - PathfindLowerPriorityReserve
            : PathfindNodeBudgetPerStep;
        int p1ProbesLeft = lowerWaiting
            ? PathfindProbeBudgetPerStep - PathfindLowerPriorityProbeReserve
            : PathfindProbeBudgetPerStep;

        // Iterate by index: a finished request removes itself from the same list, so the element at this index is the next
        // one and a request that still had budget left is revisited in the same step.
        int index = 0;
        while (index < _pathRequests.Count)
        {
            if (nodesLeft <= 0 && probesLeft <= 0)
                break;

            var request = _pathRequests[index];
            if (request.Priority == PathPriority.P1 && p1NodesLeft <= 0 && p1ProbesLeft <= 0)
            {
                // The P1 band spent its share of this step. Its request keeps its place and resumes next step; walking
                // past it is what lets the reserve reach P2/P3 instead of staying unused behind a running P1 search.
                index++;
                continue;
            }
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
                FineageTelemetry.PathfindSearches.Add(1, PriorityTag(request.Priority));
            }

            int expansionsBefore = request.Search.Expansions;
            int nodesCap = request.Priority == PathPriority.P1 ? Math.Min(nodesLeft, p1NodesLeft) : nodesLeft;
            if (request.Search.Current == GeoPathSearch.Status.Running && nodesCap > 0)
                request.Search.Step(nodesCap);
            int spent = request.Search.Expansions - expansionsBefore;
            nodesLeft -= spent;
            nodesSpent += spent;
            if (request.Priority == PathPriority.P1)
            {
                p1NodesLeft -= spent;
                p1NodesSpent += spent;
            }
            if (spent > 0)
                request.ServedThisStep = true;

            if (request.Search.Current == GeoPathSearch.Status.Running)
            {
                // Out of node budget: keep the queue position and resume next step.
                if (spent > 0)
                    RecordPathfindWait(request, now);
                index++;
                continue;
            }

            int probeCap = request.Priority == PathPriority.P1 ? Math.Min(probesLeft, p1ProbesLeft) : probesLeft;
            var (walked, done) = FinishPathfind(request, npc, now, probeCap);
            probesLeft = Math.Max(0, probesLeft - walked);
            if (request.Priority == PathPriority.P1)
                p1ProbesLeft = Math.Max(0, p1ProbesLeft - walked);
            if (walked > 0)
                request.ServedThisStep = true;
            if (spent > 0 || walked > 0 || done)
                RecordPathfindWait(request, now);
            if (!done)
            {
                // The route is found but not fully smoothed yet; it resumes next step with the rest of the probe budget.
                index++;
                continue;
            }

            // The request removed itself, so the element at this index is now the next one.
        }

        _pathfindNodesLastStep = nodesSpent;
        _pathfindP1NodesLastStep = p1NodesSpent;
        _pathfindQueuedLastStep = _pathRequests.Count;
        FineageTelemetry.PathfindNodes.Record((int)nodesSpent);
        FineageTelemetry.PathfindQueued.Record(_pathfindQueuedLastStep);
        if (nodesSpent >= PathfindNodeBudgetPerStep)
            FineageTelemetry.PathfindBudgetExhausted.Add(1);

        RecordPathfindBacklog(now, nodesLeft);
    }

    /// <summary>
    /// Records the queue wait once, the first step the stage does real work for the request: expansions it paid for, probe
    /// cells it smoothed, or the resolution that removed it. A request the budget never reached is still waiting and is not
    /// recorded — logging it would hide exactly the backlog this instrument exists to show.
    /// </summary>
    static void RecordPathfindWait(PathRequest request, long now)
    {
        if (request.WaitRecorded)
            return;
        request.WaitRecorded = true;
        FineageTelemetry.PathfindWaitMilliseconds.Record(
            Math.Max(0, now - request.QueuedAtMs), PriorityTag(request.Priority));
    }

    /// <summary>
    /// Emits the per-priority backlog of what is still queued after a step: the age of the oldest request, and whether
    /// the node budget was what kept it waiting. Priority order alone cannot starve below P1 unless the budget is the
    /// blocker, so this is the signal that tells the two apart. A priority with nothing queued emits nothing, so an idle
    /// band cannot be mistaken for a satisfied one with age 0.
    /// <para>
    /// "Starved" means the step ended with the node budget gone and the request not advanced by it. A request the P1
    /// reserve held back while lower priorities ran is not counted: that is the policy working, not the budget failing.
    /// </para>
    /// </summary>
    void RecordPathfindBacklog(long now, int nodesLeft)
    {
        long p1 = 0, p2 = 0, p3 = 0;
        bool p1Queued = false, p2Queued = false, p3Queued = false;
        bool p1Blocked = false, p2Blocked = false, p3Blocked = false;
        foreach (var request in _pathRequests)
        {
            long age = Math.Max(0, now - request.QueuedAtMs);
            bool blockedByNodes = nodesLeft <= 0 && !request.ServedThisStep;
            switch (request.Priority)
            {
                case PathPriority.P1:
                    p1Queued = true;
                    p1 = Math.Max(p1, age);
                    p1Blocked |= blockedByNodes;
                    break;
                case PathPriority.P2:
                    p2Queued = true;
                    p2 = Math.Max(p2, age);
                    p2Blocked |= blockedByNodes;
                    break;
                default:
                    p3Queued = true;
                    p3 = Math.Max(p3, age);
                    p3Blocked |= blockedByNodes;
                    break;
            }
        }

        if (p1Queued)
            Record(PathPriority.P1, p1, p1Blocked);
        if (p2Queued)
            Record(PathPriority.P2, p2, p2Blocked);
        if (p3Queued)
            Record(PathPriority.P3, p3, p3Blocked);

        void Record(PathPriority priority, long age, bool blocked)
        {
            KeyValuePair<string, object?> tag = PriorityTag(priority);
            FineageTelemetry.PathfindOldestAgeMilliseconds.Record(age, tag);
            if (blocked)
                FineageTelemetry.PathfindStarvedSteps.Add(1, tag);
        }
    }

    static KeyValuePair<string, object?> PriorityTag(PathPriority priority) =>
        new("priority", priority switch
        {
            PathPriority.P1 => "p1",
            PathPriority.P2 => "p2",
            _ => "p3"
        });

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
            if (search.Current == GeoPathSearch.Status.NoRoute)
            {
                // The open set emptied: this is a definitive answer for this origin, destination and geometry, so it is
                // the only outcome that may be cached. An Exhausted search ran out of iterations instead — it says the
                // budget was too small, not that no route exists, so it is never remembered.
                RememberNoRoute(npc, request.OriginX, request.OriginY, request.WantX, request.WantY, now);
                FineageTelemetry.PathfindNoRoute.Add(1);
            }

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
            FineageTelemetry.PathfindEmptyPath.Add(1);
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

    /// <summary>
    /// Answers from the cached "no route" fact for this origin, destination and geometry. A hit also drops the matching
    /// positive entry: the two caches are answers to the same question, and keeping both would let a stale route answer a
    /// pair the search has since proven unreachable.
    /// </summary>
    bool TryCachedNoRoute(ZoneEntity npc, int ox, int oy, int tx, int ty, long now)
    {
        var key = NoRouteKey(npc, ox, oy, tx, ty);
        if (!_npcNoRouteCache.TryGetValue(key, out long stamp))
            return false;
        if (now - stamp > NpcNoRouteCacheTtlMs || now < stamp)
        {
            // Expired, or the clock went backwards (a test rewinding time): do not trust it, and let the next search
            // re-establish it rather than answering from a fact of unknown age.
            EvictNoRoute(key);
            return false;
        }

        return true;
    }

    void RememberNoRoute(ZoneEntity npc, int ox, int oy, int tx, int ty, long now)
    {
        var key = NoRouteKey(npc, ox, oy, tx, ty);
        // A positive route for the same pair is stale now: the search just proved there is none.
        _npcPathCache.Remove(QuantizePathKey(ox, oy, tx, ty));
        if (_npcNoRouteCache.ContainsKey(key))
            return;
        _npcNoRouteCache[key] = now;
        _npcNoRouteCacheEvict.Enqueue((key, now));
        while (_npcNoRouteCacheEvict.Count > NpcNoRouteCacheCapacity)
            EvictNoRoute(_npcNoRouteCacheEvict.Dequeue().Key);
    }

    void EvictNoRoute(NpcNoRouteKey key) => _npcNoRouteCache.Remove(key);

    NpcNoRouteKey NoRouteKey(ZoneEntity npc, int ox, int oy, int tx, int ty)
    {
        var from = QuantizePathKey(ox, oy, tx, ty);
        return new NpcNoRouteKey(npc.InstanceId, _geo.GeometryVersion, from.FromQx, from.FromQy, from.ToQx, from.ToQy);
    }

    /// <summary>
    /// Tests: whether the pair currently has a cached "no route" answer, so a regression can assert what is remembered
    /// (and, just as important, what is not). <paramref name="now"/> of 0 means "the world clock right now".
    /// </summary>
    internal bool HasCachedNoRouteForTest(ZoneEntity npc, int ox, int oy, int tx, int ty, long now = 0) =>
        TryCachedNoRoute(npc, ox, oy, tx, ty, now > 0 ? now : NowUnixMs());

    /// <summary>Tests: the stamp of the cached "no route" answer, or null when there is none.</summary>
    internal long? NoRouteCachedStampForTest(ZoneEntity npc, int ox, int oy, int tx, int ty) =>
        _npcNoRouteCache.TryGetValue(NoRouteKey(npc, ox, oy, tx, ty), out long stamp) ? stamp : null;

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

    /// <summary>
    /// Tests and the perf harness: queue depth, running searches, and the node budget the last step spent — with the P1
    /// share separately, so a test can prove the reserve actually capped the top band instead of trusting queue contents.
    /// </summary>
    internal (int Queued, int Running, long NodesLastStep, long P1NodesLastStep) PathfindDiagnostics()
    {
        int running = 0;
        foreach (var request in _pathRequests)
        {
            if (request.Search is { Current: GeoPathSearch.Status.Running })
                running++;
        }

        return (_pathfindQueuedLastStep, running, _pathfindNodesLastStep, _pathfindP1NodesLastStep);
    }


    /// <summary>
    /// Tests: the queue in service order (priority, then arrival), so ordering regressions are observable.
    /// <c>Served</c> is whether the stage has advanced the request at least once, which is how a starvation test tells a
    /// request that is merely queued from one the budget never reached.
    /// </summary>
    internal IReadOnlyList<(int ObjectId, PathPriority Priority, int Sequence, bool Served)> PathfindQueueForTest() =>
        _pathRequests.Select(static r => (r.ObjectId, r.Priority, r.Sequence, r.WaitRecorded)).ToList();

    /// <summary>
    /// Tests: queues a route for an entity at an explicit priority. <see cref="PathPriorityOf"/> is a pure function of
    /// entity state, so a test cannot stand up a P2 band through the public pathing calls. This only creates a request;
    /// a later promotion must go through <see cref="RequestNpcGeoPath"/> so the test exercises the real re-ordering.
    /// </summary>
    internal void EnqueueNpcPathfindForTest(ZoneEntity npc, PathPriority priority, int wantX, int wantY, int wantZ)
    {
        using (_affinity.Step())
        {
            if (_pathRequestsById.ContainsKey(npc.ObjectId))
                throw new InvalidOperationException($"entity {npc.ObjectId} already has a pending route; promote it through RequestNpcGeoPath");

            npc.PathWantX = wantX;
            npc.PathWantY = wantY;
            npc.PathWantZ = wantZ;
            npc.PathfindPending = true;
            var request = new PathRequest
            {
                ObjectId = npc.ObjectId,
                WantX = wantX,
                WantY = wantY,
                WantZ = wantZ,
                QueuedAtMs = NowUnixMs(),
                Priority = priority,
                Sequence = ++_pathRequestSequence,
            };
            _pathRequestsById[npc.ObjectId] = request;
            InsertPathRequest(request);
        }
    }

    /// <summary>Tests: asks for a route through the production path, so <see cref="PathPriorityOf"/> and the enqueue decide.</summary>
    internal void RequestNpcGeoPathForTest(ZoneEntity npc, int wantX, int wantY, int wantZ)
    {
        using (_affinity.Step())
            RequestNpcGeoPath(npc, wantX, wantY, wantZ);
    }

    /// <summary>Tests: the production approach entry point, so the backoff on every caller is exercised, not just one.</summary>
    internal long ApproachTargetForTest(ZoneEntity player, ZoneEntity target, int range)
    {
        using (_affinity.Step())
            return ApproachTarget(player, target, range);
    }
}
