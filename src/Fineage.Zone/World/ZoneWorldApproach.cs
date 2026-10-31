using Fineage.Protocol;
using Fineage.Zone.Combat;
using MessagePack;

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
    /// Server position snaps to the stop point immediately (same model as <c>TryMove</c>).
    /// </summary>
    long ApproachTarget(ZoneEntity player, ZoneEntity target, int range, List<Pending> outbound)
    {
        if (player.Sitting || player.Dead)
            return -1;
        var session = player.Session;
        long now = _clock().ToUnixTimeMilliseconds();

        // Sticky intention: same target, still running, target has not drifted -> no re-send, same ETA.
        if (session != null && session.ApproachTargetId == target.ObjectId && now < session.ApproachArriveUnixMs &&
            ZoneCombat.Within(target.X, target.Y, session.ApproachTargetX, session.ApproachTargetY, ZoneCombat.ApproachRepathDistance))
            return session.ApproachArriveUnixMs - now;

        double dx = target.X - player.X;
        double dy = target.Y - player.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > ZoneCombat.AggroForgetRange || !TalkingIsland.WithinStep(player.X, player.Y, target.X, target.Y))
            return -1;
        double stopAt = Math.Max(10, range - ZoneCombat.ApproachSlack);
        if (dist <= stopAt)
        {
            session?.ClearApproach();
            return 0;
        }

        int destX = (int)Math.Round(target.X - dx / dist * stopAt);
        int destY = (int)Math.Round(target.Y - dy / dist * stopAt);
        int destZ = target.Z;

        if (!player.Running)
        {
            // Mobius: attack intention forces setRunning() -> ChangeMoveType so others see a run, not a walk.
            player.Running = true;
            FanoutMoveType(player, outbound);
        }

        var ack = new MoveToMsg
        {
            ObjectId = player.ObjectId,
            DestX = destX, DestY = destY, DestZ = destZ,
            OriginX = player.X, OriginY = player.Y, OriginZ = player.Z
        };
        int originX = player.X, originY = player.Y;

        player.X = destX;
        player.Y = destY;
        player.Z = destZ;
        if (session != null)
        {
            session.X = destX;
            session.Y = destY;
            session.Z = destZ;
        }
        // Knownlist reconcile (CharInfo / MoveToAck / hysteresis forget) — was a bare AOI move before, which
        // left watcher knownlists stale and let a later move DeleteObject the other PC mid-fight.
        ReconcileKnownlist(player, ack, outbound, outbound);

        float speed = player.RunSpeed > 0 ? player.RunSpeed : ZoneCombat.DefaultPlayerRunSpeed;
        long ms = Math.Max(1, (long)Math.Ceiling((dist - stopAt) * 1000.0 / speed));
        if (session != null)
        {
            session.ApproachTargetId = target.ObjectId;
            session.ApproachArriveUnixMs = now + ms;
            session.ApproachOriginX = originX;
            session.ApproachOriginY = originY;
            session.ApproachDestX = destX;
            session.ApproachDestY = destY;
            session.ApproachTargetX = target.X;
            session.ApproachTargetY = target.Y;
        }
        return ms;
    }

    /// <summary>Client MoveTo that lies on the in-flight moveToPawn path is a position report, not a new order.</summary>
    bool IsApproachSync(ZoneSession session, MoveToMsg move)
    {
        if (session.ApproachTargetId <= 0 || _clock().ToUnixTimeMilliseconds() >= session.ApproachArriveUnixMs)
            return false;
        return ZoneCombat.DistanceToSegment(move.DestX, move.DestY,
            session.ApproachOriginX, session.ApproachOriginY,
            session.ApproachDestX, session.ApproachDestY) <= ZoneCombat.ApproachPathTolerance;
    }

    /// <summary>Mobius: RequestMoveToLocation sets AI_INTENTION_MOVE_TO, dropping ATTACK/CAST unless still in melee.</summary>
    void CancelAttackIntentionOnMove(ZoneSession session, MoveToMsg move)
    {
        int targetId = session.AutoAttackTargetId > 0 ? session.AutoAttackTargetId : session.PendingCastTargetId;
        if (targetId <= 0)
        {
            session.ClearApproach();
            return;
        }
        if (_byId.TryGetValue(targetId, out var target) &&
            ZoneCombat.Within(move.DestX, move.DestY, target.X, target.Y, ZoneCombat.MeleeRange + ZoneCombat.HitRangeTolerance))
            return; // shuffling next to the target keeps the fight
        session.AutoAttackTargetId = 0;
        session.PendingCastSkillId = 0;
        session.PendingCastTargetId = 0;
        session.PendingCastAtUnixMs = 0;
        session.ClearApproach();
    }

    /// <summary>Interlude ActionFailed equivalent on V2: SystemMessage 22 so the client unlocks and shows why.</summary>
    void SendActionFailed(ZoneEntity player, List<Pending> outbound)
    {
        if (player.Session == null)
            return;
        byte[] body = MessagePackSerializer.Serialize(new SystemMessageMsg
        {
            Id = SysMsgTargetOutOfRange,
            Text = "Your target is out of range."
        });
        outbound.Add(new Pending(player.Session, MsgType.SystemMessage, body));
    }

    void TickPendingCasts(long now, List<Pending> outbound)
    {
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || session.PendingCastSkillId <= 0 || now < session.PendingCastAtUnixMs)
                continue;
            var request = new SkillUseRequestMsg
            {
                SkillId = session.PendingCastSkillId,
                TargetObjectId = session.PendingCastTargetId
            };
            session.PendingCastSkillId = 0;
            session.PendingCastTargetId = 0;
            session.PendingCastAtUnixMs = 0;
            if (TrySkillUse(session, request, outbound) != null)
                SendActionFailed(player, outbound);
        }
    }
}
