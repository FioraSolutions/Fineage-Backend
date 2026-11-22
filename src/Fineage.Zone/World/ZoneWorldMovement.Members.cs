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
        StampKnownlistRefreshAnchor(e);
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
        var addedList = (IReadOnlyList<int>?)added ?? [];
        NetBridge.FanoutKnownlistDiff(e.ObjectId, addedList, (IReadOnlyList<int>?)removed ?? []);
        FanoutMoveSyncToNewWatchers(e, addedList, ack != null);
    }

    /// <summary>Mid-run AOI adds: new player watchers see the mover at the live leg origin.</summary>
    void FanoutMoveSyncToNewWatchers(ZoneEntity mover, IReadOnlyList<int> added, bool legAckAlreadySent)
    {
        if (legAckAlreadySent || added.Count == 0 || !mover.MoveActive || NetBridge == null || mover.Session == null)
            return;

        var watchers = new List<int> { mover.ObjectId };
        foreach (int id in added)
        {
            if (_byId.TryGetValue(id, out var peer) && peer.Session != null)
                watchers.Add(id);
        }

        NetBridge.FanoutMoveState(
            watchers,
            mover.ObjectId,
            mover.X, mover.Y, mover.Z,
            mover.MoveDestX, mover.MoveDestY, mover.MoveDestZ,
            mover.Running);
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
        double distSq = dx * dx + dy * dy;
        if (distSq >= 1)
            e.Heading = ZoneCombat.HeadingFrom(e.X, e.Y, e.MoveDestX, e.MoveDestY);
        double stepSq = step * step;
        if ((distSq <= stepSq || distSq < 1) && e.MovePath.Count > 0)
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
        else if (distSq <= stepSq || distSq < 1)
        {
            e.MoveCarryX = e.MoveCarryY = 0;
            e.X = e.MoveDestX;
            e.Y = e.MoveDestY;
            e.Z = e.MoveDestZ;
            SetMoveActive(e, false);
        }
        else
        {
            double dist = Math.Sqrt(distSq);
            double k = step / dist;
            double nx = e.X + e.MoveCarryX + dx * k, ny = e.Y + e.MoveCarryY + dy * k;
            int ix = (int)Math.Round(nx), iy = (int)Math.Round(ny);
            e.MoveCarryX = nx - ix;
            e.MoveCarryY = ny - iy;
            e.X = ix;
            e.Y = iy;
            e.Z += (int)Math.Round((e.MoveDestZ - e.Z) * k);
            ApplyMoveGeoHeight(e);
        }
        MoveAoi(e.ObjectId, e.X, e.Y);
        if (e.Session != null)
        {
            SyncSession(e, e.Session);
            UpdateMoveEnvironment(e);
        }
    }

    /// <summary>
    /// Mobius <c>CreatureStat.getMoveSpeed()</c> (<c>CreatureStat.java:487-495</c>): inside a WaterZone it returns
    /// <c>getSwimRunSpeed()</c>/<c>getSwimWalkSpeed()</c> by <c>isRunning()</c>, else <c>getRunSpeed()</c>/<c>getWalkSpeed()</c>.
    /// All four are the same <c>runSpd</c> stat folded onto a different base, so buffs and DEX apply everywhere.
    /// A live speed of 0 is legal (Weight Penalty 4 = <c>runSpd</c> ×0) and is not replaced by a default.
    /// </summary>
    internal static float MoveSpeedOf(ZoneEntity e)
    {
        if (e.Mount != null)
            return e.Running ? e.Mount.RunSpeed : e.Mount.WalkSpeed;
        float speed;
        if (e.InWater)
            speed = e.Running ? e.SwimRunSpeed : e.SwimWalkSpeed;
        else
            speed = e.Running ? e.RunSpeed : e.WalkSpeed;

        if (e.InSwamp && e.SwampMoveBonus > 0 && e.SwampMoveBonus < 1)
            speed *= (float)e.SwampMoveBonus;

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

    /// <summary>GetHeight only when the mover enters a new grid cell; interpolation covers ticks inside a cell.</summary>
    void ApplyMoveGeoHeight(ZoneEntity e)
    {
        if (!_geo.HasGeo(e.X, e.Y))
            return;
        long cell = AoiCellKeyAt(e.X, e.Y);
        if (cell != 0 && cell == e.MoveGeoHeightCell)
            return;
        e.Z = _geo.GetHeight(e.X, e.Y, e.Z);
        e.MoveGeoHeightCell = cell;
    }
}
