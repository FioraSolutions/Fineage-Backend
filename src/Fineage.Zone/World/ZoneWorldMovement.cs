using Fineage.Zone.Combat;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Movement;

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
        ReconcileKnownlist(entity, ack);
        entity.AoiRefreshX = curX;
        entity.AoiRefreshY = curY;

        entity.MoveDestX = destX;
        entity.MoveDestY = destY;
        entity.MoveDestZ = destZ;
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
        AdvanceMove(entity, _clock().ToUnixTimeMilliseconds());
        SetMoveActive(entity, false);
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
                allowXy += MoveSpeedOf(e) * ValidateLagSeconds;
            }
            return dxy > allowXy || dz > (geo ? ValidateMaxZDriftGeo : ValidateMaxZDrift);
        }
    }

    /// <summary>
    /// In-move knownlist revalidate distance (L2 units, ≈ 4.8m). Mobius updates the knownlist as the creature
    /// crosses regions / on a short timer; a distance gate keeps it to ~1 refresh per 1.5s at run speed so a
    /// long run never floods spawn/despawn while still revealing objects before they're on screen.
    /// </summary>
    internal const int AoiRefreshDistance = 250;

    void TickPlayerMovement(long now)
    {
        if (_moving.Count == 0)
            return;
        foreach (int id in _moving.ToArray())
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
                        DestX = e.MoveDestX, DestY = e.MoveDestY, DestZ = e.MoveDestZ,
                        OriginX = e.X, OriginY = e.Y, OriginZ = e.Z
                    };
                    // Keep approach sync on the current leg so client mid-path reports are not treated as cancel.
                    if (e.Session is { ApproachTargetId: > 0 } session)
                    {
                        session.ApproachOriginX = e.X;
                        session.ApproachOriginY = e.Y;
                        session.ApproachDestX = e.MoveDestX;
                        session.ApproachDestY = e.MoveDestY;
                    }
                    RefreshMovingKnownlist(e, ack);
                    continue;
                }
            }
            if (!wasMoving)
                continue;
            long dx = e.X - e.AoiRefreshX, dy = e.Y - e.AoiRefreshY;
            // Refresh on distance travelled, and once on arrival so the final position is exact.
            if (!e.MoveActive || dx * dx + dy * dy >= (long)AoiRefreshDistance * AoiRefreshDistance)
                RefreshMovingKnownlist(e, null);
        }
    }

    /// <summary>Knownlist diff at the mover's current XY; Net clients get EntitySpawn/EntityDespawn (+ MoveState for runners).</summary>
    void RefreshMovingKnownlist(ZoneEntity e, MoveToMsg? ack)
    {
        e.AoiRefreshX = e.X;
        e.AoiRefreshY = e.Y;
        var before = new HashSet<int>(e.Knownlist);
        ReconcileKnownlist(e, ack);
        if (NetBridge == null || e.Session == null)
            return;
        List<int>? added = null, removed = null;
        foreach (int id in e.Knownlist)
            if (!before.Contains(id))
                (added ??= []).Add(id);
        foreach (int id in before)
            if (!e.Knownlist.Contains(id))
                (removed ??= []).Add(id);
        if (added == null && removed == null)
            return;
        added?.Sort();
        removed?.Sort();
        ZoneLog.Info($"[aoi] move refresh obj={e.ObjectId} at={e.X},{e.Y} +{added?.Count ?? 0} -{removed?.Count ?? 0}");
        NetBridge.FanoutKnownlistDiff(e.ObjectId, (IReadOnlyList<int>?)added ?? [], (IReadOnlyList<int>?)removed ?? []);
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
            SetMoveActive(e, false);
            return;
        }

        float speed = MoveSpeedOf(e);
        double step = speed * dt / 1000.0;
        double dx = e.MoveDestX - e.X, dy = e.MoveDestY - e.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist >= 1)
            e.Heading = ZoneCombat.HeadingFrom(e.X, e.Y, e.MoveDestX, e.MoveDestY);
        if ((dist <= step || dist < 1) && e.MovePath.Count > 0)
        {
            // Reached a geo-path waypoint: snap to it and start the next leg (leftover distance is dropped, ≤1 tick).
            e.X = e.MoveDestX;
            e.Y = e.MoveDestY;
            e.Z = e.MoveDestZ;
            (e.MoveDestX, e.MoveDestY, e.MoveDestZ) = e.MovePath.Dequeue();
            int lx = e.MoveDestX, ly = e.MoveDestY, lz = e.MoveDestZ;
            if (ClipDoors(e, ref lx, ref ly, ref lz))
            {
                e.MovePath.Clear();
                (e.MoveDestX, e.MoveDestY, e.MoveDestZ) = (lx, ly, lz);
            }
            e.MoveSegmentDirty = true;
        }
        else if (dist <= step || dist < 1)
        {
            e.X = e.MoveDestX;
            e.Y = e.MoveDestY;
            e.Z = e.MoveDestZ;
            SetMoveActive(e, false);
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
        UpdateWaterState(e);
    }

    /// <summary>
    /// Mobius <c>CreatureStat.getMoveSpeed()</c> (<c>CreatureStat.java:487-495</c>): inside a WaterZone it returns
    /// <c>getSwimRunSpeed()</c>/<c>getSwimWalkSpeed()</c> by <c>isRunning()</c>, else <c>getRunSpeed()</c>/<c>getWalkSpeed()</c>.
    /// All four are the same <c>runSpd</c> stat folded onto a different base, so buffs and DEX apply everywhere.
    /// A live speed of 0 is legal (Weight Penalty 4 = <c>runSpd</c> ×0) and is not replaced by a default.
    /// </summary>
    internal static float MoveSpeedOf(ZoneEntity e)
    {
        float speed;
        if (e.InWater)
            speed = e.Running ? e.SwimRunSpeed : e.SwimWalkSpeed;
        else
            speed = e.Running ? e.RunSpeed : e.WalkSpeed;

        // Only entities with no template speed at all fall back (Mobius baseRunSpd default 120).
        bool hasBases = e.BaseRunSpeed > 0 || e.BaseWalkSpeed > 0 || e.BaseSwimRunSpeed > 0 || e.BaseSwimWalkSpeed > 0;
        return hasBases ? speed : DefaultRunSpeed;
    }

    /// <summary>
    /// Mobius <c>Creature.getStat().getRunSpeed()</c> recompute after a buff / debuff / passive / gear change;
    /// when it changed, tell the Net watchers the new gait speed (Mobius broadcast MoveToLocation/ChangeMoveType).
    /// Mid-run the entity keeps moving at the new speed on the next tick (no re-path needed).
    /// </summary>
    void RefreshRunSpeedAndFanout(ZoneEntity entity)
    {
        if (!RefreshRunSpeed(entity))
            return;
        ZoneLog.Info($"[speed] obj={entity.ObjectId} run={entity.RunSpeed} base={entity.BaseRunSpeed}");
        FanoutNetMoveType(entity);
    }

    /// <summary>Mobius WaterZone onEnter/onExit for players: flip swim state and tell Net watchers the new speed.</summary>
    void UpdateWaterState(ZoneEntity e)
    {
        if (e.Kind != ZoneEntityKind.Player || Water.Count == 0)
            return;
        bool inWater = Water.IsInWater(e.X, e.Y, e.Z);
        if (inWater == e.InWater)
            return;
        e.InWater = inWater;
        // Mobius onEnter/onExit recomputes the live speed for the new base pair: refresh first, then tell watchers.
        RefreshRunSpeed(e);
        ZoneLog.Info($"[water] obj={e.ObjectId} {(inWater ? "enter" : "exit")} at={e.X},{e.Y},{e.Z} speed={MoveSpeedOf(e)}");
        FanoutNetMoveType(e);
    }

    /// <summary>
    /// Mobius <c>checkIfDoorsBetween</c>: truncate a leg in front of the first closed door it crosses.
    /// Returns true when the leg was clipped (caller drops the remaining geo path).
    /// </summary>
    bool ClipDoors(ZoneEntity e, ref int destX, ref int destY, ref int destZ)
    {
        if (Doors.Count == 0)
            return false;
        var door = Doors.FirstBlocking(e.X, e.Y, e.Z, destX, destY, destZ, out int sx, out int sy, out int sz);
        if (door == null)
            return false;
        ZoneLog.Info($"[door] move blocked obj={e.ObjectId} door={door.Id}:{door.Name} from={e.X},{e.Y},{e.Z} want={destX},{destY},{destZ} stop={sx},{sy},{sz}");
        destX = sx;
        destY = sy;
        destZ = _geo.HasGeo(sx, sy) ? _geo.GetHeight(sx, sy, sz) : sz;
        return true;
    }

    static void SyncSession(ZoneEntity e, ZoneSession s)
    {
        s.X = e.X;
        s.Y = e.Y;
        s.Z = e.Z;
    }
}
