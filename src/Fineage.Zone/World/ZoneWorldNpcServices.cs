using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryTeleportSelectAsync(ZoneSession session, TeleportSelectRequestMsg request, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = TeleportTo(session, request.NpcObjectId, request.ListName, request.LocationId, html: false);
        return Task.FromResult<string?>(error);
    }

    public Task<string?> TryWarehouseDepositAsync(ZoneSession session, WarehouseDepositRequestMsg request, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = DepositPrivate(session, request.NpcObjectId, request.Lines, html: false);
        return Task.FromResult<string?>(error);
    }

    public Task<string?> TryWarehouseWithdrawAsync(ZoneSession session, WarehouseWithdrawRequestMsg request, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = WithdrawPrivate(session, request.NpcObjectId, request.Lines, html: false);
        return Task.FromResult<string?>(error);
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

    string? OpenDeposit(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (!IsWarehouse(npc))
        {
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return null;
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.EnchantOpenText + "</body></html>");
            return null;
        }

        SendDepositList(session, player, npc);
        SendHtml(session, player, npc, "<html><body>%npcname%:<br>Deposit an item. The fee is 30 adena per item.</body></html>");
        return null;
    }

    string? OpenWithdraw(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (!IsWarehouse(npc))
        {
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return null;
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.EnchantOpenText + "</body></html>");
            return null;
        }

        SendWithdrawList(session, player, npc);
        SendHtml(session, player, npc, "<html><body>%npcname%:<br>Withdraw an item from your private warehouse.</body></html>");
        return null;
    }

    string? DepositPrivate(ZoneSession session, int npcObjectId, WarehouseCountMsg[]? lines, bool html)
    {
        if (!TryServicePair(session, npcObjectId, out var player, out var npc, out var error))
            return error;
        if (player.Inventory == null || player.Warehouse == null)
            return "world";
        if (!IsWarehouse(npc))
        {
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return "bad_request";
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            return null;
        }

        if (!TryReadLines(lines, out var request, out error))
            return error;

        var planned = new List<(ItemInstance Item, int Count, bool Stackable)>();
        var seen = new HashSet<int>();
        int depositedAdena = 0;
        int slots = 0;
        foreach (var line in request)
        {
            if (!seen.Add(line.ObjectId))
                return "bad_request";
            var item = player.Inventory.Find(line.ObjectId);
            if (item == null || item.IsEquipped || !item.Template.CanDropTradeStore || item.Count < line.Count)
            {
                Notice(session, NpcServiceProtocol.CannotStore, NpcServiceProtocol.CannotStoreText);
                return null;
            }

            bool stackable = item.Template.Stackable;
            if (!stackable && line.Count != 1)
                return "bad_request";
            planned.Add((item, line.Count, stackable));
            if (item.ItemId == WarehouseRules.AdenaItemId)
                depositedAdena = checked(depositedAdena + line.Count);
            slots += WarehouseRules.SlotsFor(stackable, player.Warehouse.ContainsItem(item.ItemId), line.Count);
        }

        if (player.Warehouse.SlotCount + slots > WarehouseRules.WarehouseSlots(player.Race))
        {
            Notice(session, NpcServiceProtocol.WarehouseFull, NpcServiceProtocol.WarehouseFullText);
            return null;
        }

        int fee = WarehouseRules.DepositFee(planned.Count);
        int adena = player.Inventory.CountOf(WarehouseRules.AdenaItemId);
        if (!WarehouseRules.FeeCovered(adena, depositedAdena, fee))
        {
            Notice(session, NpcServiceProtocol.WarehouseAdena, NpcServiceProtocol.NotEnoughAdenaText);
            return null;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        if (fee > 0 && !Take(player.Inventory, WarehouseRules.AdenaItemId, fee, changes))
        {
            Notice(session, NpcServiceProtocol.WarehouseAdena, NpcServiceProtocol.NotEnoughAdenaText);
            return null;
        }

        foreach (var (item, count, _) in planned)
        {
            if (!player.Inventory.TryTakeFrom(item.ObjectId, count, out var taken, out bool removed) || taken == null)
                return "bad_request";
            changes.Add(removed
                ? InventoryMessages.RemoveRow(taken, -1)
                : InventoryMessages.ChangeRow(InventoryChange.Modify, taken));
            player.Warehouse.Add(taken.ItemId, count, taken.EnchantLevel, out _);
        }

        FanInventory(session, Collapse(changes));
        SaveInventory(session, player.Inventory);
        SaveWarehouse(session, player);
        Notice(session, NpcServiceProtocol.TransactionComplete, NpcServiceProtocol.TransactionCompleteText);
        return null;
    }

    string? WithdrawPrivate(ZoneSession session, int npcObjectId, WarehouseCountMsg[]? lines, bool html)
    {
        if (!TryServicePair(session, npcObjectId, out var player, out var npc, out var error))
            return error;
        if (player.Inventory == null || player.Warehouse == null)
            return "world";
        if (!IsWarehouse(npc))
        {
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return "bad_request";
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            return null;
        }

        if (!TryReadLines(lines, out var request, out error))
            return error;

        var planned = new List<(WarehouseStack Stack, int Count, bool Stackable)>();
        var seen = new HashSet<int>();
        int slots = 0;
        int incomingWeight = 0;
        foreach (var line in request)
        {
            if (!seen.Add(line.ObjectId))
                return "bad_request";
            var stack = player.Warehouse.Find(line.ObjectId);
            if (stack == null || stack.Count < line.Count)
                return "bad_request";
            var template = _game.Items.Get(stack.ItemId);
            if (!template.Stackable && line.Count != 1)
                return "bad_request";
            planned.Add((stack, line.Count, template.Stackable));
            slots += WarehouseRules.SlotsFor(template.Stackable, player.Inventory.FindByItemId(stack.ItemId) != null, line.Count);
            incomingWeight = checked(incomingWeight + _weights.Of(stack.ItemId) * line.Count);
        }

        if (player.Inventory.Items.Count + slots > WarehouseRules.InventorySlots(player.Race))
        {
            Notice(session, NpcServiceProtocol.InventoryFull, NpcServiceProtocol.InventoryFullText);
            return null;
        }

        int carried = _weights.Load(player.Inventory.Items.Select(item => (item.ItemId, item.Count)));
        if ((long)carried + incomingWeight > MaxLoadOf(player))
        {
            Notice(session, NpcServiceProtocol.WeightLimit, NpcServiceProtocol.WeightLimitText);
            return null;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        foreach (var (stack, count, stackable) in planned)
        {
            if (!player.Warehouse.TryTake(stack.ObjectId, count, out _, out _))
                return "bad_request";
            bool existed = stackable && player.Inventory.FindByItemId(stack.ItemId) != null;
            var added = player.Inventory.Add(stack.ItemId, count, stack.EnchantLevel);
            changes.Add(InventoryMessages.ChangeRow(existed ? InventoryChange.Modify : InventoryChange.Add, added));
        }

        FanInventory(session, Collapse(changes));
        SaveInventory(session, player.Inventory);
        SaveWarehouse(session, player);
        Notice(session, NpcServiceProtocol.TransactionComplete, NpcServiceProtocol.TransactionCompleteText);
        return null;
    }

    string? ApplySupportMagic(ZoneSession session, int npcObjectId, bool html)
    {
        if (!TryServicePair(session, npcObjectId, out var player, out var npc, out var error))
            return error;

        var refusal = _newbieBuffs.Evaluate(player.Newbie, player.ClassId, ClassLevelOf(player), player.Level, out var buffs);
        if (refusal != NewbieBuffRefusal.None)
        {
            var (id, text, code) = Refusal(refusal);
            Notice(session, id, text);
            if (html)
            {
                // Mobius SupportMagic shows default/SupportMagicHighLevel.htm / SupportMagicLowLevel.htm.
                string? page = refusal switch
                {
                    NewbieBuffRefusal.LevelHigh => "default/SupportMagicHighLevel.htm",
                    NewbieBuffRefusal.LevelLow => "default/SupportMagicLowLevel.htm",
                    _ => null
                };
                SendHtml(session, player, npc, page != null && _html.TryGet(page, out var mobius)
                    ? mobius
                    : "<html><body>%npcname%:<br>" + text + "</body></html>");
            }

            return null;
        }

        var ids = new int[buffs.Count];
        for (int i = 0; i < buffs.Count; i++)
        {
            var buff = buffs[i];
            ids[i] = buff.SkillId;
            CastBuff(npc, player, buff.SkillId, buff.SkillLevel, buff.AbnormalLevel, buff.DurationSeconds);
        }

        FanoutAbnormal(player);
        if (html)
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>Supplemental magic has been granted.</body></html>");
        return null;
    }

    string? ShowBlessingPage(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        string page = _html.TryGet("default/BlessingOfProtection.htm", out var html)
            ? html
            : "<html><body>%npcname%:<br><a action=\"bypass -h npc_%objectId%_GiveBlessing\">Receive the Blessing of Protection.</a></body></html>";
        SendHtml(session, player, npc, page);
        return null;
    }

    string? ApplyBlessing(ZoneSession session, int npcObjectId, bool html)
    {
        if (!TryServicePair(session, npcObjectId, out var player, out var npc, out var error))
            return error;

        var blessing = _newbieBuffs.Blessing;
        if (!_newbieBuffs.CanBless(player.Level, ClassLevelOf(player)))
        {
            Notice(session, NpcServiceProtocol.BlessingRefused, NpcServiceProtocol.BlessingRefusedText);
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.BlessingRefusedText + "</body></html>");
            return null;
        }

        CastBuff(npc, player, blessing.SkillId, blessing.SkillLevel, blessing.AbnormalLevel, blessing.DurationSeconds);
        FanoutAbnormal(player);
        if (html)
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>The Blessing of Protection is upon you.</body></html>");
        return null;
    }

    void SendDepositList(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var items = new List<ItemListEntryMsg>();
        if (player.Inventory != null)
        {
            foreach (var item in player.Inventory.Items)
            {
                if (!item.IsEquipped && item.Template.CanDropTradeStore)
                    items.Add(InventoryMessages.Entry(item));
            }
        }

        var list = new WarehouseDepositListMsg
        {
            NpcObjectId = npc.ObjectId,
            FeePerLine = WarehouseRules.DepositFeePerLine,
            SlotsUsed = player.Warehouse?.SlotCount ?? 0,
            SlotsMax = WarehouseRules.WarehouseSlots(player.Race),
            Adena = player.Inventory?.CountOf(WarehouseRules.AdenaItemId) ?? 0,
            Items = items.ToArray()
        };
        if (NetBridge != null)
            NetBridge.FanoutWarehouseDepositList([session.ObjectId], ToNetDepositList(list));
    }

    void SendWithdrawList(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var items = new List<ItemListEntryMsg>();
        if (player.Warehouse != null)
        {
            foreach (var stack in player.Warehouse.Items)
                items.Add(StackEntry(stack));
        }

        var list = new WarehouseWithdrawListMsg
        {
            NpcObjectId = npc.ObjectId,
            SlotsUsed = player.Warehouse?.SlotCount ?? 0,
            SlotsMax = WarehouseRules.WarehouseSlots(player.Race),
            InventorySlotsUsed = player.Inventory?.Items.Count ?? 0,
            InventorySlotsMax = WarehouseRules.InventorySlots(player.Race),
            CurrentWeight = player.Inventory == null ? 0 : _weights.Load(player.Inventory.Items.Select(item => (item.ItemId, item.Count))),
            MaxWeight = MaxLoadOf(player),
            Items = items.ToArray()
        };
        if (NetBridge != null)
            NetBridge.FanoutWarehouseWithdrawList([session.ObjectId], ToNetWithdrawList(list));
    }

    ItemListEntryMsg StackEntry(WarehouseStack stack)
    {
        var template = _game.Items.Get(stack.ItemId);
        return new ItemListEntryMsg
        {
            ObjectId = stack.ObjectId,
            ItemId = stack.ItemId,
            Count = stack.Count,
            EquippedSlot = -1,
            BodySlot = "none",
            Kind = template.Kind switch
            {
                ItemKind.Weapon => "weapon",
                ItemKind.Armor => "armor",
                ItemKind.Etc => "etc",
                _ => "etc"
            },
            Icon = template.Icon,
            WeaponType = "",
            ArmorType = "",
            EnchantLevel = stack.EnchantLevel
        };
    }

    void CastBuff(
        ZoneEntity npc,
        ZoneEntity player,
        int skillId,
        int level,
        int abnormalLevel,
        int durationSeconds)
    {
        if (player.Session == null)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        ZoneAbnormals.Apply(player.Abnormals, skillId, abnormalLevel, durationSeconds, now, level);
        var cast = new MagicSkillUseMsg
        {
            CasterObjectId = npc.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = skillId,
            SkillLevel = level,
            HitTimeMs = 0,
            ReuseDelayMs = 0
        };
                // Net clients only see the gesture through the bridge; the abnormal row alone leaves the pawn silent.
        NetBridge?.FanoutMagicSkillUse([player.ObjectId], new Fineage.Net.Contract.MagicSkillUseNetMsg
        {
            CasterObjectId = cast.CasterObjectId,
            TargetObjectId = cast.TargetObjectId,
            SkillId = cast.SkillId,
            SkillLevel = cast.SkillLevel,
            HitTimeMs = cast.HitTimeMs,
            ReuseDelayMs = cast.ReuseDelayMs
        });
        foreach (var other in Players())
        {
            if (other.Session == null || ReferenceEquals(other, player))
                continue;
            if (!other.Knownlist.Contains(player.ObjectId) && !other.Knownlist.Contains(npc.ObjectId))
                continue;
        }
    }

    bool TryServicePair(ZoneSession session, int npcObjectId, out ZoneEntity player, out ZoneEntity npc, out string? error)
    {
        player = null!;
        npc = null!;
        error = null;
        if (!IsLive(session) || !TryPlayer(session, out player!) || player.Inventory == null)
        {
            error = "world";
            return false;
        }

        if (player.Dead)
        {
            error = InventoryProtocol.Dead;
            return false;
        }

        if (!_byId.TryGetValue(npcObjectId, out npc!) || npc.Kind != ZoneEntityKind.Npc)
        {
            error = QuestProtocol.Npc;
            return false;
        }

        if (npc.Attackable)
        {
            error = NpcHtmlProtocol.Attackable;
            return false;
        }

        if (!InTalkRange(player, npc))
        {
            error = "range";
            return false;
        }

        return true;
    }

    static bool TryReadLines(WarehouseCountMsg[]? lines, out WarehouseCountMsg[] request, out string? error)
    {
        request = [];
        error = null;
        if (lines == null || lines.Length == 0 || lines.Length > WarehouseRules.MaxLines)
        {
            error = "bad_request";
            return false;
        }

        foreach (var line in lines)
        {
            if (line == null || line.ObjectId <= 0 || line.Count < 1)
            {
                error = "bad_request";
                return false;
            }
        }

        request = lines;
        return true;
    }

    bool SiegeBlocks(TeleportLocation loc)
    {
        foreach (int castleId in loc.CastleIds)
        {
            var castle = _siege.Find(castleId);
            if (castle is { InProgress: true })
                return true;
        }

        return false;
    }

    (int X, int Y) Offset(int x, int y)
    {
        if (_teleportOffset <= 0)
            return (x, y);
        int ox = x + _serviceRandom.Next(-_teleportOffset, _teleportOffset + 1);
        int oy = y + _serviceRandom.Next(-_teleportOffset, _teleportOffset + 1);
        return Loaded(ox, oy) ? (ox, oy) : (x, y);
    }

    bool Loaded(int x, int y)
    {
        foreach (var entity in _byId.Values)
        {
            if (entity.Kind != ZoneEntityKind.Npc)
                continue;
            if (NpcServiceProtocol.InLoadedZone(x, y, [(entity.X, entity.Y)]))
                return true;
        }

        return false;
    }

    int ClassLevelOf(ZoneEntity player)
    {
        if (PlayerClasses.TryGet(player.ClassId, out var template) && template != null)
            return template.ClassLevel;
        return 0;
    }

    void SaveWarehouse(ZoneSession session, ZoneEntity player)
    {
        if (_accounts == null || player.Warehouse == null)
            return;
        var stacks = player.Warehouse.Items;
        var stored = new StoredItem[stacks.Count];
        for (int i = 0; i < stacks.Count; i++)
        {
            var stack = stacks[i];
            stored[i] = new StoredItem(stack.ItemId, stack.Count, -1, stack.EnchantLevel, stack.ObjectId);
        }

        _accounts.TryReplaceWarehouse(session.Account, session.ObjectId, stored);
    }

    static int ReadTeleportOffset()
    {
        string? text = Environment.GetEnvironmentVariable("FINEAGE_TELEPORT_OFFSET");
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return NpcServiceProtocol.DefaultTeleportOffset;
        return Math.Clamp(value, 0, 500);
    }

    static bool IsWarehouse(ZoneEntity npc) =>
        string.Equals(npc.NpcType, "Warehouse", StringComparison.OrdinalIgnoreCase);

    void Notice(ZoneSession session, int id, string text)
    {
        var message = new SystemMessageMsg
        {
            Id = id,
            Params = [],
            Text = text
        };
        if (NetBridge != null)
        {
            NetBridge.FanoutSystemMessage([session.ObjectId], new Fineage.Net.Contract.SystemMessageNetMsg
            {
                Id = message.Id,
                Text = message.Text ?? "",
                Params = message.Params ?? []
            });
        }
    }

    void FanInventory(ZoneSession session, InventoryUpdateEntryMsg[] changes)
    {
        var invUpdate = new InventoryUpdateMsg { Changes = changes };
        if (NetBridge != null)
            NetBridge.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(invUpdate));
    }

    static Fineage.Net.Contract.WarehouseDepositListNetMsg ToNetDepositList(WarehouseDepositListMsg list) => new()
    {
        NpcObjectId = list.NpcObjectId,
        FeePerLine = list.FeePerLine,
        SlotsUsed = list.SlotsUsed,
        SlotsMax = list.SlotsMax,
        Adena = list.Adena,
        Items = ToNetItemRows(list.Items)
    };

    static Fineage.Net.Contract.WarehouseWithdrawListNetMsg ToNetWithdrawList(WarehouseWithdrawListMsg list) => new()
    {
        NpcObjectId = list.NpcObjectId,
        SlotsUsed = list.SlotsUsed,
        SlotsMax = list.SlotsMax,
        InventorySlotsUsed = list.InventorySlotsUsed,
        InventorySlotsMax = list.InventorySlotsMax,
        CurrentWeight = list.CurrentWeight,
        MaxWeight = list.MaxWeight,
        Items = ToNetItemRows(list.Items)
    };

    static Fineage.Net.Contract.ItemListEntryNet[] ToNetItemRows(ItemListEntryMsg[]? items)
    {
        items ??= [];
        var rows = new Fineage.Net.Contract.ItemListEntryNet[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            rows[i] = new Fineage.Net.Contract.ItemListEntryNet
            {
                ObjectId = items[i].ObjectId,
                ItemId = items[i].ItemId,
                Count = items[i].Count,
                EnchantLevel = items[i].EnchantLevel,
                EquippedSlot = items[i].EquippedSlot
            };
        }

        return rows;
    }

    static (int Id, string Text, string Code) Refusal(NewbieBuffRefusal refusal) => refusal switch
    {
        NewbieBuffRefusal.NotNewbie => (NpcServiceProtocol.BuffNotNewbie, NpcServiceProtocol.BuffNotNewbieText, "newbie"),
        NewbieBuffRefusal.LevelLow => (NpcServiceProtocol.BuffLevelLow, NpcServiceProtocol.BuffLevelLowText, "low"),
        NewbieBuffRefusal.LevelHigh => (NpcServiceProtocol.BuffLevelHigh, NpcServiceProtocol.BuffLevelHighText, "high"),
        NewbieBuffRefusal.ThirdClass => (NpcServiceProtocol.BuffThirdClass, NpcServiceProtocol.BuffThirdClassText, "class"),
        _ => (NpcServiceProtocol.BuffNone, NpcServiceProtocol.BuffNoneText, "none")
    };

    static string FeeName(int itemId) => itemId == WarehouseRules.AdenaItemId ? "Adena" : "items";

    static string HtmlText(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    static InventoryUpdateEntryMsg[] Collapse(List<InventoryUpdateEntryMsg> changes)
    {
        var order = new List<int>();
        var map = new Dictionary<int, InventoryUpdateEntryMsg>();
        foreach (var change in changes)
        {
            if (map.TryAdd(change.ObjectId, change))
                order.Add(change.ObjectId);
            else
                map[change.ObjectId] = change;
        }

        var rows = new InventoryUpdateEntryMsg[order.Count];
        for (int i = 0; i < order.Count; i++)
            rows[i] = map[order[i]];
        return rows;
    }
}
