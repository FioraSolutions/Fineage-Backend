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
        double dx = target.X - player.X;
        double dy = target.Y - player.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > ZoneCombat.AggroForgetRange || !TalkingIsland.WithinStep(player.X, player.Y, target.X, target.Y))
            return -1;
        double stopAt = Math.Max(10, range - ZoneCombat.ApproachSlack);
        if (dist <= stopAt)
            return 0;

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
        byte[] body = MessagePackSerializer.Serialize(ack);
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            if (ReferenceEquals(watcher, player) || watcher.Knownlist.Contains(player.ObjectId))
                outbound.Add(new Pending(watcher.Session, MsgType.MoveToAck, body));
        }

        player.X = destX;
        player.Y = destY;
        player.Z = destZ;
        if (player.Session != null)
        {
            player.Session.X = destX;
            player.Session.Y = destY;
            player.Session.Z = destZ;
        }
        _aoi.Move(player.ObjectId, destX, destY);

        float speed = player.RunSpeed > 0 ? player.RunSpeed : ZoneCombat.DefaultPlayerRunSpeed;
        return Math.Max(1, (long)Math.Ceiling((dist - stopAt) * 1000.0 / speed));
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
