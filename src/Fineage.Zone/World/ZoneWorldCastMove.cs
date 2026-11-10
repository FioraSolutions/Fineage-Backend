using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius Interlude cast ↔ movement rules (CreatureAI.thinkCast / PlayerAI.onIntentionMoveTo / Player.useMagic):
/// <list type="bullet">
/// <item>Starting a cast stops the run (<c>clientStopMoving(null)</c>) and turns the caster to the target.</item>
/// <item>A move order while casting is not dropped: it is saved as the next intention and runs when the cast ends.</item>
/// <item>A skill used while casting is queued (<c>setQueuedSkill</c>) and fires when the current cast ends.</item>
/// <item>Sit is refused while casting; sit while running sits on arrival (RequestActionUse NextAction EVT_ARRIVED).</item>
/// </list>
/// </summary>
public sealed partial class ZoneWorld
{
    bool IsCasting(ZoneEntity player, long now) => player.CastingUntilMs > now;

    /// <summary>Mobius thinkCast: clientStopMoving(null) + doCast heading. Watchers get a zero-length move = stop.</summary>
    void StopForCast(ZoneEntity player, ZoneEntity target, long now)
    {
        var session = player.Session;
        if (session != null)
        {
            session.ClearApproach();
            session.SitOnArrive = false;
        }
        if (target.ObjectId != player.ObjectId)
            player.Heading = ZoneCombat.HeadingFrom(player.X, player.Y, target.X, target.Y);
        AdvanceMove(player, now);
        if (!player.MoveActive)
            return;
        StopTimedMove(player);
        if (session != null)
            SyncSession(player, session);
        ZoneLog.Info($"[cast] stop-move obj={player.ObjectId} at={player.X},{player.Y}");
        FanoutStopInPlace(player);
    }

    /// <summary>Tell remote Net watchers the pawn halted here (origin == dest). The caster's own client stops on MagicSkillUse.</summary>
    void FanoutStopInPlace(ZoneEntity player)
    {
        if (NetBridge == null)
            return;
        var watchers = NetWatchersOf(player);
        watchers.Remove(player.ObjectId);
        if (watchers.Count == 0)
            return;
        NetBridge.FanoutMoveState(watchers, player.ObjectId, player.X, player.Y, player.Z,
            player.X, player.Y, player.Z, player.Running);
    }

    /// <summary>Mobius Player.useMagic while isCastingNow(): queue the skill for when the current cast ends.</summary>
    void QueueSkillAfterCast(ZoneSession session, ZoneEntity player, SkillUseIntent request)
    {
        session.PendingCastSkillId = request.SkillId;
        session.PendingCastTargetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        session.PendingCastCtrlPressed = request.CtrlPressed;
        session.PendingCastAtUnixMs = player.CastingUntilMs;
        ZoneLog.Info($"[cast] queued skill={request.SkillId} obj={player.ObjectId} at={player.CastingUntilMs}");
    }

    /// <summary>PlayerAI.onIntentionMoveTo while casting: saveNextIntention(MOVE_TO) and run it after the cast.</summary>
    void TickQueuedMoves(long now)
    {
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null)
                continue;
            if (session.QueuedMove != null && !IsCasting(player, now))
            {
                var move = session.QueuedMove;
                session.QueuedMove = null;
                if (!player.Dead)
                    TryMove(session, move.DestX, move.DestY, move.DestZ);
            }
            if (session.SitOnArrive && !player.MoveActive)
            {
                session.SitOnArrive = false;
                if (!player.Sitting && !player.Dead)
                    ToggleSitStand(player);
            }
        }
    }
}
