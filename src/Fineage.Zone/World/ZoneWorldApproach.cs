using Fineage.Protocol;
using Fineage.Protocol.Telemetry;
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
            return ApproachRefused(player, target, "immobile");
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
                return ApproachServed(player, target, Math.Max(1, player.Intent.ApproachArriveUnixMs - now), "sticky");
        }

        double dx = target.X - player.X;
        double dy = target.Y - player.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > ZoneCombat.AggroForgetRange)
            return ApproachRefused(player, target, "out_of_range");
        double stopAt = ApproachStopDistance(range, Math.Abs(target.Z - player.Z));
        if (dist <= stopAt)
        {
            player.Intent.ClearApproach();
            return ApproachRefused(player, target, "in_range");
        }

        // This is the single entry point for every approach (auto attack, action, skill, pickup, talk), so the backoff
        // belongs here rather than in one caller: an unreachable target is asked about from several paths, and gating
        // only the auto-attack tick left the others re-running the search. A target that drifted or a cleared intent
        // resets the wait, so a reachable target is never held back by a stale failure.
        if (session != null && player.Intent.ApproachTargetId == target.ObjectId && !ApproachRetryDue(player, target, now))
            return ApproachRefused(player, target, "backoff", Math.Max(1, player.Intent.ApproachRetryAtUnixMs - now));

        int originX = player.X, originY = player.Y;

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

        // Net: Mobius moveToPawn is a real timed run (MovementTaskManager), never a snap. Snapping the server
        // position here made the next ValidatePosition send a PositionCorrection onto the mob = "teleport".
        // StartTimedMove applies MoveCheck + FindPath so walls are walked around, not crashed into.
        if (session != null)
            StartTimedMove(player, session, ack);
        ZoneLog.Info(
            $"[combat] approach obj={player.ObjectId} target={target.ObjectId} from={originX},{originY} " +
            $"to={player.MoveDestX},{player.MoveDestY} path={player.MovePath.Count} dist={(int)dist}");

        // A MoveCheck that clipped the whole straight line back onto the origin is the shape of an unreachable target:
        // the pawn stands still and asks again on the next tick unless the backoff holds it.
        if (player.MoveDestX == originX && player.MoveDestY == originY)
            FineageTelemetry.ApproachClippedToOrigin.Add(1);

        float speed = MoveSpeedOf(player);
        long ms = RemainingPathTravelMs(player, speed);
        bool served = false;
        if (session != null)
        {
            // Whether this attempt actually got the pawn moving. A MoveCheck that clipped the destination back onto the
            // origin leaves no move and no queued leg. A queued search is deliberately NOT counted as progress: it is a
            // temporary state, and when it resolves without a route the pawn is still standing exactly where it was —
            // counting it here reset the attempt counter every tick and the backoff never engaged.
            bool progressed = player.MoveActive || player.MovePath.Count > 0;
            served = progressed;
            if (progressed)
            {
                player.Intent.ApproachAttempts = 0;
                player.Intent.ApproachRetryAtUnixMs = 0;
            }
            else
            {
                player.Intent.ApproachAttempts++;
                player.Intent.ApproachRetryAtUnixMs = now + ApproachRetryDelayMs(player.Intent.ApproachAttempts);
            }

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
        return ApproachServed(player, target, ms, served ? "served" : "pending");
    }

    static long ApproachRefused(ZoneEntity player, ZoneEntity target, string outcome) =>
        ApproachRefused(player, target, outcome, outcome is "in_range" ? 0 : -1);

    /// <summary>
    /// Where an approach stops in the XY plane for a reach <paramref name="reach"/> and a target
    /// <paramref name="zDelta"/> above or below the pawn. Mobius <c>Creature.moveToLocation</c>
    /// (<c>Creature.java:4346-4368</c>) first folds the height into the offset —
    /// <c>offset -= |dz|; if (offset &lt; 5) offset = 5;</c> — and then backs one more unit off
    /// (<c>distance -= offset - 5</c>). So a pawn standing below a ledge keeps walking all the way under the target and
    /// climbs the ramp, while a level approach keeps the usual <see cref="ZoneCombat.ApproachSlack"/> margin. Taking the
    /// height out of the *stop distance* is what makes a 3D interaction gate satisfiable; a flat stop under the ledge
    /// never gets inside the sphere no matter how long the pawn waits.
    /// </summary>
    internal static double ApproachStopDistance(int reach, int zDelta)
    {
        double offset = Math.Max(5, reach - Math.Abs(zDelta));
        return Math.Max(10, offset - 5);
    }

    static long ApproachRefused(ZoneEntity player, ZoneEntity target, string outcome, long result)
    {
        FineageTelemetry.ApproachAttempts.Add(1, OutcomeTag(outcome));
        return result;
    }

    static long ApproachServed(ZoneEntity player, ZoneEntity target, long ms, string outcome)
    {
        FineageTelemetry.ApproachAttempts.Add(1, OutcomeTag(outcome));
        return ms;
    }

    static KeyValuePair<string, object?> OutcomeTag(string outcome) => new("outcome", outcome);

    /// <summary>
    /// Wait before retrying a target this pawn has failed to close on <paramref name="attempts"/> times in a row:
    /// <see cref="ZoneCombat.ApproachRetryMinMs"/> doubled per attempt, capped at
    /// <see cref="ZoneCombat.ApproachRetryMaxMs"/>. Bounded so the cap costs no allocation or loop over a long failure run.
    /// </summary>
    internal static long ApproachRetryDelayMs(int attempts)
    {
        if (attempts <= 1)
            return ZoneCombat.ApproachRetryMinMs;
        int shift = Math.Min(attempts - 1, 8);
        long delay = (long)ZoneCombat.ApproachRetryMinMs << shift;
        return Math.Min(delay, ZoneCombat.ApproachRetryMaxMs);
    }

    /// <summary>
    /// Whether a fresh approach attempt for <paramref name="target"/> is allowed now. A retry blocked by the backoff is
    /// only held while the target stays put: a target that moved, or an attempt still in flight, clears the wait.
    /// </summary>
    internal static bool ApproachRetryDue(ZoneEntity player, ZoneEntity target, long now)
    {
        var intent = player.Intent;
        if (intent.ApproachTargetId != target.ObjectId)
            return true;
        if (intent.ApproachRetryAtUnixMs <= 0 || now >= intent.ApproachRetryAtUnixMs)
            return true;
        // The target drifted: the previous failure says nothing about this new position.
        return !ZoneCombat.Within(target.X, target.Y, intent.ApproachTargetX, intent.ApproachTargetY,
            ZoneCombat.ApproachRepathDistance);
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
