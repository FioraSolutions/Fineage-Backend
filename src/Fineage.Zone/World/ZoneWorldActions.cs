using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryCharacterActionAsync(
        ZoneSession session, int actionId, int targetObjectId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TryCharacterAction(session, actionId, targetObjectId);
        return Task.FromResult<string?>(error);
    }

    string? TryCharacterAction(ZoneSession session, int actionId, int targetObjectId)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (player.Dead)
            return ActionProtocol.Dead;

        return actionId switch
        {
            ActionProtocol.SitStand => ToggleSitStand(player),
            ActionProtocol.WalkRun => ToggleWalkRun(player),
            ActionProtocol.Attack => TryAttack(session, targetObjectId),
            ActionProtocol.NextTarget => TryNextTarget(session, player),
            ActionProtocol.SocialGreeting or ActionProtocol.SocialVictory or ActionProtocol.SocialAdvance
                => FanoutSocial(player, actionId),
            >= 10 and <= 61 => ActionProtocol.Unsupported,
            _ => ActionProtocol.Unknown
        };
    }

    string? ToggleSitStand(ZoneEntity player)
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
            // Mobius: standing up ends a private store.
            if (player.PrivateStoreType != PrivateStoreProtocol.TypeNone)
                ClosePrivateStore(player.ObjectId, standUp: false);
            player.Sitting = false;
            FanoutWaitType(player, ActionProtocol.WaitStanding);
        }
        else
        {
            player.Sitting = true;
            player.Running = false;
            FanoutWaitType(player, ActionProtocol.WaitSitting);
            FanoutMoveType(player);
        }

        return null;
    }

    string? ToggleWalkRun(ZoneEntity player)
    {
        if (player.Sitting)
            return ActionProtocol.Blocked;

        // Settle the in-flight segment at the old speed before switching gait (Mobius setWalking/setRunning).
        long now = _clock().ToUnixTimeMilliseconds();
        AdvanceMove(player, now);
        player.Running = !player.Running;
        ZoneLog.Info($"[move] obj={player.ObjectId} {(player.Running ? "run" : "walk")} speed={MoveSpeedOf(player)}");
        FanoutMoveType(player);
        if (player.MoveActive && NetBridge != null)
        {
            // Remote Net pawns re-target the remaining leg with the new gait.
            NetBridge.FanoutMoveState(NetWatchersOf(player), player.ObjectId,
                player.X, player.Y, player.Z,
                player.MoveDestX, player.MoveDestY, player.MoveDestZ, player.Running);
        }
        return null;
    }

    string? TryNextTarget(ZoneSession session, ZoneEntity player)
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
        PublishTargetSelection(session, player, best);
        return null;
    }

    string? FanoutSocial(ZoneEntity player, int actionId)
    {
        // Mobius broadcastPacket(new SocialAction(...)): self + everyone holding the actor in view.
        NetBridge?.FanoutSocialAction(NetWatchersOf(player), new Fineage.Net.Contract.SocialActionNetMsg
        {
            EntityValue = (ulong)(uint)player.ObjectId,
            EntityGeneration = 1,
            ActionId = actionId
        });

        return null;
    }

    void FanoutWaitType(ZoneEntity player, int waitType)
    {
        // Mobius Player.sitDown/standUp: broadcastPacket(new ChangeWaitType(this, ...)), self included.
        NetBridge?.FanoutChangeWaitType(NetWatchersOf(player), new Fineage.Net.Contract.ChangeWaitTypeNetMsg
        {
            EntityValue = (ulong)(uint)player.ObjectId,
            EntityGeneration = 1,
            WaitType = waitType,
            X = player.X,
            Y = player.Y,
            Z = player.Z
        });
    }

    void FanoutMoveType(ZoneEntity player) => FanoutNetMoveType(player);

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
}
