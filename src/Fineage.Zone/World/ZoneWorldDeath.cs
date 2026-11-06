using Fineage.Protocol;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryReviveAsync(ZoneSession session, ReviveRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TryRevive(session, request);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryAdminKillPlayerAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TryAdminKillPlayer(session);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    string? TryAdminKillPlayer(ZoneSession session)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (player.Dead)
            return DeathProtocol.AlreadyDead;

        ApplyPlayerDeath(player, killerObjectId: 0);
        ZoneLog.Info($"death object={player.ObjectId} admin_kill");
        return null;
    }

    string? TryRevive(ZoneSession session, ReviveRequestMsg request)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (!player.Dead)
            return DeathProtocol.NotDead;
        if (request.Action != DeathProtocol.ReviveToVillage)
            return DeathProtocol.Unsupported;

        ReviveAtVillage(player);
        ZoneLog.Info($"revive object={player.ObjectId} -> {TalkingIslandRestart.X},{TalkingIslandRestart.Y},{TalkingIslandRestart.Z}");
        return null;
    }

    void ApplyPlayerDeath(ZoneEntity player, int killerObjectId)
    {
        if (player.Kind != ZoneEntityKind.Player || player.Dead)
            return;

        player.Dead = true;
        player.CurHp = 0;
        player.Sitting = false;
        player.Running = false;
        player.ActiveEnchantObjectId = 0;
        player.Abnormals.Clear();
        RefreshRunSpeed(player); // dead: buff runSpd funcs drop with the abnormals
        player.CombatUntilUnixMs = 0;
        player.PvpFlagged = false;
        player.PvpFlagUntilMs = 0;

        if (player.Session != null)
            player.Session.SelectedTargetId = 0;

        // Mobius Playable.doDie → killer.onKillUpdatePvPKarma before death penalty / drops.
        if (killerObjectId > 0
            && _byId.TryGetValue(killerObjectId, out var killer)
            && killer.Kind == ZoneEntityKind.Player)
            OnKillUpdatePvPKarma(killer, player);

        // Mobius Player.onDeath runs the exp penalty before the Die packet goes out, so the client's exp bar and the
        // Death Penalty level are already the post-death values on the death screen.
        ApplyDeathConsequences(player, killerObjectId);
        // Mobius Player.onDieDropItem — after exp/DP, before Die packet.
        OnDieDropItem(player, killerObjectId);

        FanoutPlayerDeath(player, killerObjectId);
        if (player.Session != null)
            SaveVitals(player.Session, player);
    }

    /// <summary>
    /// Mobius <c>Creature.doRevive</c> (<c>:2524-2553</c>): the revive only lifts the bars to the configured share of
    /// the maxima when they are still below it — with <c>RespawnRestoreHP = 65</c> and <c>RespawnRestoreMP = 0</c> the
    /// character comes back on two thirds of the health bar and an empty mana bar, not on full vitals.
    /// </summary>
    void ReviveAtVillage(ZoneEntity player)
    {
        player.Dead = false;
        player.CurHp = RestoreShare(player.CurHp, player.MaxHp, _deathRules.RespawnRestoreHp);
        player.CurMp = RestoreShare(player.CurMp, player.MaxMp, _deathRules.RespawnRestoreMp);
        player.Running = true;

        FanoutRevive(player);
        // Relocate fans TeleportToLocation to the owner and every peer that held it in view.
        Relocate(player, TalkingIslandRestart.X, TalkingIslandRestart.Y, TalkingIslandRestart.Z,
            heading: TalkingIslandRestart.Heading);

        if (player.Session != null)
        {
            SelfUserInfo(player);
            FanoutVitals(player, PlayerVitalsStatus(player));
            SaveVitals(player.Session, player);
        }
    }

    void FanoutPlayerDeath(ZoneEntity dead, int killerObjectId)
    {
        var status = new StatusUpdateMsg
        {
            ObjectId = dead.ObjectId,
            AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp],
            Values = [0, dead.MaxHp]
        };

        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            if (!ReferenceEquals(watcher, dead) && !watcher.Knownlist.Contains(dead.ObjectId))
                continue;

            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutDie(netWatchers, new Fineage.Net.Contract.EntityDieMsg
            {
                EntityValue = (ulong)(uint)dead.ObjectId,
                EntityGeneration = 1,
                KillerObjectId = killerObjectId
            });
            NetBridge.FanoutStatusUpdate(netWatchers, new Fineage.Net.Contract.StatusUpdateNetMsg
            {
                EntityValue = (ulong)(uint)dead.ObjectId,
                EntityGeneration = 1,
                AttributeIds = status.AttributeIds ?? [],
                Values = status.Values ?? []
            });
        }
    }

    void FanoutRevive(ZoneEntity actor)
    {
        List<int>? netWatchers = null;
        if (actor.Session != null)
            netWatchers = [actor.ObjectId];

        foreach (var watcher in Players())
        {
            if (watcher.Session == null || ReferenceEquals(watcher, actor))
                continue;
            if (!watcher.Knownlist.Contains(actor.ObjectId))
                continue;
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 })
        {
            NetBridge?.FanoutRevive(netWatchers, new Fineage.Net.Contract.ReviveNetMsg
            {
                ObjectId = actor.ObjectId
            });
        }
    }

    /// <summary>
    /// One bar of <c>Creature.doRevive</c>: the restore only lifts a bar that is below its target share (so a
    /// resurrection spell that already restored more is never lowered), and the target is floored like Mobius'
    /// double-to-int assignment of <c>maxHp * share</c>.
    /// </summary>
    static int RestoreShare(int current, int max, double share)
    {
        if (share <= 0d || max <= 0)
            return current;
        int target = (int)(max * share);
        return current < target ? target : current;
    }
}
