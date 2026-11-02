using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Net-path timed movement (Mobius <c>Creature.moveToLocation</c> + <c>MovementTaskManager</c>, simplified):
/// a MoveInput starts a straight segment; the server advances the entity along it at run/walk speed
/// every combat tick instead of teleporting to dest. Z follows L2 geodata (<see cref="Geo.GeoEngine"/>) where region files exist, else the stub sanity clamp.
/// Blocked straight moves follow an A* geo path (<see cref="Geo.GeoEngine.FindPath"/>) one MoveToLocation leg at a time. Follow-ups (not here): water/doors, client ValidatePosition drift check (see ValidateClientPosition).
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Max Z change allowed per unit of horizontal travel (≈ 45° slope) before we treat client Z as bogus.</summary>
    internal const double MaxZPerXy = 1.0;
    /// <summary>Flat Z tolerance (stairs / small steps) on top of the slope allowance.</summary>
    internal const int MaxZFlat = 96;
    internal const float DefaultRunSpeed = 120f;

    /// <summary>Stub geo: client dest Z far off a sane slope from the current ground is replaced with current Z.</summary>
    internal static int SanitizeDestZ(int curX, int curY, int curZ, int destX, int destY, int destZ)
    {
        double xy = Math.Sqrt((double)(destX - curX) * (destX - curX) + (double)(destY - curY) * (destY - curY));
        double allowed = MaxZFlat + xy * MaxZPerXy;
        return Math.Abs(destZ - curZ) > allowed ? curZ : destZ;
    }

    string? StartTimedMove(ZoneEntity entity, ZoneSession session, MoveToMsg move, List<Pending> toSelf, List<Pending> toOthers)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        AdvanceMove(entity, now);

        int destX = move.DestX, destY = move.DestY, destZ;
        entity.MovePath.Clear();
        entity.MoveSegmentDirty = false;
        if (_geo.HasGeo(entity.X, entity.Y))
        {
            // L2 geo: server Z = ground under the current XY; dest truncated before walls / steep climbs.
            entity.Z = _geo.GetHeight(entity.X, entity.Y, entity.Z);
            (destX, destY, destZ) = _geo.MoveCheck(entity.X, entity.Y, entity.Z, move.DestX, move.DestY, move.DestZ, out bool blocked);
            if (blocked && _geo.FindPath(entity.X, entity.Y, entity.Z, move.DestX, move.DestY, move.DestZ) is { Count: > 0 } path)
            {
                // Mobius moveToLocation + geoPath: walk the first waypoint, queue the rest (moveToNextRoutePoint).
                ZoneLog.Info($"[geo] path obj={entity.ObjectId} from={entity.X},{entity.Y},{entity.Z} want={move.DestX},{move.DestY} nodes={path.Count} {string.Join(' ', path.Select(p => $"{p.X},{p.Y},{p.Z}"))}");
                (destX, destY, destZ) = path[0];
                for (int i = 1; i < path.Count; i++)
                    entity.MovePath.Enqueue(path[i]);
            }
            else if (blocked)
                ZoneLog.Info($"[geo] move blocked obj={entity.ObjectId} from={entity.X},{entity.Y},{entity.Z} want={move.DestX},{move.DestY},{move.DestZ} stop={destX},{destY},{destZ}");
        }
        else
        {
            destZ = SanitizeDestZ(entity.X, entity.Y, entity.Z, move.DestX, move.DestY, move.DestZ);
        }

        int curX = entity.X, curY = entity.Y, curZ = entity.Z;

        var ack = new MoveToMsg
        {
            ObjectId = entity.ObjectId,
            DestX = destX,
            DestY = destY,
            DestZ = destZ,
            OriginX = curX,
            OriginY = curY,
            OriginZ = curZ
        };

        // AOI is evaluated at the destination (same visibility as before), then the entity is put back
        // at its real position and walks there over ticks.
        entity.X = destX;
        entity.Y = destY;
        ReconcileKnownlist(entity, ack, toSelf, toOthers);
        entity.X = curX;
        entity.Y = curY;

        entity.MoveDestX = destX;
        entity.MoveDestY = destY;
        entity.MoveDestZ = destZ;
        entity.MoveLastTickMs = now;
        entity.MoveActive = curX != destX || curY != destY;
        if (!entity.MoveActive)
            entity.Z = destZ;
        SyncSession(entity, session);
        return null;
    }

    /// <summary>Stop in place (StopMove / attack / cast). Returns current server position.</summary>
    internal void StopTimedMove(ZoneEntity entity)
    {
        AdvanceMove(entity, _clock().ToUnixTimeMilliseconds());
        entity.MoveActive = false;
        entity.MovePath.Clear();
        entity.MoveSegmentDirty = false;
    }

    /// <summary>Net StopMove: halt at the server's interpolated position so the correction is truthful.</summary>
    internal void StopMove(ZoneSession session)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(session.ObjectId, out var e) && e.Kind == ZoneEntityKind.Player)
            {
                StopTimedMove(e);
                SyncSession(e, session);
            }
        }
    }

    /// <summary>
    /// Mobius ValidatePosition drift tolerance (L2 units, 1 Unity m ≈ 52.5u). Base XY allowance ≈ 2.9m;
    /// while moving we add speed × <see cref="ValidateLagSeconds"/> so heartbeat/RTT lag isn't punished.
    /// </summary>
    internal const int ValidateMaxXyDrift = 150;
    internal const int ValidateMaxZDrift = 256;
    /// <summary>With real geo the server Z is ground truth, so a client below/above it by &gt; ~2.4m is corrected (temple-floor fall).</summary>
    internal const int ValidateMaxZDriftGeo = 128;
    internal const double ValidateLagSeconds = 0.5;

    /// <summary>
    /// Net ValidatePosition: compare client-reported XYZ with the authoritative timed-move position.
    /// Returns true when drift exceeds tolerance (caller sends PositionCorrection with session XYZ).
    /// Server never adopts client XY or Z; with geo the server Z is snapped to ground.
    /// </summary>
    internal bool ValidateClientPosition(ZoneSession session, int clientX, int clientY, int clientZ, out int dxy, out int dz)
    {
        dxy = 0;
        dz = 0;
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var e) || e.Kind != ZoneEntityKind.Player || e.Dead)
                return false;
            AdvanceMove(e, _clock().ToUnixTimeMilliseconds());
            bool geo = _geo.HasGeo(e.X, e.Y);
            if (geo)
                e.Z = _geo.GetHeight(e.X, e.Y, e.Z);
            SyncSession(e, session);
            double dx = clientX - e.X, dy = clientY - e.Y;
            dxy = (int)Math.Sqrt(dx * dx + dy * dy);
            dz = Math.Abs(clientZ - e.Z);
            double allowXy = ValidateMaxXyDrift;
            if (e.MoveActive)
            {
                float speed = e.Running ? e.RunSpeed : e.WalkSpeed;
                if (speed <= 0)
                    speed = DefaultRunSpeed;
                allowXy += speed * ValidateLagSeconds;
            }
            return dxy > allowXy || dz > (geo ? ValidateMaxZDriftGeo : ValidateMaxZDrift);
        }
    }

    void TickPlayerMovement(long now, List<Pending> outbound)
    {
        foreach (var e in _byId.Values)
        {
            if (e.Kind != ZoneEntityKind.Player)
                continue;
            if (e.MoveActive)
                AdvanceMove(e, now);
            if (e.MoveSegmentDirty)
            {
                e.MoveSegmentDirty = false;
                if (!e.MoveActive)
                    continue;
                // Next geo-path leg: plain MoveToLocation (MoveToAck / Net move state) from here to the waypoint.
                var ack = new MoveToMsg
                {
                    ObjectId = e.ObjectId,
                    DestX = e.MoveDestX, DestY = e.MoveDestY, DestZ = e.MoveDestZ,
                    OriginX = e.X, OriginY = e.Y, OriginZ = e.Z
                };
                ReconcileKnownlist(e, ack, outbound, outbound);
            }
        }
    }

    void AdvanceMove(ZoneEntity e, long now)
    {
        if (!e.MoveActive)
            return;
        long dt = now - e.MoveLastTickMs;
        if (dt <= 0)
            return;
        e.MoveLastTickMs = now;
        if (e.Dead)
        {
            e.MoveActive = false;
            return;
        }

        float speed = e.Running ? e.RunSpeed : e.WalkSpeed;
        if (speed <= 0)
            speed = DefaultRunSpeed;
        double step = speed * dt / 1000.0;
        double dx = e.MoveDestX - e.X, dy = e.MoveDestY - e.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if ((dist <= step || dist < 1) && e.MovePath.Count > 0)
        {
            // Reached a geo-path waypoint: snap to it and start the next leg (leftover distance is dropped, ≤1 tick).
            e.X = e.MoveDestX;
            e.Y = e.MoveDestY;
            e.Z = e.MoveDestZ;
            (e.MoveDestX, e.MoveDestY, e.MoveDestZ) = e.MovePath.Dequeue();
            e.MoveSegmentDirty = true;
        }
        else if (dist <= step || dist < 1)
        {
            e.X = e.MoveDestX;
            e.Y = e.MoveDestY;
            e.Z = e.MoveDestZ;
            e.MoveActive = false;
        }
        else
        {
            double k = step / dist;
            e.X += (int)Math.Round(dx * k);
            e.Y += (int)Math.Round(dy * k);
            e.Z += (int)Math.Round((e.MoveDestZ - e.Z) * k);
            if (_geo.HasGeo(e.X, e.Y))
                e.Z = _geo.GetHeight(e.X, e.Y, e.Z);
        }
        _aoi.Move(e.ObjectId, e.X, e.Y);
        if (e.Session != null)
            SyncSession(e, e.Session);
    }

    static void SyncSession(ZoneEntity e, ZoneSession s)
    {
        s.X = e.X;
        s.Y = e.Y;
        s.Z = e.Z;
    }
}
