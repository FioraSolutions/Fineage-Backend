using Fineage.Protocol;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryReviveAsync(ZoneSession session, int action, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error = TryRevive(session, action);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryAdminKillPlayerAsync(ZoneSession session, CancellationToken ct)
    {
        using (_affinity.Step())
            return Task.FromResult(IsLive(session) ? TryAdminKillPlayer(session) : "world");
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

    /// <summary>Test hook: full player death pipeline with a killer object id.</summary>
    internal void ApplyPlayerDeathForTest(ZoneEntity player, int killerObjectId)
    {
        using (_affinity.Step())
            ApplyPlayerDeath(player, killerObjectId);
    }

    string? TryRevive(ZoneSession session, int action)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (!player.Dead)
            return DeathProtocol.NotDead;

        if (action == DeathProtocol.AcceptSkillResurrection)
            return AcceptSkillResurrection(player);
        if (action == DeathProtocol.DeclineSkillResurrection)
        {
            ClearReviveOffer(player);
            return null;
        }
        if (action == DeathProtocol.ReviveToClanHall)
        {
            var dest = ClanHallDestination(player);
            if (dest is not { } hp)
                return DeathProtocol.NoClanHall;
            ClearReviveOffer(player);
            ReviveAtVillage(player, hp.X, hp.Y, hp.Z);
            ZoneLog.Info($"revive object={player.ObjectId} action={action} -> {hp.X},{hp.Y},{hp.Z}");
            return null;
        }
        if (action == DeathProtocol.ReviveToCastle || action == DeathProtocol.ReviveToSiegeHq)
        {
            // Mobius RequestRestartPoint cases 2 / 3: castle (defender in a siege, or the owner clan) and siege HQ.
            var point = action == DeathProtocol.ReviveToCastle ? CastleRestartPoint(player) : SiegeHqRestartPoint(player);
            if (point is not { } p)
                return action == DeathProtocol.ReviveToCastle ? DeathProtocol.NoCastle : DeathProtocol.NoHeadquarters;
            ClearReviveOffer(player);
            ReviveAtVillage(player, p.X, p.Y, p.Z);
            ZoneLog.Info($"revive object={player.ObjectId} action={action} -> {p.X},{p.Y},{p.Z}");
            return null;
        }

        if (action != DeathProtocol.ReviveToVillage)
            return DeathProtocol.Unsupported;
        if (ZoneRules.Query(player.X, player.Y, player.Z, player.Race).BlocksRestart)
        {
            ZoneLog.Error($"[zone] NoRestartZone refused village restart obj={player.ObjectId} at {player.X},{player.Y},{player.Z}");
            return DeathProtocol.NoRestart;
        }

        ClearReviveOffer(player);
        var (x, y, z) = TownDestination(player);
        ReviveAtVillage(player, x, y, z);
        ZoneLog.Info($"revive object={player.ObjectId} -> {x},{y},{z}");
        return null;
    }

    void ApplyPlayerDeath(ZoneEntity player, int killerObjectId)
    {
        if (player.Kind != ZoneEntityKind.Player || player.Dead)
            return;
        // A summon's kill is its owner's kill (Mobius Summon.asPlayer for onKillUpdatePvPKarma / death penalty).
        if (killerObjectId > 0 && _byId.TryGetValue(killerObjectId, out var killerSummon) && OwnerOf(killerSummon) is { } summoner)
            killerObjectId = summoner.ObjectId;
        if (DuelKnockout(player))
            return;
        if (OlympiadKnockout(player, killerObjectId))
            return;

        EndFishing(player, win: false);
        ClearMount(player);
        player.Dead = true;
        player.CurHp = 0;
        ClosePrivateStore(player.ObjectId, standUp: false);
        // A dead player cannot finish an exchange: close the window on both sides, drop pending asks.
        TradePlayerLeftWorld(player.ObjectId);
        if (player.Session != null)
            player.Intent.SitOnArrive = false;
        player.Sitting = false;
        player.Running = false;
        player.ActiveEnchantObjectId = 0;
        player.Abnormals.Clear();
        _byId.Leave(player, ActiveSets.WithAbnormals);
        // Mobius Creature.doDie -> getEffectList().stopAllEffects() then updateEffectIcons: the corpse's abnormal bar
        // empties for every watcher, so a stunned/slept victim still plays its death animation and revives clean.
        FanoutAbnormal(player);
        RemoveAllCubics(player); // Mobius CubicAction / CubicHeal: a dead owner loses its cubics
        RefreshRunSpeed(player); // dead: buff runSpd funcs drop with the abnormals
        player.CombatUntilUnixMs = 0;
        // The kill is judged on the flag the victim died with (Mobius onKillUpdatePvPKarma reads getPvpFlag()
        // before stopPvPFlag), so remember it before death wipes it.
        bool wasFlagged = player.PvpFlagged;
        player.PvpFlagged = false;
        player.PvpFlagBlink = false;
        player.PvpFlagUntilMs = 0;
        ClearReviveOffer(player);

        if (player.Session != null)
            player.Session.SelectedTargetId = 0;

        // Mobius Playable.doDie → killer.onKillUpdatePvPKarma before death penalty / drops.
        if (killerObjectId > 0
            && _byId.TryGetValue(killerObjectId, out var killer)
            && killer.Kind == ZoneEntityKind.Player)
            OnKillUpdatePvPKarma(killer, player, wasFlagged);

        // Mobius Player.onDeath runs the exp penalty before the Die packet goes out, so the client's exp bar and the
        // Death Penalty level are already the post-death values on the death screen.
        ApplyDeathConsequences(player, killerObjectId);
        // Mobius Player.onDieDropItem — after exp/DP, before Die packet.
        OnDieDropItem(player, killerObjectId);
        if (killerObjectId > 0 && _byId.TryGetValue(killerObjectId, out var killerEntity))
            OnCursedOwnerDeath(player, killerEntity);
        else
            OnCursedOwnerDeath(player, null);
        // Mobius Player.doDie karma tempfix: a dying PK keeps 3/4 of its karma (under 200 → 0).
        if (player.Karma > 0)
        {
            player.Karma = KarmaAfterDeath(player.Karma);
            PersistKarmaPk(player);
            if (player.Karma == 0)
                wasFlagged = true; // repaint below: the red name goes white
        }
        if (wasFlagged)
            BroadcastPvpAppearance(player);

        RiftOnPlayerDeath(player);
        FanoutPlayerDeath(player, killerObjectId);
        if (player.Session != null)
        {
            SaveVitals(player.Session, player);
            // Death exp/level must land in characters.exp — SaveVitals only writes HP/MP.
            _persist.SaveProgress(
                player.Session.Account,
                player.Session.ObjectId,
                player.Level,
                player.Exp,
                player.Sp,
                player.MaxHp,
                player.CurHp,
                player.MaxMp,
                player.CurMp,
                player.MaxCp,
                player.CurCp);
        }
    }

    static void ClearReviveOffer(ZoneEntity player)
    {
        player.ReviveRequested = false;
        player.RevivePower = 0;
    }

    /// <summary>
    /// Mobius <c>Player.reviveAnswer(1)</c>: lift death in place with the offered restore percent and return lost exp.
    /// </summary>
    string? AcceptSkillResurrection(ZoneEntity player)
    {
        if (!player.ReviveRequested)
            return DeathProtocol.Unsupported;

        double power = player.RevivePower;
        ClearReviveOffer(player);
        ReviveInPlace(player, power);
        ZoneLog.Info($"revive object={player.ObjectId} skill power={power:F1}");
        return null;
    }

    /// <summary>
    /// Mobius <c>Creature.doRevive(revivePower)</c> without a village teleport: bars to RespawnRestore share,
    /// then <c>restoreExp(power)</c> returns the death-penalty slice.
    /// </summary>
    void ReviveInPlace(ZoneEntity player, double revivePower)
    {
        player.Dead = false;
        player.CurHp = RestoreShare(player.CurHp, player.MaxHp, _deathRules.RespawnRestoreHp);
        player.CurMp = RestoreShare(player.CurMp, player.MaxMp, _deathRules.RespawnRestoreMp);
        player.Running = true;
        RestoreExpFromDeath(player, revivePower);

        RiftOnPlayerRevive(player, usedTeleport: false);
        FanoutRevive(player);
        if (player.Session != null)
        {
            SelfUserInfo(player);
            FanoutVitals(player, PlayerVitalsStatus(player));
            SaveVitals(player.Session, player);
            _persist.SaveProgress(
                player.Session.Account,
                player.Session.ObjectId,
                player.Level,
                player.Exp,
                player.Sp,
                player.MaxHp,
                player.CurHp,
                player.MaxMp,
                player.CurMp,
                player.MaxCp,
                player.CurCp);
        }
    }

    /// <summary>
    /// Mobius <c>Creature.doRevive</c> + <c>teleToLocation(TOWN)</c>: bars to RespawnRestore share, then nearest
    /// map-region town square (same resolver as Scroll of Escape). No Talking Island hardcode.
    /// </summary>
    void ReviveAtVillage(ZoneEntity player, int x, int y, int z)
    {
        player.Dead = false;
        player.CurHp = RestoreShare(player.CurHp, player.MaxHp, _deathRules.RespawnRestoreHp);
        player.CurMp = RestoreShare(player.CurMp, player.MaxMp, _deathRules.RespawnRestoreMp);
        player.Running = true;

        FanoutRevive(player);
        // Relocate fans TeleportToLocation to the owner and every peer that held it in view.
        Relocate(player, x, y, z, heading: 0);
        if (PlayerInDimensionalRift(player.ObjectId) || _party.Of(player.ObjectId) is { } party && RiftBoard.PartyInRift(party.PartyId))
            RiftOnPlayerRevive(player, usedTeleport: true);

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

        var netWatchers = NetWatchersOf(dead);

        if (netWatchers.Count > 0 && NetBridge != null)
        {
            NetBridge.FanoutDie(netWatchers, dead.ObjectId, killerObjectId, RestartFlagsFor(dead));
            NetBridge.FanoutStatusUpdate(netWatchers, status);
        }
    }

    void FanoutRevive(ZoneEntity actor)
    {
        var netWatchers = NetWatchersOfRevive(actor);

        if (netWatchers is { Count: > 0 })
        {
            NetBridge?.FanoutRevive(netWatchers, actor.ObjectId);
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
