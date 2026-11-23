using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Skills;
using Fineage.Game.Shops;
using Fineage.Game.Wedding;
using Fineage.Protocol;
using Fineage.Zone.Punishment;
using Fineage.Zone.Wedding;

namespace Fineage.Zone.World;

/// <summary>Mobius custom Wedding: engage, ceremony NPC, divorce, go to love.</summary>
public sealed partial class ZoneWorld
{
    const int SoeSkillId = 1050;

    readonly WeddingSettings _weddingSettings;
    readonly WeddingActor _wedding;

    internal WeddingSettings WeddingSettingsForTests => _weddingSettings;
    internal WeddingActor WeddingActorForTests => _wedding;

    internal string? WeddingNpcBypassForTests(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptEvent) =>
        WeddingNpcBypass(session, player, npc, scriptEvent);

    void PersistWedding()
    {
        if (_accounts == null)
            return;
        if (!_accounts.SaveWeddingCouples(_wedding.Snapshot(), _wedding.NextId))
            ZoneLog.Warn("wedding persist failed");
    }

    void ApplyWeddingOnEnter(ZoneEntity player)
    {
        if (!_weddingSettings.AllowWedding)
            return;
        var couple = _wedding.FindByPlayer(player.ObjectId);
        if (couple == null)
            return;
        player.CoupleId = couple.Id;
        player.PartnerId = couple.PartnerOf(player.ObjectId);
        player.Married = couple.Married;
        if (player.PartnerId != 0 && _byId.TryGetValue(player.PartnerId, out var partner) && partner.Session?.InWorld == true)
            NotifyLine(partner.ObjectId, 0, "Your partner has logged in.");
    }

    void ClearWeddingOnPlayer(ZoneEntity player)
    {
        player.CoupleId = 0;
        player.PartnerId = 0;
        player.Married = false;
        player.EngageInviterId = 0;
        player.MarryRequest = false;
        player.MarryAccepted = false;
    }

    public Task<string?> TryWeddingEngageAnswerAsync(ZoneSession session, int response, CancellationToken ct) =>
        Task.FromResult(TryWeddingEngageAnswer(session, response));

    string? TryWeddingEngageAnswer(ZoneSession session, int response)
    {
        if (!_weddingSettings.AllowWedding)
            return "disabled";
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var self))
            return "world";
        if (!_wedding.TakeEngageInvite(self.ObjectId, out int inviterId))
            return "busy";
        self.EngageInviterId = 0;
        if (!_byId.TryGetValue(inviterId, out var inviter) || inviter.Session == null || !inviter.Session.InWorld)
            return null;
        if (response != 1)
        {
            NotifyLine(inviter.ObjectId, 0, "Request to Engage has been >DENIED<!");
            return null;
        }

        if (self.PartnerId != 0 || inviter.PartnerId != 0)
            return "busy";
        var couple = _wedding.Create(inviter.ObjectId, self.ObjectId, NowUnixMs());
        inviter.PartnerId = self.ObjectId;
        self.PartnerId = inviter.ObjectId;
        inviter.CoupleId = couple.Id;
        self.CoupleId = couple.Id;
        PersistWedding();
        NotifyLine(inviter.ObjectId, 0, "Request to Engage has been >ACCEPTED<");
        return null;
    }

    public Task<string?> TryWeddingVoicedAsync(ZoneSession session, string verb, CancellationToken ct) =>
        Task.FromResult(TryWeddingVoiced(session, verb));

    string? TryWeddingVoiced(ZoneSession session, string verb)
    {
        if (!_weddingSettings.AllowWedding)
            return null;
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        return verb.ToLowerInvariant() switch
        {
            "engage" => TryEngage(player),
            "divorce" => TryDivorce(player),
            "gotolove" => TryGoToLove(player),
            _ => null
        };
    }

    string? TryEngage(ZoneEntity activeChar)
    {
        int targetId = activeChar.Session?.SelectedTargetId ?? 0;
        if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target))
            return SendWeddingLine(activeChar, "You have no one targeted.");
        bool targetMarried = target.Married;
        bool selfPartner = activeChar.PartnerId != 0;
        bool targetPartner = target.PartnerId != 0;
        bool targetPending = _wedding.HasEngageInvite(target.ObjectId) || target.EngageInviterId != 0;
        bool mutual = _friends.TryGet(activeChar.ObjectId, out var selfLists)
            && _friends.TryGet(target.ObjectId, out var targetLists)
            && WeddingRules.MutualFriends(selfLists, targetLists, activeChar.ObjectId, target.ObjectId);
        if (WeddingRules.EngageError(_weddingSettings, activeChar, target, mutual, targetPending, selfPartner, targetPartner, targetMarried)
            is { } err)
        {
            if (selfPartner && _weddingSettings.PunishInfidelity)
                ApplyInfidelityPunishment(activeChar);
            return SendWeddingLine(activeChar, err);
        }

        _wedding.SetEngageInvite(target.ObjectId, activeChar.ObjectId);
        target.EngageInviterId = activeChar.ObjectId;
        NetBridge?.FanoutWeddingEngageAsk([target.ObjectId], new WeddingEngageAskMsg
        {
            RequestorName = activeChar.Name,
            RequestorObjectId = activeChar.ObjectId
        });
        return null;
    }

    void ApplyInfidelityPunishment(ZoneEntity player)
    {
        int skillId = player.ClassId is 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 27 or 28 or 41 or 42 or 43 or 51 or 52 or 94 or 95 or 96 or 97 or 98 or 103 or 104 or 105 or 110 or 111 or 112 or 115 or 116
            ? WeddingRules.InfidelityDebuffSkillMage
            : WeddingRules.InfidelityDebuffSkillFighter;
        var template = _game.Skills.Find(skillId);
        if (template == null)
            return;
        if (!template.TryLevelData(1, out var levelData))
            levelData = new SkillLevelData(1, mpConsume: 0, power: 0);
        ApplyAbnormalFromSkill(template, levelData, player, NowUnixMs(), attacker: player, force: true);
    }

    string? TryDivorce(ZoneEntity activeChar)
    {
        if (activeChar.PartnerId == 0)
            return null;
        int partnerId = activeChar.PartnerId;
        int coupleId = activeChar.CoupleId;
        long transfer = 0;
        if (activeChar.Married)
        {
            SendWeddingLine(activeChar, "You are now divorced.");
            transfer = WeddingRules.DivorceAdenaTransfer(
                activeChar.Inventory!.CountOf(ShopTrade.AdenaItemId),
                _weddingSettings.DivorceCostPercent);
            if (transfer > 0 && activeChar.Inventory != null)
                TransferWeddingAdena(activeChar, partnerId, transfer);
        }
        else
            SendWeddingLine(activeChar, "You have broken up as a couple.");

        if (_byId.TryGetValue(partnerId, out var partner))
        {
            ClearWeddingOnPlayer(partner);
            SendWeddingLine(partner, activeChar.Married
                ? "Your spouse has decided to divorce you."
                : "Your fiance has decided to break the engagement with you.");
        }

        ClearWeddingOnPlayer(activeChar);
        if (coupleId != 0)
            _wedding.TryDelete(coupleId, out _);
        PersistWedding();
        if (transfer > 0)
        {
            if (activeChar.Session != null)
                SaveInventory(activeChar.Session, activeChar.Inventory!);
            if (partner?.Session != null && partner.Inventory != null)
                SaveInventory(partner.Session, partner.Inventory);
        }

        return null;
    }

    void TransferWeddingAdena(ZoneEntity payer, int payeeObjectId, long amount)
    {
        if (payer.Inventory == null || amount <= 0 || !_byId.TryGetValue(payeeObjectId, out var payee) || payee.Inventory == null)
            return;
        var move = BeginLedgerMove(LedgerTxKind.WeddingDivorcePenalty, payer.ObjectId, payeeObjectId);
        ObserveLedger(move, LedgerAccount.Inventory(payer.ObjectId));
        ObserveLedger(move, LedgerAccount.Inventory(payeeObjectId));
        move.Post(LedgerAccount.Inventory(payer.ObjectId), ShopTrade.AdenaItemId, -amount);
        move.Post(LedgerAccount.Inventory(payeeObjectId), ShopTrade.AdenaItemId, amount);
        if (move.CheckCommit() != LedgerRejection.None)
            return;
        int pay = checked((int)amount);
        Take(payer.Inventory, ShopTrade.AdenaItemId, pay, []);
        payee.Inventory.Add(ShopTrade.AdenaItemId, pay);
        move.Commit(LedgerHoldingsOf);
    }

    string? TryGoToLove(ZoneEntity activeChar)
    {
        long now = NowUnixMs();
        bool cursed = HasCursedWeaponForWedding(activeChar.Inventory);
        bool combat = activeChar.CombatUntilUnixMs > now;
        bool siege = SiegeBlocksPartnerTeleport(activeChar);
        if (WeddingRules.GoToLoveSelfError(
                _weddingSettings, activeChar, activeChar.Married, activeChar.PartnerId, combat, cursed,
                jailed: PlayerZoneChecks.IsPlayerJailed(this, activeChar.ObjectId),
                olympiad: PlayerInOlympiad(activeChar),
                duel: activeChar.DuelId != 0,
                observer: PlayerZoneChecks.IsOlympiadObserver(this, activeChar.ObjectId),
                siege,
                festival: PlayerZoneChecks.IsInFestivalArena(this, activeChar.ObjectId),
                dimensionalRift: PlayerZoneChecks.IsInDimensionalRift(this, activeChar.ObjectId),
                noSummonZone: ZoneRules.Query(activeChar.X, activeChar.Y, activeChar.Z, activeChar.Race).BlocksSummon,
                grandBossZone: PlayerZoneChecks.IsInGrandBossZone(this, activeChar.ObjectId))
            is { } selfErr)
            return SendWeddingLine(activeChar, selfErr);

        if (!_byId.TryGetValue(activeChar.PartnerId, out var partner) || partner.Session?.InWorld != true)
            return SendWeddingLine(activeChar, "Your partner is not online.");
        if (activeChar.InstanceId != partner.InstanceId)
            return SendWeddingLine(activeChar, "Your partner is in another World!");

        bool partnerCursed = HasCursedWeaponForWedding(partner.Inventory);
        if (WeddingRules.GoToLovePartnerError(
                partner, partnerCursed,
                jailed: PlayerZoneChecks.IsPlayerJailed(this, partner.ObjectId),
                olympiad: PlayerInOlympiad(partner),
                duel: partner.DuelId != 0,
                observer: PlayerZoneChecks.IsOlympiadObserver(this, partner.ObjectId),
                siegeInProgress: SiegeBlocksPartnerTeleport(partner),
                festival: PlayerZoneChecks.IsInFestivalArena(this, partner.ObjectId),
                dimensionalRift: PlayerZoneChecks.IsInDimensionalRift(this, partner.ObjectId),
                noSummonZone: ZoneRules.Query(partner.X, partner.Y, partner.Z, partner.Race).BlocksSummon,
                grandBossZone: PlayerZoneChecks.IsInGrandBossZone(this, partner.ObjectId),
                offline: false)
            is { } partnerErr)
            return SendWeddingLine(activeChar, partnerErr);

        if (!WeddingRules.CanPayTeleport(activeChar.Inventory!, _weddingSettings.TeleportPrice))
            return SendWeddingLine(activeChar, "You do not have enough adena.");

        RecordBagChange(
            activeChar,
            LedgerTxKind.WeddingTeleportFee,
            LedgerAccount.Of(SystemAccount.NpcShop),
            () =>
            {
                Take(activeChar.Inventory!, ShopTrade.AdenaItemId, checked((int)_weddingSettings.TeleportPrice), []);
                return true;
            },
            _ => partner.ObjectId);
        if (activeChar.Session != null)
            SaveInventory(activeChar.Session, activeChar.Inventory!);

        int castMs = _weddingSettings.TeleportDurationSeconds * 1000;
        SendWeddingLine(activeChar, $"You will be teleported to your partner in {castMs / 1000} seconds.");
        activeChar.Intent.WeddingTeleportPartnerId = partner.ObjectId;
        activeChar.Intent.WeddingTeleportAtUnixMs = now + castMs;
        NetBridge?.FanoutMagicSkillUse(WatchersOf(activeChar), new MagicSkillUseMsg
        {
            CasterObjectId = activeChar.ObjectId,
            TargetObjectId = activeChar.ObjectId,
            SkillId = SoeSkillId,
            SkillLevel = 1,
            HitTimeMs = castMs,
            ReuseDelayMs = 0
        });
        return null;
    }

    void TickPendingWeddingTeleports(long now)
    {
        foreach (var player in Players())
        {
            if (player.Intent.WeddingTeleportPartnerId <= 0 || now < player.Intent.WeddingTeleportAtUnixMs)
                continue;
            int partnerId = player.Intent.WeddingTeleportPartnerId;
            player.Intent.WeddingTeleportPartnerId = 0;
            player.Intent.WeddingTeleportAtUnixMs = 0;
            if (player.Dead || !_byId.TryGetValue(partnerId, out var partner))
                continue;
            if (SiegeBlocksPartnerTeleport(partner))
            {
                SendWeddingLine(player, "Your partner is in siege, you can't go to your partner.");
                continue;
            }

            Relocate(player, partner.X, partner.Y, partner.Z);
        }
    }

    bool SiegeBlocksPartnerTeleport(ZoneEntity entity) =>
        entity.ClanId > 0
        && _siege.Board.ExportCastles().Any(c => c.InProgress)
        && ClanInSiege(entity.ClanId);

    bool PlayerInOlympiad(ZoneEntity player) => player.InstanceId > 0 && _olympiad.Board.Stadiums.All.Any(s => s.ZoneId == player.InstanceId);

    static bool HasCursedWeaponForWedding(PlayerInventory? inventory) =>
        inventory != null &&
        (inventory.CountOf(Fineage.Protocol.OlympiadProtocol.Zariche) > 0
         || inventory.CountOf(Fineage.Protocol.OlympiadProtocol.Akamanah) > 0);

    string? SendWeddingLine(ZoneEntity player, string text)
    {
        NotifyLine(player.ObjectId, 0, text);
        return null;
    }

    string? WeddingNpcTalk(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        string html = TryPage(session.Language, "wedding/Start.htm", out var page)
            ? page.Replace("%fee%", _weddingSettings.WeddingPrice.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : "<html><body>Wedding service unavailable.</body></html>";
        SendHtml(session, player, npc, html);
        return null;
    }

    string? WeddingNpcBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptEvent)
    {
        if (!_weddingSettings.AllowWedding)
            return WeddingHtml(player, "Instructions");
        if (player.PartnerId == 0)
            return WeddingHtml(player, "NoPartner");
        if (!_byId.TryGetValue(player.PartnerId, out var partner) || partner.Session?.InWorld != true)
            return WeddingHtml(player, "NotFound");
        if (player.Married)
            return WeddingHtml(player, "Already");
        if (player.MarryAccepted)
            return WeddingHtml(player, "WaitForPartner");

        string ev = scriptEvent.ToLowerInvariant();
        if (ev is "accept")
            return WeddingNpcAccept(session, player, partner, npc);
        if (ev is "decline")
            return WeddingNpcDecline(player, partner);

        if (player.MarryRequest)
        {
            if (!WeddingRules.IsFormalWear(_weddingSettings, player.Inventory!)
                || !WeddingRules.IsFormalWear(_weddingSettings, partner.Inventory!))
                return WeddingHtml(partner, "NoFormal");
            player.MarryRequest = false;
            partner.MarryRequest = false;
            WeddingHtml(partner, "Ask", "%player%", player.Name);
            return WeddingHtml(player, "Ask", "%player%", partner.Name);
        }

        return ev switch
        {
            "ask" => WeddingNpcAsk(player, partner),
            _ => WeddingHtml(player, "Start", "%fee%", _weddingSettings.WeddingPrice.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
    }

    string? WeddingNpcAsk(ZoneEntity player, ZoneEntity partner)
    {
        if (!WeddingRules.IsFormalWear(_weddingSettings, player.Inventory!)
            || !WeddingRules.IsFormalWear(_weddingSettings, partner.Inventory!))
            return WeddingHtml(partner, "NoFormal");
        player.MarryAccepted = true;
        partner.MarryRequest = true;
        WeddingHtml(partner, "Ask", "%player%", player.Name);
        return WeddingHtml(player, "Requested", "%player%", partner.Name);
    }

    string? WeddingNpcAccept(ZoneSession session, ZoneEntity player, ZoneEntity partner, ZoneEntity npc)
    {
        if (!WeddingRules.IsFormalWear(_weddingSettings, player.Inventory!)
            || !WeddingRules.IsFormalWear(_weddingSettings, partner.Inventory!))
            return WeddingHtml(partner, "NoFormal");
        if (!WeddingRules.CanPayCeremony(player.Inventory!, partner.Inventory!, _weddingSettings.WeddingPrice))
            return WeddingHtml(partner, "Adena", "%fee%", _weddingSettings.WeddingPrice.ToString(System.Globalization.CultureInfo.InvariantCulture));

        int coupleId = player.CoupleId;
        PayWeddingFee(player, npc.NpcId, coupleId);
        PayWeddingFee(partner, npc.NpcId, coupleId);
        player.MarryAccepted = true;
        var couple = _wedding.Find(coupleId);
        if (couple == null)
        {
            ZoneLog.Warn($"wedding ceremony missing couple {coupleId} for player {player.ObjectId}");
            return SendWeddingLine(player, "Wedding data error.");
        }

        couple.Marry(NowUnixMs());
        player.Married = true;
        partner.Married = true;
        player.MarryRequest = false;
        partner.MarryRequest = false;
        PersistWedding();
        SendWeddingLine(player, "Congratulations you are married!");
        SendWeddingLine(partner, "Congratulations you are married!");
        BroadcastWeddingEffects(player, partner);
        BroadcastToAllOnline($"Congratulations to {player.Name} and {partner.Name}! They have been married.");
        WeddingHtml(partner, "Accepted");
        return WeddingHtml(player, "Accepted");
    }

    void PayWeddingFee(ZoneEntity player, int npcId, int coupleId)
    {
        if (player.Session == null || player.Inventory == null)
            return;
        RecordBagChange(player, LedgerTxKind.WeddingCeremonyFee, LedgerAccount.Of(SystemAccount.NpcShop), () =>
        {
            Take(player.Inventory!, ShopTrade.AdenaItemId, checked((int)_weddingSettings.WeddingPrice), []);
            return true;
        }, _ => coupleId);
        SaveInventory(player.Session, player.Inventory);
    }

    string? WeddingNpcDecline(ZoneEntity player, ZoneEntity partner)
    {
        player.MarryRequest = false;
        partner.MarryRequest = false;
        player.MarryAccepted = false;
        partner.MarryAccepted = false;
        SendWeddingLine(player, "You declined your partner's marriage request.");
        SendWeddingLine(partner, "Your partner declined your marriage request.");
        WeddingHtml(partner, "Declined");
        return WeddingHtml(player, "Declined");
    }

    void BroadcastWeddingEffects(ZoneEntity player, ZoneEntity partner)
    {
        NetBridge?.FanoutMagicSkillUse(WatchersOf(player), new MagicSkillUseMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = _weddingSettings.WeddingMarchSkillId,
            SkillLevel = 1,
            HitTimeMs = 1,
            ReuseDelayMs = 0
        });
        NetBridge?.FanoutMagicSkillUse(WatchersOf(partner), new MagicSkillUseMsg
        {
            CasterObjectId = partner.ObjectId,
            TargetObjectId = partner.ObjectId,
            SkillId = _weddingSettings.WeddingMarchSkillId,
            SkillLevel = 1,
            HitTimeMs = 1,
            ReuseDelayMs = 0
        });
        NetBridge?.FanoutMagicSkillUse(WatchersOf(player), new MagicSkillUseMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = _weddingSettings.LargeFireworkSkillId,
            SkillLevel = 1,
            HitTimeMs = 500,
            ReuseDelayMs = 0
        });
        NetBridge?.FanoutMagicSkillUse(WatchersOf(partner), new MagicSkillUseMsg
        {
            CasterObjectId = partner.ObjectId,
            TargetObjectId = partner.ObjectId,
            SkillId = _weddingSettings.LargeFireworkSkillId,
            SkillLevel = 1,
            HitTimeMs = 500,
            ReuseDelayMs = 0
        });
    }

    void BroadcastToAllOnline(string text)
    {
        var live = Players().Where(p => p.Session != null).Select(p => p.ObjectId).ToArray();
        if (live.Length == 0)
            return;
        NetBridge?.FanoutSystemMessage(live, new SystemMessageMsg
        {
            Id = ChatProtocol.Announcement,
            Text = text,
            Params = []
        });
    }

    string WeddingHtml(ZoneEntity player, string file, string? token = null, string? value = null)
    {
        string key = $"wedding/{file}.html";
        if (!TryPage(player.Session?.Language, key, out var html))
            html = $"<html><body>{file}</body></html>";
        if (token != null && value != null)
            html = html.Replace(token, value, StringComparison.Ordinal);
        if (player.Session != null && _byId.TryGetValue(player.Session.ObjectId, out var self))
        {
            var npc = _byId.Values.FirstOrDefault(e => e.Kind == ZoneEntityKind.Npc && e.NpcId == _weddingSettings.ManagerNpcId);
            if (npc != null)
                SendHtml(player.Session, self, npc, html);
        }

        return html;
    }
}