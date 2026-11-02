using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Net-path timed movement (Mobius <c>Creature.moveToLocation</c> + <c>MovementTaskManager</c>, simplified):
/// a MoveInput starts a straight segment; the server advances the entity along it at run/walk speed
/// every combat tick instead of teleporting to dest. Z is sanity-clamped (stub for GeoEngine / ValidatePosition).
/// Follow-ups (not here): L2D GeoEngine heights + canMoveToTarget, pathfinding, water/doors, client ValidatePosition packet.
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

        int destZ = SanitizeDestZ(entity.X, entity.Y, entity.Z, move.DestX, move.DestY, move.DestZ);
        int curX = entity.X, curY = entity.Y, curZ = entity.Z;

        var ack = new MoveToMsg
        {
            ObjectId = entity.ObjectId,
            DestX = move.DestX,
            DestY = move.DestY,
            DestZ = destZ,
            OriginX = curX,
            OriginY = curY,
            OriginZ = curZ
        };

        // AOI is evaluated at the destination (same visibility as before), then the entity is put back
        // at its real position and walks there over ticks.
        entity.X = move.DestX;
        entity.Y = move.DestY;
        ReconcileKnownlist(entity, ack, toSelf, toOthers);
        entity.X = curX;
        entity.Y = curY;

        entity.MoveDestX = move.DestX;
        entity.MoveDestY = move.DestY;
        entity.MoveDestZ = destZ;
        entity.MoveLastTickMs = now;
        entity.MoveActive = curX != move.DestX || curY != move.DestY;
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

    void TickPlayerMovement(long now)
    {
        foreach (var e in _byId.Values)
        {
            if (e.MoveActive && e.Kind == ZoneEntityKind.Player)
                AdvanceMove(e, now);
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
        if (dist <= step || dist < 1)
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
