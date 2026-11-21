using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Interlude/Mobius <c>moveToPawn</c> for players: the client only sends RequestAttack / RequestMagicSkillUse
/// and never walks in by itself, so the server runs the pawn to the target (ChangeMoveType run + MoveToAck
/// to self and watchers) and fires the action on arrival (<c>onEvtArrived</c>).
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Interlude SystemMessageId 22 "Your target is out of range."</summary>
    public const int SysMsgTargetOutOfRange = 22;

    /// <summary>
    /// Start a server-driven approach so <paramref name="player"/> ends within <paramref name="range"/>
    /// of <paramref name="target"/>. Returns travel time in ms, or -1 when the target is unreachable.
    /// Uses <see cref="StartTimedMove"/> so a geo-blocked straight line becomes an A* path (Mobius moveToPawn).
    /// </summary>
    long ApproachTarget(ZoneEntity player, ZoneEntity target, int range)
    {
        if (player.Sitting || player.Dead || IsImmobilized(player))
            return -1;
        var session = player.Session;
        long now = NowUnixMs();
        // Net: bring the timed-move position up to date before measuring (the pawn may already be running).
        AdvanceMove(player, now);

        // Sticky intention: same target, still running a path, target has not drifted -> no re-send, same ETA.
        // If the pawn stopped short (wall / truncated MoveCheck) while still out of range, repath immediately.
        if (session != null && player.Intent.ApproachTargetId == target.ObjectId && now < player.Intent.ApproachArriveUnixMs &&
            ZoneCombat.Within(target.X, target.Y, player.Intent.ApproachTargetX, player.Intent.ApproachTargetY, ZoneCombat.ApproachRepathDistance))
        {
            if (player.MoveActive || ZoneCombat.Within(player.X, player.Y, target.X, target.Y, range))
                return Math.Max(1, player.Intent.ApproachArriveUnixMs - now);
        }

        double dx = target.X - player.X;
        double dy = target.Y - player.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > ZoneCombat.AggroForgetRange)
            return -1;
        double stopAt = Math.Max(10, range - ZoneCombat.ApproachSlack);
        if (dist <= stopAt)
        {
            player.Intent.ClearApproach();
            return 0;
        }

        int destX = (int)Math.Round(target.X - dx / dist * stopAt);
        int destY = (int)Math.Round(target.Y - dy / dist * stopAt);
        int destZ = target.Z;

        if (!player.Running)
        {
            // Mobius: attack intention forces setRunning() -> ChangeMoveType so others see a run, not a walk.
            player.Running = true;
            FanoutMoveType(player);
        }

        var ack = new MoveToMsg
        {
            ObjectId = player.ObjectId,
            DestX = destX,
            DestY = destY,
            DestZ = destZ,
            OriginX = player.X,
            OriginY = player.Y,
            OriginZ = player.Z
        };
        int originX = player.X, originY = player.Y;

        // Net: Mobius moveToPawn is a real timed run (MovementTaskManager), never a snap. Snapping the server
        // position here made the next ValidatePosition send a PositionCorrection onto the mob = "teleport".
        // StartTimedMove applies MoveCheck + FindPath so walls are walked around, not crashed into.
        if (session != null)
            StartTimedMove(player, session, ack);
        ZoneLog.Info(
            $"[combat] approach obj={player.ObjectId} target={target.ObjectId} from={originX},{originY} " +
            $"to={player.MoveDestX},{player.MoveDestY} path={player.MovePath.Count} dist={(int)dist}");

        float speed = MoveSpeedOf(player);
        long ms = RemainingPathTravelMs(player, speed);
        if (session != null)
        {
            player.Intent.ApproachTargetId = target.ObjectId;
            player.Intent.ApproachArriveUnixMs = now + ms;
            player.Intent.ApproachOriginX = originX;
            player.Intent.ApproachOriginY = originY;
            // Sync segment = current MoveToLocation leg (first geo waypoint), not the ideal stop behind a wall.
            player.Intent.ApproachDestX = player.MoveDestX;
            player.Intent.ApproachDestY = player.MoveDestY;
            player.Intent.ApproachTargetX = target.X;
            player.Intent.ApproachTargetY = target.Y;
        }
        return ms;
    }

    /// <summary>Travel time along the active MoveDest + queued geo waypoints at <paramref name="speed"/>.</summary>
    internal static long RemainingPathTravelMs(ZoneEntity entity, float speed)
    {
        if (speed <= 0)
            return 1;
        double len = Math.Sqrt(
            (double)(entity.MoveDestX - entity.X) * (entity.MoveDestX - entity.X) +
            (double)(entity.MoveDestY - entity.Y) * (entity.MoveDestY - entity.Y));
        int ax = entity.MoveDestX, ay = entity.MoveDestY;
        foreach (var (x, y, _) in entity.MovePath)
        {
            double dx = x - ax, dy = y - ay;
            len += Math.Sqrt(dx * dx + dy * dy);
            ax = x;
            ay = y;
        }
        if (len < 1 && !entity.MoveActive)
            return 1;
        return Math.Max(1, (long)Math.Ceiling(len * 1000.0 / speed));
    }

    /// <summary>Client MoveTo that lies on the current approach leg is a position report, not a new order.</summary>
    bool IsApproachSync(ZoneSession session, MoveToMsg move)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player))
            return false;
        var intent = player.Intent;
        if (intent.ApproachTargetId <= 0 || NowUnixMs() >= intent.ApproachArriveUnixMs)
            return false;
        return ZoneCombat.DistanceToSegment(move.DestX, move.DestY,
            intent.ApproachOriginX, intent.ApproachOriginY,
            intent.ApproachDestX, intent.ApproachDestY) <= ZoneCombat.ApproachPathTolerance;
    }

    /// <summary>Mobius: RequestMoveToLocation sets AI_INTENTION_MOVE_TO, dropping ATTACK/CAST unless still in melee.</summary>
    void CancelAttackIntentionOnMove(ZoneSession session, MoveToMsg move)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player))
            return;
        var intent = player.Intent;
        // A fresh move order also drops AI_INTENTION_PICK_UP.
        intent.PendingPickupId = 0;
        intent.PendingPickupAtUnixMs = 0;
        int targetId = intent.AutoAttackTargetId > 0 ? intent.AutoAttackTargetId : intent.PendingCastTargetId;
        if (targetId <= 0)
        {
            intent.ClearApproach();
            return;
        }
        if (_byId.TryGetValue(targetId, out var target) &&
            ZoneCombat.Within(move.DestX, move.DestY, target.X, target.Y, PlayerReach(player, target)))
            return; // shuffling next to the target keeps the fight
        intent.AutoAttackTargetId = 0;
        intent.PendingCastSkillId = 0;
        intent.PendingCastTargetId = 0;
        intent.PendingCastAtUnixMs = 0;
        intent.ClearApproach();
    }

    /// <summary>Interlude ActionFailed equivalent on V2: SystemMessage 22 so the client unlocks and shows why.</summary>
    void SendActionFailed(ZoneEntity player)
    {
        if (player.Session == null)
            return;
        NotifyLine(player.ObjectId, SysMsgTargetOutOfRange, "Your target is out of range.");
    }

    void TickPendingCasts(long now)
    {
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || player.Intent.PendingCastSkillId <= 0 || now < player.Intent.PendingCastAtUnixMs)
                continue;
            var request = new SkillUseIntent(
                player.Intent.PendingCastSkillId,
                player.Intent.PendingCastTargetId,
                player.Intent.PendingCastCtrlPressed);
            player.Intent.PendingCastSkillId = 0;
            player.Intent.PendingCastCtrlPressed = false;
            player.Intent.PendingCastTargetId = 0;
            player.Intent.PendingCastAtUnixMs = 0;
            if (TrySkillUse(session, request) != null)
                SendActionFailed(player);
        }
    }
}
