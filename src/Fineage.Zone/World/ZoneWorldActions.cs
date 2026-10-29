using Fineage.Protocol;
using Fineage.Zone.Clan;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryCharacterActionAsync(ZoneSession session, CharacterActionRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryCharacterAction(session, request, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    string? TryCharacterAction(ZoneSession session, CharacterActionRequestMsg request, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (player.Dead)
            return ActionProtocol.Dead;

        return request.ActionId switch
        {
            ActionProtocol.SitStand => ToggleSitStand(player, outbound),
            ActionProtocol.WalkRun => ToggleWalkRun(player, outbound),
            ActionProtocol.Attack => TryAttack(session, request.TargetObjectId, outbound),
            ActionProtocol.NextTarget => TryNextTarget(session, player, outbound),
            ActionProtocol.SocialGreeting or ActionProtocol.SocialVictory or ActionProtocol.SocialAdvance
                => FanoutSocial(player, request.ActionId, outbound),
            >= 10 and <= 61 => ActionProtocol.Unsupported,
            _ => ActionProtocol.Unknown
        };
    }

    string? ToggleSitStand(ZoneEntity player, List<Pending> outbound)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        if (!player.Sitting && ClanRules.IsInCombat(player.CombatUntilUnixMs, now))
            return ActionProtocol.Blocked;

        if (player.Sitting)
        {
            player.Sitting = false;
            FanoutWaitType(player, ActionProtocol.WaitStanding, outbound);
        }
        else
        {
            player.Sitting = true;
            player.Running = false;
            FanoutWaitType(player, ActionProtocol.WaitSitting, outbound);
            FanoutMoveType(player, outbound);
        }

        return null;
    }

    string? ToggleWalkRun(ZoneEntity player, List<Pending> outbound)
    {
        if (player.Sitting)
            return ActionProtocol.Blocked;

        player.Running = !player.Running;
        FanoutMoveType(player, outbound);
        return null;
    }

    string? TryNextTarget(ZoneSession session, ZoneEntity player, List<Pending> outbound)
    {
        int current = session.SelectedTargetId;
        ZoneEntity? best = null;
        long bestDistSq = long.MaxValue;

        foreach (int id in player.Knownlist)
        {
            if (id == current)
                continue;
            if (!_byId.TryGetValue(id, out var other))
                continue;
            if (other.Kind != ZoneEntityKind.Npc || !other.Attackable || other.CurHp <= 0)
                continue;

            long dx = (long)player.X - other.X;
            long dy = (long)player.Y - other.Y;
            long distSq = dx * dx + dy * dy;
            if (distSq >= bestDistSq)
                continue;
            bestDistSq = distSq;
            best = other;
        }

        if (best == null)
        {
            foreach (int id in player.Knownlist)
            {
                if (!_byId.TryGetValue(id, out var other))
                    continue;
                if (other.Kind != ZoneEntityKind.Npc || !other.Attackable || other.CurHp <= 0)
                    continue;
                long dx = (long)player.X - other.X;
                long dy = (long)player.Y - other.Y;
                long distSq = dx * dx + dy * dy;
                if (distSq >= bestDistSq)
                    continue;
                bestDistSq = distSq;
                best = other;
            }
        }

        if (best == null)
            return "target";

        session.SelectedTargetId = best.ObjectId;
        outbound.Add(Pack(session, MsgType.MyTargetSelected, new MyTargetSelectedMsg
        {
            ObjectId = best.ObjectId,
            ColorHint = InterludeStatus.TargetColor(player.Level, best.Level)
        }));
        return null;
    }

    string? FanoutSocial(ZoneEntity player, int actionId, List<Pending> outbound)
    {
        var msg = new SocialActionMsg { ObjectId = player.ObjectId, ActionId = actionId };
        FanoutActor(player, MsgType.SocialAction, msg, outbound);
        return null;
    }

    void FanoutWaitType(ZoneEntity player, int waitType, List<Pending> outbound)
    {
        var msg = new ChangeWaitTypeMsg { ObjectId = player.ObjectId, WaitType = waitType };
        FanoutActor(player, MsgType.ChangeWaitType, msg, outbound);
    }

    void FanoutMoveType(ZoneEntity player, List<Pending> outbound)
    {
        var msg = new ChangeMoveTypeMsg { ObjectId = player.ObjectId, Running = player.Running };
        FanoutActor(player, MsgType.ChangeMoveType, msg, outbound);
    }

    void FanoutActor(ZoneEntity actor, MsgType type, object payload, List<Pending> outbound)
    {
        byte[] body = MessagePackSerializer.Serialize(payload);
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            if (!ReferenceEquals(watcher, actor) && !watcher.Knownlist.Contains(actor.ObjectId))
                continue;
            outbound.Add(new Pending(watcher.Session, type, body));
        }
    }
}
