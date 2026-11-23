using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius geo pathing deferred off the movement hot loop: MoveCheck on the sim thread, A* in <c>npc.paths</c>
/// with a fixed per-tick budget and a small waypoint cache.
/// </summary>
public sealed partial class ZoneWorld
{
    const int NpcPathfindBudgetPerTick = 48;
    const int NpcPathCacheCapacity = 4096;
    static readonly int GeoPathQuantize = Math.Max(128, GridRegionPartition.MapTileSize / 4);

    readonly List<int> _npcPathfindOrder = [];
    readonly HashSet<int> _npcPathfindPending = [];
    readonly Dictionary<NpcPathCacheKey, List<(int X, int Y, int Z)>> _npcPathCache = [];
    readonly Queue<NpcPathCacheKey> _npcPathCacheEvict = [];

    readonly record struct NpcPathCacheKey(int FromQx, int FromQy, int ToQx, int ToQy);

    void EnqueueNpcPathfind(int objectId)
    {
        if (!_npcPathfindPending.Add(objectId))
            return;
        _npcPathfindOrder.Add(objectId);
    }

    void DequeueNpcPathfind(int objectId)
    {
        if (!_npcPathfindPending.Remove(objectId))
            return;
        for (int i = 0; i < _npcPathfindOrder.Count; i++)
        {
            if (_npcPathfindOrder[i] != objectId)
                continue;
            _npcPathfindOrder.RemoveAt(i);
            break;
        }
    }

    void TickNpcPathfind(long now)
    {
        if (_npcPathfindOrder.Count == 0)
            return;
        int budget = NpcPathfindBudgetPerTick;
        int index = 0;
        while (budget > 0 && index < _npcPathfindOrder.Count)
        {
            int objectId = _npcPathfindOrder[index];
            if (!_npcPathfindPending.Contains(objectId))
            {
                _npcPathfindOrder.RemoveAt(index);
                continue;
            }

            if (!_byId.TryGetValue(objectId, out var npc) || !npc.PathfindPending)
            {
                npc?.PathfindPending = false;
                DequeueNpcPathfind(objectId);
                continue;
            }

            // Players share the budget. Their blocked approach used to search on the auto-attack tick.
            if (npc.Kind == ZoneEntityKind.Npc && !NpcRegionAwake(npc))
            {
                npc.PathfindPending = false;
                DequeueNpcPathfind(objectId);
                index++;
                continue;
            }

            ResolveNpcPathfind(npc, now);
            budget--;
            index++;
        }
    }

    void ResolveNpcPathfind(ZoneEntity npc, long now)
    {
        npc.PathfindPending = false;
        DequeueNpcPathfind(npc.ObjectId);
        int wantX = npc.PathWantX, wantY = npc.PathWantY, wantZ = npc.PathWantZ;
        int destX = wantX, destY = wantY, destZ = wantZ;
        npc.MovePath.Clear();
        if (_geo.HasGeo(npc.X, npc.Y))
        {
            npc.Z = _geo.GetHeight(npc.X, npc.Y, npc.Z);
            (destX, destY, destZ) = _geo.MoveCheck(npc.X, npc.Y, npc.Z, wantX, wantY, wantZ, out bool blocked);
            if (blocked)
            {
                var path = FindNpcPathCached(npc.X, npc.Y, npc.Z, wantX, wantY, wantZ);
                if (path is { Count: > 0 })
                {
                    (destX, destY, destZ) = path[0];
                    for (int i = 1; i < path.Count; i++)
                        npc.MovePath.Enqueue(path[i]);
                }
            }
        }
        else
            destZ = SanitizeDestZ(npc.X, npc.Y, npc.Z, wantX, wantY, wantZ);

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

    List<(int X, int Y, int Z)>? FindNpcPathCached(int ox, int oy, int oz, int tx, int ty, int tz)
    {
        var key = QuantizePathKey(ox, oy, tx, ty);
        if (_npcPathCache.TryGetValue(key, out var hit))
            return hit;
        var path = _geo.FindPath(ox, oy, oz, tx, ty, tz);
        if (path == null || path.Count == 0)
            return path;
        RememberNpcPathCache(key, path);
        return path;
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

    void RememberNpcPathCache(NpcPathCacheKey key, List<(int X, int Y, int Z)> path)
    {
        if (_npcPathCache.ContainsKey(key))
            return;
        _npcPathCache[key] = path;
        _npcPathCacheEvict.Enqueue(key);
        while (_npcPathCacheEvict.Count > NpcPathCacheCapacity)
        {
            var evict = _npcPathCacheEvict.Dequeue();
            _npcPathCache.Remove(evict);
        }
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

    /// <summary>MoveCheck reported blocked straight line; A* is deferred to <see cref="TickNpcPathfind"/>.</summary>
    void RequestNpcGeoPath(ZoneEntity npc, int wantX, int wantY, int wantZ)
    {
        npc.PathWantX = wantX;
        npc.PathWantY = wantY;
        npc.PathWantZ = wantZ;
        npc.PathfindPending = true;
        EnqueueNpcPathfind(npc.ObjectId);
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
}