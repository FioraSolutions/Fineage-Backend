using Fineage.Protocol;
using Fineage.Zone.Clan;
using Fineage.Zone.Geo;
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
        // Mobius Player.sitDown: isAttackingDisabled() (casting) → no sit.
        if (!player.Sitting && IsCasting(player, now))
            return ActionProtocol.Blocked;
        if (!player.Sitting && player.MoveActive)
        {
            AdvanceMove(player, now);
            if (player.MoveActive && player.Session != null)
            {
                // Mobius RequestActionUse case 0: moving → NextAction(EVT_ARRIVED) sit on arrival.
                player.Session.SitOnArrive = true;
                return null;
            }
        }

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

        // Settle the in-flight segment at the old speed before switching gait (Mobius setWalking/setRunning).
        long now = _clock().ToUnixTimeMilliseconds();
        AdvanceMove(player, now);
        player.Running = !player.Running;
        ZoneLog.Info($"[move] obj={player.ObjectId} {(player.Running ? "run" : "walk")} speed={MoveSpeedOf(player)}");
        FanoutMoveType(player, outbound);
        if (player.MoveActive && NetBridge != null)
        {
            // Remote Net pawns re-target the remaining leg with the new gait.
            var ack = new MoveToMsg
            {
                ObjectId = player.ObjectId,
                DestX = player.MoveDestX, DestY = player.MoveDestY, DestZ = player.MoveDestZ,
                OriginX = player.X, OriginY = player.Y, OriginZ = player.Z
            };
            NetBridge.FanoutMoveState(NetWatchersOf(player), player.ObjectId, ack.OriginX, ack.OriginY, ack.OriginZ,
                ack.DestX, ack.DestY, ack.DestZ, player.Running);
        }
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
        PublishTargetSelection(session, player, best, outbound);
        return null;
    }

    string? FanoutSocial(ZoneEntity player, int actionId, List<Pending> outbound)
    {
        var msg = new SocialActionMsg { ObjectId = player.ObjectId, ActionId = actionId };
        FanoutActor(player, MsgType.SocialAction, msg, outbound);
        // Mobius broadcastPacket(new SocialAction(...)): self + everyone holding the actor in view.
        if (NetBridge != null)
        {
            NetBridge.FanoutSocialAction(NetWatchersOf(player), new Fineage.Net.Contract.SocialActionNetMsg
            {
                EntityValue = (ulong)(uint)player.ObjectId,
                EntityGeneration = 1,
                ActionId = actionId
            });
        }

        return null;
    }

    void FanoutWaitType(ZoneEntity player, int waitType, List<Pending> outbound)
    {
        var msg = new ChangeWaitTypeMsg { ObjectId = player.ObjectId, WaitType = waitType };
        FanoutActor(player, MsgType.ChangeWaitType, msg, outbound);
        // Mobius Player.sitDown/standUp: broadcastPacket(new ChangeWaitType(this, ...)), self included.
        if (NetBridge != null)
        {
            NetBridge.FanoutChangeWaitType(NetWatchersOf(player), new Fineage.Net.Contract.ChangeWaitTypeNetMsg
            {
                EntityValue = (ulong)(uint)player.ObjectId,
                EntityGeneration = 1,
                WaitType = waitType,
                X = player.X,
                Y = player.Y,
                Z = player.Z
            });
        }
    }

    void FanoutMoveType(ZoneEntity player, List<Pending> outbound)
    {
        var msg = new ChangeMoveTypeMsg { ObjectId = player.ObjectId, Running = player.Running };
        FanoutActor(player, MsgType.ChangeMoveType, msg, outbound);
        FanoutNetMoveType(player);
    }

    /// <summary>Self (players) + knownlist players: the Net ChangeMoveType audience.</summary>
    List<int> NetWatchersOf(ZoneEntity actor)
    {
        var list = new List<int>();
        if (actor.Session != null)
            list.Add(actor.ObjectId);
        foreach (int id in actor.Knownlist)
        {
            if (_byId.TryGetValue(id, out var w) && w.Session != null && w.Kind == ZoneEntityKind.Player)
                list.Add(id);
        }
        return list;
    }

    /// <summary>
    /// Net ChangeMoveType (gait + effective run/walk speed, swim-aware) to self + watchers.
    /// Both speeds are the folded <c>runSpd</c> stat (<see cref="RefreshRunSpeed"/>), so walk buffs land too; in water
    /// the swim pair is sent, matching Mobius <c>getSwimRunSpeed()/getSwimWalkSpeed()</c>.
    /// </summary>
    void FanoutNetMoveType(ZoneEntity actor)
    {
        if (NetBridge == null)
            return;
        var watchers = NetWatchersOf(actor);
        if (watchers.Count == 0)
            return;
        float run = actor.InWater ? actor.SwimRunSpeed : actor.RunSpeed;
        float walk = actor.InWater ? actor.SwimWalkSpeed : actor.WalkSpeed;
        NetBridge.FanoutChangeMoveType(watchers, actor.ObjectId, actor.Running, run, walk, actor.InWater);
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
