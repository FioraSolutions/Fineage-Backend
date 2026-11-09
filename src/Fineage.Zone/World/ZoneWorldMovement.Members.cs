using Fineage.Zone.Combat;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Movement;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
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
