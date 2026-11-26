using Fineage.Zone.Combat;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Interest;
using Fineage.Zone.Movement;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Net-path timed movement (Mobius <c>Creature.moveToLocation</c> + <c>MovementTaskManager</c>, simplified):
/// a MoveInput starts a straight segment; the server advances the entity along it at run/walk speed
/// every combat tick instead of teleporting to dest. Z follows L2 geodata (<see cref="Geo.GeoEngine"/>) where region files exist, else the stub sanity clamp.
/// Blocked straight moves follow an A* geo path (<see cref="Geo.GeoEngine.FindPath"/>) one MoveToLocation leg at a time. Closed doors (<see cref="Geo.DoorTable"/>) clip every leg; inside a water zone (<see cref="Geo.WaterZones"/>) speed is the swim speed.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Max Z change allowed per unit of horizontal travel (≈ 45° slope) before we treat client Z as bogus.</summary>
    internal const double MaxZPerXy = MoveRules.MaxZPerXy;
    /// <summary>Flat Z tolerance (stairs / small steps) on top of the slope allowance.</summary>
    internal const int MaxZFlat = MoveRules.MaxZFlat;
    internal const float DefaultRunSpeed = 120f;

    /// <summary>Stub geo: client dest Z far off a sane slope from the current ground is replaced with current Z.</summary>
    internal static int SanitizeDestZ(int curX, int curY, int curZ, int destX, int destY, int destZ) =>
        MoveRules.SanitizeDestZ(curX, curY, curZ, destX, destY, destZ);

    string? StartTimedMove(ZoneEntity entity, ZoneSession session, MoveToMsg move)
    {
        long now = NowUnixMs();
        AdvanceMove(entity, now);

        int destX = move.DestX, destY = move.DestY, destZ;
        entity.MovePath.Clear();
        entity.MoveSegmentDirty = false;
        if (_geo.HasGeo(entity.X, entity.Y))
        {
            // L2 geo: server Z = ground under the current XY; dest truncated before walls / steep climbs.
            entity.Z = _geo.GetHeight(entity.X, entity.Y, entity.Z);
            (destX, destY, destZ) = _geo.MoveCheck(entity.X, entity.Y, entity.Z, move.DestX, move.DestY, move.DestZ, out bool blocked);
            if (blocked && TryNpcPathCacheOnly(entity.X, entity.Y, move.DestX, move.DestY, out var path) && path.Count > 0)
            {
                // A path computed for this cell pair already. Walk it now; a miss waits for the
                // shared per-tick A* budget instead of searching inside this step.
                ZoneHotPathLogs.GeoPath(
                    entity.ObjectId, entity.X, entity.Y, entity.Z, move.DestX, move.DestY, path.Count);
                (destX, destY, destZ) = path[0];
                for (int i = 1; i < path.Count; i++)
                    entity.MovePath.Enqueue(path[i]);
            }
            else if (blocked)
            {
                RequestNpcGeoPath(entity, move.DestX, move.DestY, move.DestZ);
                ZoneHotPathLogs.GeoMoveBlocked(
                    entity.ObjectId, entity.X, entity.Y, entity.Z,
                    move.DestX, move.DestY, move.DestZ, destX, destY, destZ);
            }
        }
        else
        {
            destZ = SanitizeDestZ(entity.X, entity.Y, entity.Z, move.DestX, move.DestY, move.DestZ);
        }

        if (ClipDoors(entity, ref destX, ref destY, ref destZ))
            entity.MovePath.Clear();

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

        // Mobius: knownlist is evaluated at the real (current) position; TickPlayerMovement revalidates it
        // every AoiRefreshDistance of travel so objects enter/leave as the runner passes them.
        // Compare the destination against the cell the knownlist was last reconciled in, not the AOI's live cell:
        // AdvanceMove above can cross a cell between ticks, and the AOI store is already in the new cell while the
        // knownlist still holds the old 3x3 watch set. Skipping on AtCell would leave that cell change unreconciled.
        if (NetBridge is { } bridge && ack != null && _aoi is GridAoi grid &&
            grid.CellKeyAt(destX, destY) == entity.AoiRefreshCell &&
            grid.CellKeyAt(entity.X, entity.Y) == entity.AoiRefreshCell)
        {
            if (_knownlistDiffActive)
            {
                _knownlistDiffAfter.Clear();
                _knownlistDiffAfter.UnionWith(entity.Knownlist);
            }

            bridge.FanoutMoveState(
                [entity.ObjectId],
                entity.ObjectId,
                ack.OriginX, ack.OriginY, ack.OriginZ,
                ack.DestX, ack.DestY, ack.DestZ,
                entity.Running);
        }
        else
            ReconcileKnownlist(entity, ack, _knownlistDiffActive ? _knownlistDiffAfter : null);
        entity.AoiRefreshX = curX;
        entity.AoiRefreshY = curY;

        entity.MoveDestX = destX;
        entity.MoveDestY = destY;
        entity.MoveDestZ = destZ;
        entity.MoveCarryX = entity.MoveCarryY = 0;
        entity.MoveLastTickMs = now;
        SetMoveActive(entity, curX != destX || curY != destY);
        if (!entity.MoveActive)
            entity.Z = destZ;
        SyncSession(entity, session);
        return null;
    }

    /// <summary>Stop in place (StopMove / attack / cast). Returns current server position.</summary>
    internal void StopTimedMove(ZoneEntity entity)
    {
        AdvanceMove(entity, NowUnixMs());
        SetMoveActive(entity, false);
        entity.MovePath.Clear();
        entity.MoveSegmentDirty = false;
        // The advance above can be the only movement of this step (client StopMove, cast/attack stop), so it can
        // cross into a cell the knownlist was never reconciled in. Arrival normally refreshes from the movement
        // stage, but a stop removes the mover from ActiveSets.Moving before that stage runs. Reconcile here or the
        // final cell's peers never get a spawn in either direction until the player happens to move again.
        if (NeedsKnownlistRefreshAfterStop(entity))
            RefreshMovingKnownlist(entity, ack: null, syncNewWatchers: false);
    }

    /// <summary>
    /// A command that advances the pawn itself (attack, cast, sit, interact) can finish the last leg inside the
    /// command instead of the movement stage. An arrived player leaves ActiveSets.Moving before that stage, so this
    /// closes the reconcile at the landing cell; a still-moving player is left to the stage's cell/distance gate.
    /// </summary>
    void ReconcileKnownlistAfterCommandAdvance(ZoneEntity entity)
    {
        if (!entity.MoveActive && NeedsKnownlistRefreshAfterStop(entity))
            RefreshMovingKnownlist(entity, ack: null, syncNewWatchers: false);
    }

    /// <summary>
    /// True when a stopping player advanced past the knownlist anchor since its last reconcile. Unlike
    /// <see cref="ShouldRefreshKnownlistAfterMove"/> this does not report a refresh for an idle stop, so client
    /// StopMove spam does not rebuild the 3x3 watch on every packet.
    /// </summary>
    bool NeedsKnownlistRefreshAfterStop(ZoneEntity entity)
    {
        if (NetBridge == null || entity.Session == null || entity.Kind != ZoneEntityKind.Player)
            return false;
        if (_aoi is Interest.GridAoi grid)
            return grid.CellKeyAt(entity.X, entity.Y) != entity.AoiRefreshCell;
        long dx = entity.X - entity.AoiRefreshX, dy = entity.Y - entity.AoiRefreshY;
        return dx * dx + dy * dy >= (long)AoiRefreshDistance * AoiRefreshDistance;
    }

    /// <summary>Net StopMove: halt at the server's interpolated position so the correction is truthful.</summary>
    internal void StopMove(ZoneSession session)
    {
        if (_byId.TryGetValue(session.ObjectId, out var e) && e.Kind == ZoneEntityKind.Player)
        {
            AdvanceMove(e, NowUnixMs());
            bool wasMoving = e.MoveActive;
            StopTimedMove(e);
            SyncSession(e, session);
            // Mobius StopMove is broadcast: peers would otherwise keep running the pawn to the old destination.
            // An idle StopMove (client spam) fans nothing.
            if (wasMoving)
                FanoutStopInPlace(e);
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
        if (!_byId.TryGetValue(session.ObjectId, out var e) || e.Kind != ZoneEntityKind.Player || e.Dead)
            return false;
        AdvanceMove(e, NowUnixMs());
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
            allowXy += MoveSpeedOf(e) * ValidateLagSeconds;
        }
        return dxy > allowXy || dz > (geo ? ValidateMaxZDriftGeo : ValidateMaxZDrift);
    }

    /// <summary>
    /// In-move knownlist revalidate distance (L2 units, ≈ 4.8m). Mobius updates the knownlist as the creature
    /// crosses regions / on a short timer; a distance gate keeps it to ~1 refresh per 1.5s at run speed so a
    /// long run never floods spawn/despawn while still revealing objects before they're on screen.
    /// </summary>
    internal const int AoiRefreshDistance = 250;

    long AoiCellKeyAt(int x, int y) => _aoi is Interest.GridAoi grid ? grid.CellKeyAt(x, y) : 0;

    /// <summary>
    /// Grid AOI: refresh knownlist when the mover crosses a cell (Mobius region revalidate). Range AOI (tests): distance gate.
    /// </summary>
    bool ShouldRefreshKnownlistAfterMove(ZoneEntity e)
    {
        if (!e.MoveActive)
            return true;
        if (_aoi is Interest.GridAoi)
            return AoiCellKeyAt(e.X, e.Y) != e.AoiRefreshCell;
        long dx = e.X - e.AoiRefreshX, dy = e.Y - e.AoiRefreshY;
        return dx * dx + dy * dy >= (long)AoiRefreshDistance * AoiRefreshDistance;
    }

    void StampKnownlistRefreshAnchor(ZoneEntity e)
    {
        e.AoiRefreshX = e.X;
        e.AoiRefreshY = e.Y;
        if (_aoi is Interest.GridAoi)
            e.AoiRefreshCell = AoiCellKeyAt(e.X, e.Y);
    }

    void TickPlayerMovement(long now)
    {
        if (_byId.CountOf(ActiveSets.Moving) == 0)
            return;
        foreach (int id in Active(ActiveSets.Moving))
        {
            if (!_byId.TryGetValue(id, out var e) || e.Kind != ZoneEntityKind.Player)
                continue;
            bool wasMoving = e.MoveActive;
            if (e.MoveActive)
                AdvanceMove(e, now);
            if (e.MoveSegmentDirty)
            {
                e.MoveSegmentDirty = false;
                if (e.MoveActive)
                {
                    // Next geo-path leg: plain MoveToLocation (MoveToAck / Net move state) from here to the waypoint.
                    var ack = new MoveToMsg
                    {
                        ObjectId = e.ObjectId,
                        DestX = e.MoveDestX,
                        DestY = e.MoveDestY,
                        DestZ = e.MoveDestZ,
                        OriginX = e.X,
                        OriginY = e.Y,
                        OriginZ = e.Z
                    };
                    // Keep approach sync on the current leg so client mid-path reports are not treated as cancel.
                    if (e.Session != null && e.Intent.ApproachTargetId > 0)
                    {
                        e.Intent.ApproachOriginX = e.X;
                        e.Intent.ApproachOriginY = e.Y;
                        e.Intent.ApproachDestX = e.MoveDestX;
                        e.Intent.ApproachDestY = e.MoveDestY;
                    }
                    RefreshMovingKnownlist(e, ack);
                    continue;
                }
            }
            if (!wasMoving)
                continue;
            if (ShouldRefreshKnownlistAfterMove(e))
                RefreshMovingKnownlist(e, null);
        }
    }

}
