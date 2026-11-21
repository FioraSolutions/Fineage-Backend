using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Inventory;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryTeleportSelectAsync(
        ZoneSession session, int npcObjectId, string? listName, int locationId, CancellationToken ct)
    {
        // Teleport fee may SaveInventory → flush after the change (B8).
        return Task.FromResult(WithDeferredInventoryPersist(
            () => TeleportTo(session, npcObjectId, listName ?? "", locationId, html: false)));
    }

    public Task<string?> TryWarehouseDepositAsync(
        ZoneSession session, int npcObjectId, WarehouseLine[] lines, CancellationToken ct)
    {
        // The Postgres inventory write runs after the change, once per step (B8).
        return Task.FromResult(WithDeferredInventoryPersist(
            () => DepositPrivate(session, npcObjectId, lines, html: false)));
    }

    public Task<string?> TryWarehouseWithdrawAsync(
        ZoneSession session, int npcObjectId, WarehouseLine[] lines, CancellationToken ct)
    {
        return Task.FromResult(WithDeferredInventoryPersist(
            () => WithdrawPrivate(session, npcObjectId, lines, html: false)));
    }

    public Task<string?> TrySupportMagicAsync(ZoneSession session, int npcObjectId, CancellationToken ct)
    {
        // Keep same defer pattern as other NPC services (B8 consistency).
        return Task.FromResult(WithDeferredInventoryPersist(
            () => ApplySupportMagic(session, npcObjectId, html: false)));
    }

    public Task<string?> TryGiveBlessingAsync(ZoneSession session, int npcObjectId, CancellationToken ct)
    {
        return Task.FromResult(WithDeferredInventoryPersist(
            () => ApplyBlessing(session, npcObjectId, html: false)));
    }

    string? ApplyNpcService(
        ZoneSession session,
        ZoneEntity player,
        ZoneEntity npc,
        BypassReply reply)
    {
        switch (reply.Service)
        {
            case NpcServiceIntent.TeleportList:
                return ShowTeleportList(session, player, npc, reply.ServiceArg);
            case NpcServiceIntent.Teleport:
                return TeleportTo(session, npc.ObjectId, reply.ServiceArg, reply.ServiceId, html: true);
            case NpcServiceIntent.Deposit:
                return OpenDeposit(session, player, npc);
            case NpcServiceIntent.Withdraw:
                return OpenWithdraw(session, player, npc);
            case NpcServiceIntent.PackageDeposit:
                return OpenPackageDeposit(session, player, npc);
            case NpcServiceIntent.PackageWithdraw:
                return OpenPackageWithdraw(session, player, npc);
            case NpcServiceIntent.SupportMagic:
                return ApplySupportMagic(session, npc.ObjectId, html: true);
            case NpcServiceIntent.SupportBlessing:
                return ShowBlessingPage(session, player, npc);
            case NpcServiceIntent.GiveBlessing:
                return ApplyBlessing(session, npc.ObjectId, html: true);
            case NpcServiceIntent.TerritoryStatus:
                return ShowTerritoryStatus(session, player, npc);
            case NpcServiceIntent.Loto:
                return ShowLoto(session, player, npc, Math.Max(0, reply.ServiceId));
            case NpcServiceIntent.MonsterRace:
                return MonsterRaceBypass(session, player, npc, reply.ServiceArg);
            case NpcServiceIntent.NoblesSelect:
                SendNoblesPage(session, player, npc, player.Noble);
                return null;
            case NpcServiceIntent.RequestNobless:
                return RequestNoblessBypass(session, player, npc);
            case NpcServiceIntent.Olympiad:
                return OlympiadNpcBypass(session, player, npc, reply.ServiceArg);
            case NpcServiceIntent.Subclass:
                return SubclassBypass(session, player, npc, reply.ServiceId, reply.ServiceArg);
            case NpcServiceIntent.AncientAdenaToAdena:
                return AncientAdenaToAdena(session, player, npc, reply.ServiceId);
            case NpcServiceIntent.AugmentMake:
                return TryAugmentOpenMake(session);
            case NpcServiceIntent.AugmentCancel:
                return TryAugmentOpenCancel(session);
            case NpcServiceIntent.FishSkillList:
                return ShowFishingSkills(session, player, npc);
            case NpcServiceIntent.FishingChampionship:
                return ShowFishingChampionship(session, player, npc, claim: false);
            case NpcServiceIntent.FishingChampionshipClaim:
                return ShowFishingChampionship(session, player, npc, claim: true);
            case NpcServiceIntent.SkillList:
                return ShowTrainerSkills(session, player, npc);
            case NpcServiceIntent.LearnSkill:
                return LearnTrainerSkill(session, player, npc, reply.ServiceId);
            case NpcServiceIntent.WyvernRide:
                return TryWyvernRideFromManager(session, player, npc);
            case NpcServiceIntent.PetEvolve:
                return ApplyPetEvolve(session, player, npc, reply.ServiceId);
            case NpcServiceIntent.DimensionalRift:
                return ApplyDimensionalRift(session, player, npc, reply);
            case NpcServiceIntent.Festival:
                return ApplyFestival(session, player, npc, reply);
            case NpcServiceIntent.DoormanOpen:
                return TryDoormanDoors(session, player, npc, reply.ServiceArg, open: true);
            case NpcServiceIntent.DoormanClose:
                return TryDoormanDoors(session, player, npc, reply.ServiceArg, open: false);
            case NpcServiceIntent.HallFunction:
            case NpcServiceIntent.HallDeco:
            case NpcServiceIntent.HallLease:
            case NpcServiceIntent.HallSupport:
            case NpcServiceIntent.HallTeleport:
            case NpcServiceIntent.HallItemCreate:
                return TryHallManagerService(session, player, npc, reply);
            default:
                return "bad_request";
        }
    }

    /// <summary>Test: run one bypass command against the first spawned NPC with this template id.</summary>
    internal string? NpcServiceForTest(ZoneSession session, int npcId, string intent, string mapId = "elven_village")
    {
        using (_affinity.Step())
        {
            if (!_byId.TryGetValue(session.ObjectId, out var player))
                return "world";
            var npc = _byId.Values.FirstOrDefault(e => e.Kind == ZoneEntityKind.Npc && e.NpcId == npcId)
                ?? new ZoneEntity
                {
                    Kind = ZoneEntityKind.Npc,
                    NpcId = npcId,
                    ObjectId = 0,
                    MerchantCastleId = Fineage.Zone.Shops.MerchantTowns.For(mapId).CastleId,
                    MerchantTown = Fineage.Zone.Shops.MerchantTowns.TownName(mapId)
                };
            var reply = BypassReply.ServiceCall(intent);
            return ApplyNpcService(session, player, npc, reply);
        }
    }

    string? ShowTerritoryStatus(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var template = Fineage.Zone.Siege.CastleCatalog.Find(npc.MerchantCastleId);
        if (template == null)
        {
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>This area belongs to no castle.</body></html>");
            return null;
        }

        var castle = _siege.Board.Find(npc.MerchantCastleId);
        bool owned = castle != null && castle.OwnerClanId > 0;
        string file = owned ? "territorystatus.htm" : "territorynoclan.htm";
        if (!TryPage(session.Language, file, out var html))
            html = "<html><body>%castlename%</body></html>";
        string clanName = "", leaderName = "";
        if (owned && _clan.TryGet(castle!.OwnerClanId, out var clan))
        {
            clanName = clan.Name;
            leaderName = clan.Members.FirstOrDefault(m => m.CharacterId == clan.LeaderId)?.Name ?? "";
        }

        html = html.Replace("%castlename%", HtmlText(template.Value.Name), StringComparison.Ordinal)
            .Replace("%territory%", HtmlText(npc.MerchantTown ?? template.Value.Name), StringComparison.Ordinal)
            .Replace("%clanname%", HtmlText(clanName), StringComparison.Ordinal)
            .Replace("%clanleadername%", HtmlText(leaderName), StringComparison.Ordinal)
            .Replace("%taxpercent%", owned ? castle!.TaxPercent.ToString() : "0", StringComparison.Ordinal);
        SendHtml(session, player, npc, html);
        return null;
    }

    /// <summary>
    /// Mobius <c>AltKarmaPlayerCanUseGK = False</c> (Player.ini default): a gatekeeper refuses anyone with karma.
    /// Shops, warehouses, trade and Scroll of Escape stay open (their AltKarmaPlayerCan* defaults are True).
    /// </summary>
    bool KarmaGkRefused(ZoneSession session, ZoneEntity player, ZoneEntity npc, bool html)
    {
        if (player.Karma <= 0)
            return false;
        Notice(session, NpcServiceProtocol.KarmaTeleportRefused, NpcServiceProtocol.KarmaTeleportRefusedText);
        if (html)
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.KarmaTeleportRefusedText + "</body></html>");
        return true;
    }

    string? ShowTeleportList(ZoneSession session, ZoneEntity player, ZoneEntity npc, string listName)
    {
        if (KarmaGkRefused(session, player, npc, html: true))
            return null;
        var list = _teleports.Find(npc.NpcId, string.IsNullOrWhiteSpace(listName) ? TeleportRules.Normal : listName);
        if (list == null)
        {
            Notice(session, NpcServiceProtocol.UnknownTeleport, NpcServiceProtocol.UnknownTeleportText);
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>That teleport is not available.</body></html>");
            return null;
        }

        if (TeleportRules.IsNobles(list.Type) && !player.Noble)
        {
            Notice(session, NpcServiceProtocol.NoblesOnly, NpcServiceProtocol.NoblesOnlyText);
            SendNoblesPage(session, player, npc, noble: false);
            return null;
        }

        var when = WallNow();
        var rows = new TeleportLocationMsg[list.Locations.Count];
        var html = new System.Text.StringBuilder();
        html.Append("<html><body>%npcname%:<br>");
        for (int i = 0; i < list.Locations.Count; i++)
        {
            var loc = list.Locations[i];
            int fee = TeleportRules.Quote(list.Type, player.Level, loc.FeeCount, when, player.SubClassActive);
            rows[i] = new TeleportLocationMsg
            {
                Id = loc.Id,
                Name = loc.Name,
                X = loc.X,
                Y = loc.Y,
                Z = loc.Z,
                FeeItemId = loc.FeeItemId,
                FeeCount = fee
            };
            string label = string.IsNullOrWhiteSpace(loc.Name) ? "Teleport" : HtmlText(loc.Name);
            if (fee > 0)
                label += " - " + fee + " " + FeeName(loc.FeeItemId);
            html.Append("<a action=\"bypass -h npc_%objectId%_teleport ")
                .Append(HtmlText(list.Name))
                .Append(' ')
                .Append(loc.Id)
                .Append("\">")
                .Append(label)
                .Append("</a><br1>");
        }

        html.Append("</body></html>");
        SendHtml(session, player, npc, html.ToString());
        return null;
    }

    string? TeleportTo(
        ZoneSession session,
        int npcObjectId,
        string listName,
        int locationId,
        bool html)
    {
        if (!TryServicePair(session, npcObjectId, out var player, out var npc, out var error))
            return error;
        if (player.Inventory == null)
            return "world";
        if (KarmaGkRefused(session, player, npc, html))
            return null;

        var list = _teleports.Find(npc.NpcId, string.IsNullOrWhiteSpace(listName) ? TeleportRules.Normal : listName);
        if (list == null || (TeleportRules.IsNobles(list.Type) && !player.Noble))
        {
            int id = list != null ? NpcServiceProtocol.NoblesOnly : NpcServiceProtocol.UnknownTeleport;
            string text = list != null ? NpcServiceProtocol.NoblesOnlyText : NpcServiceProtocol.UnknownTeleportText;
            Notice(session, id, text);
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + text + "</body></html>");
            return null;
        }

        var loc = list.Find(locationId);
        if (loc == null)
        {
            Notice(session, NpcServiceProtocol.UnknownTeleport, NpcServiceProtocol.UnknownTeleportText);
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>That teleport is not available.</body></html>");
            return null;
        }

        if (SiegeBlocks(loc))
        {
            Notice(session, NpcServiceProtocol.SiegeBlocked, NpcServiceProtocol.SiegeBlockedText);
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>You cannot teleport while that castle is under siege.</body></html>");
            return null;
        }

        var (x, y) = Offset(player, loc.X, loc.Y);
        if (!Loaded(x, y))
        {
            Notice(session, NpcServiceProtocol.DestinationUnloaded, NpcServiceProtocol.DestinationUnloadedText);
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>That destination is not on a loaded map.</body></html>");
            return null;
        }

        int fee = TeleportRules.Quote(list.Type, player.Level, loc.FeeCount, WallNow(), player.SubClassActive);
        if (fee > 0 && player.Inventory.CountOf(loc.FeeItemId) < fee)
        {
            bool adena = loc.FeeItemId == WarehouseRules.AdenaItemId;
            Notice(
                session,
                NpcServiceProtocol.NotEnoughAdena,
                adena ? NpcServiceProtocol.NotEnoughAdenaText : NpcServiceProtocol.NotEnoughFeeText);
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + (adena ? NpcServiceProtocol.NotEnoughAdenaText : NpcServiceProtocol.NotEnoughFeeText) + "</body></html>");
            return null;
        }

        int locationIdRef = locationId;
        RecordBagChange(
            player,
            LedgerTxKind.TeleportFee,
            Fees,
            () =>
            {
                var changes = new List<InventoryUpdateEntryMsg>();
                if (fee > 0)
                {
                    Take(player.Inventory, loc.FeeItemId, fee, changes);
                    if (changes.Count > 0)
                    {
                        FanInventory(session, Collapse(changes));
                        SaveInventory(session, player.Inventory);
                    }
                }

                Relocate(player, x, y, loc.Z);
                Notice(session, NpcServiceProtocol.Teleported, NpcServiceProtocol.TeleportedText);
                return true;
            },
            _ => locationIdRef);
        return null;
    }

}
