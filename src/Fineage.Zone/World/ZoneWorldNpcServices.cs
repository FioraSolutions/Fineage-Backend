using Fineage.Game;
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
        string? error;
        lock (_gate)
            error = TeleportTo(session, npcObjectId, listName ?? "", locationId, html: false);
        return Task.FromResult<string?>(error);
    }

    public Task<string?> TryWarehouseDepositAsync(
        ZoneSession session, int npcObjectId, WarehouseLine[] lines, CancellationToken ct)
    {
        // World mutate under _gate; Postgres inventory write after unlock (B8).
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
        string? error;
        lock (_gate)
            error = ApplySupportMagic(session, npcObjectId, html: false);
        return Task.FromResult<string?>(error);
    }

    public Task<string?> TryGiveBlessingAsync(ZoneSession session, int npcObjectId, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = ApplyBlessing(session, npcObjectId, html: false);
        return Task.FromResult<string?>(error);
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
            case NpcServiceIntent.SupportMagic:
                return ApplySupportMagic(session, npc.ObjectId, html: true);
            case NpcServiceIntent.SupportBlessing:
                return ShowBlessingPage(session, player, npc);
            case NpcServiceIntent.GiveBlessing:
                return ApplyBlessing(session, npc.ObjectId, html: true);
            case NpcServiceIntent.TerritoryStatus:
                return ShowTerritoryStatus(session, player, npc);
            case NpcServiceIntent.SkillList:
                return ShowTrainerSkills(session, player, npc);
            case NpcServiceIntent.LearnSkill:
                return LearnTrainerSkill(session, player, npc, reply.ServiceId);
            default:
                return "bad_request";
        }
    }

    /// <summary>Test: run one bypass command against the first spawned NPC with this template id.</summary>
    internal string? NpcServiceForTest(ZoneSession session, int npcId, string intent, string mapId = "elven_village")
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var player))
                return "world";
            var npc = _byId.Values.FirstOrDefault(e => e.Kind == ZoneEntityKind.Npc && e.NpcId == npcId)
                ?? new ZoneEntity
                {
                    Kind = ZoneEntityKind.Npc, NpcId = npcId, ObjectId = 0,
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

        var castle = _siege.Find(npc.MerchantCastleId);
        bool owned = castle != null && castle.OwnerClanId > 0;
        string file = owned ? "territorystatus.htm" : "territorynoclan.htm";
        if (!_html.TryGet(file, out var html))
            html = "<html><body>%castlename%</body></html>";
        string clanName = "", leaderName = "";
        if (owned && _clans.TryGetValue(castle!.OwnerClanId, out var clan))
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

    string? ShowTeleportList(ZoneSession session, ZoneEntity player, ZoneEntity npc, string listName)
    {
        var list = _teleports.Find(npc.NpcId, string.IsNullOrWhiteSpace(listName) ? TeleportRules.Normal : listName);
        if (list == null)
        {
            Notice(session, NpcServiceProtocol.UnknownTeleport, NpcServiceProtocol.UnknownTeleportText);
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>That teleport is not available.</body></html>");
            return null;
        }

        if (TeleportRules.IsNobles(list.Type))
        {
            Notice(session, NpcServiceProtocol.NoblesDeferred, NpcServiceProtocol.NoblesDeferredText);
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>Noblesse teleport is not available yet.</body></html>");
            return null;
        }

        var when = _clock();
        var rows = new TeleportLocationMsg[list.Locations.Count];
        var html = new System.Text.StringBuilder();
        html.Append("<html><body>%npcname%:<br>");
        for (int i = 0; i < list.Locations.Count; i++)
        {
            var loc = list.Locations[i];
            int fee = TeleportRules.Quote(list.Type, player.Level, loc.FeeCount, when);
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

        var list = _teleports.Find(npc.NpcId, string.IsNullOrWhiteSpace(listName) ? TeleportRules.Normal : listName);
        if (list == null || TeleportRules.IsNobles(list.Type))
        {
            int id = list != null ? NpcServiceProtocol.NoblesDeferred : NpcServiceProtocol.UnknownTeleport;
            string text = list != null ? NpcServiceProtocol.NoblesDeferredText : NpcServiceProtocol.UnknownTeleportText;
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

        var (x, y) = Offset(loc.X, loc.Y);
        if (!Loaded(x, y))
        {
            Notice(session, NpcServiceProtocol.DestinationUnloaded, NpcServiceProtocol.DestinationUnloadedText);
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>That destination is not on a loaded map.</body></html>");
            return null;
        }

        int fee = TeleportRules.Quote(list.Type, player.Level, loc.FeeCount, _clock());
        var changes = new List<InventoryUpdateEntryMsg>();
        if (fee > 0 && !Take(player.Inventory, loc.FeeItemId, fee, changes))
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

        if (changes.Count > 0)
        {
            // The fare is a bag change like any other, and Net clients only see it through the bridge.
            FanInventory(session, Collapse(changes));
            SaveInventory(session, player.Inventory);
        }

        Relocate(player, x, y, loc.Z);
        Notice(session, NpcServiceProtocol.Teleported, NpcServiceProtocol.TeleportedText);
        return null;
    }

}
