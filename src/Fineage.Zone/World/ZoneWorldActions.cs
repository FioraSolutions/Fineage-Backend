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
            _ when ActionProtocol.IsPanelSocial(actionId) => FanoutSocial(player, actionId),
            >= 10 and <= 61 => ActionProtocol.Unsupported,
            _ => ActionProtocol.Unknown
        };
    }

    string? ToggleSitStand(ZoneEntity player)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        // Mobius RequestActionUse case 0 / useSit: a fake-dead pawn is stood up by the sit action
        // (player.stopEffects(FAKE_DEATH)) instead of toggling the seat.
        if (player.FakeDeath)
        {
            WakeUpFromFakeDeath(player, "sit");
            return null;
        }
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
            StandUp(player);
        else
            SitDown(player, checkCast: false);

        return null;
    }

    /// <summary>
    /// Mobius <c>Player.sitDown(checkCast)</c>: sit immediately and broadcast ChangeWaitType only.
    /// Combat, sit-on-arrive, and the Fake Death wake-up stay RequestActionUse gates, not this helper.
    /// Relax / ChameleonRest pass <paramref name="checkCast"/> false so the toggle's own cast window
    /// does not refuse the sit.
    /// </summary>
    void SitDown(ZoneEntity player, bool checkCast)
    {
        if (player.Sitting)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        if (checkCast && IsCasting(player, now))
            return;
        // Mobius Player.sitDown keeps the run/walk gait; only ChangeWaitType is broadcast.
        player.Sitting = true;
        if (player.Session != null)
            player.Session.SitOnArrive = false;
        FanoutWaitType(player, ActionProtocol.WaitSitting);
    }

    /// <summary>
    /// Mobius <c>Player.standUp</c>: stop EffectType.RELAXING, then broadcast WT_STANDING.
    /// HP-full Relax exit does not stand the pawn — the effect just ends.
    /// </summary>
    void StandUp(ZoneEntity player)
    {
        // Mobius: standing up ends a private store.
        if (player.PrivateStoreType != PrivateStoreProtocol.TypeNone)
            ClosePrivateStore(player.ObjectId, standUp: false);
        StopRelaxingEffects(player);
        if (!player.Sitting)
            return;
        player.Sitting = false;
        FanoutWaitType(player, ActionProtocol.WaitStanding);
    }

    /// <summary>
    /// Mobius <c>stopEffects(EffectType.RELAXING)</c>: end every Relax / ChameleonRest toggle and fan
    /// AbnormalStatus so the client toggle glow clears.
    /// </summary>
    void StopRelaxingEffects(ZoneEntity player)
    {
        for (int i = player.Abnormals.Count - 1; i >= 0; i--)
        {
            int skillId = player.Abnormals[i].Id;
            if (!IsRelaxingEffect(skillId))
                continue;
            DeactivateToggle(player.Session, player, skillId, _game.Skills.Find(skillId), "stand", sendAbort: false);
        }
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
            NetBridge.FanoutMoveState(KnownNetPlayersOf(player), player.ObjectId,
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
        // Mobius RequestActionUse.useSocial: no store, no pending/active trade request, not casting,
        // and AI intention IDLE (not sitting / running / attacking).
        long now = _clock().ToUnixTimeMilliseconds();
        AdvanceMove(player, now);
        if (player.Sitting || player.MoveActive || IsCasting(player, now) ||
            player.PrivateStoreType != PrivateStoreProtocol.TypeNone ||
            _tradeByPlayer.ContainsKey(player.ObjectId) || _pendingTradeAsk.ContainsKey(player.ObjectId) ||
            player.Session is { AutoAttackTargetId: > 0 })
            return ActionProtocol.Blocked;

        // Mobius broadcastPacket(new SocialAction(...)): self + everyone holding the actor in view.
        NetBridge?.FanoutSocialAction(KnownNetPlayersOf(player), player.ObjectId, actionId);

        return null;
    }

    void FanoutWaitType(ZoneEntity player, int waitType)
    {
        // Mobius Player.sitDown/standUp: broadcastPacket(new ChangeWaitType(this, ...)), self included.
        string label = waitType switch
        {
            ActionProtocol.WaitSitting => "sit",
            ActionProtocol.WaitStanding => "stand",
            ActionProtocol.WaitStartFakeDeath => "fakedeath",
            ActionProtocol.WaitStopFakeDeath => "stop-fakedeath",
            _ => waitType.ToString()
        };
        ZoneLog.Info($"[wait-type] obj={player.ObjectId} type={label} x={player.X} y={player.Y} z={player.Z}");
        NetBridge?.FanoutChangeWaitType(KnownNetPlayersOf(player), player.ObjectId, waitType, player.X, player.Y, player.Z);
    }

    void FanoutMoveType(ZoneEntity player) => FanoutNetMoveType(player);

    /// <summary>
    /// Self (players) + live players in the actor's own knownlist. Equals <see cref="NetWatchersOf"/> for a player
    /// actor (player knownlists are symmetric); an NPC actor keeps no knownlist, so its audience is empty.
    /// </summary>
    List<int> KnownNetPlayersOf(ZoneEntity actor)
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
        var watchers = KnownNetPlayersOf(actor);
        if (watchers.Count == 0)
            return;
        float run = actor.InWater ? actor.SwimRunSpeed : actor.RunSpeed;
        float walk = actor.InWater ? actor.SwimWalkSpeed : actor.WalkSpeed;
        NetBridge.FanoutChangeMoveType(watchers, actor.ObjectId, actor.Running, run, walk, actor.InWater);
    }
}
