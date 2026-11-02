using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Shops;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryBuyAsync(ZoneSession session, RequestBuyItemMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = Buy(session, request, toSelf);
        if (error != null)
            return error;
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TrySellAsync(ZoneSession session, RequestSellItemMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = Sell(session, request, toSelf);
        if (error != null)
            return error;
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryMultiSellAsync(ZoneSession session, RequestMultiSellChooseMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = ChooseMultiSell(session, request, toSelf);
        if (error != null)
            return error;
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryPreviewAsync(ZoneSession session, RequestPreviewItemMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = Preview(session, request, toSelf);
        if (error != null)
            return error;
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    string? OpenBuyList(ZoneSession session, ZoneEntity player, ZoneEntity npc, int listId, List<Pending> toSelf)
    {
        if (!_buyLists.TryGet(listId, out var list) || !list.Allows(npc.NpcId))
        {
            SendHtml(session, player, npc, ShopProtocol.NoListHtml, toSelf);
            return null;
        }

        player.Shop = new ShopSession { NpcObjectId = npc.ObjectId, BuyListId = listId, SellOpen = player.Shop?.SellOpen ?? false };
        Tax(npc, out int baseTax, out int castleTax, out _);
        var buy = BuyList(npc, list, baseTax, castleTax);
        var balance = Balance(player, npc);
        toSelf.Add(Pack(session, MsgType.BuyList, buy));
        toSelf.Add(Pack(session, MsgType.ShopBalance, balance));
        FanNetBuy(session, buy, balance);
        return null;
    }

    string? OpenSellList(ZoneSession session, ZoneEntity player, ZoneEntity npc, List<Pending> toSelf)
    {
        if (!CanSell(npc))
        {
            SendHtml(session, player, npc, ShopProtocol.NotMerchantHtml, toSelf);
            return null;
        }

        var shop = player.Shop ?? new ShopSession { NpcObjectId = npc.ObjectId };
        if (shop.NpcObjectId != npc.ObjectId)
            shop = new ShopSession { NpcObjectId = npc.ObjectId };
        shop.SellOpen = true;
        player.Shop = shop;
        var sell = SellList(npc, player);
        var balance = Balance(player, npc);
        toSelf.Add(Pack(session, MsgType.SellList, sell));
        toSelf.Add(Pack(session, MsgType.ShopBalance, balance));
        FanNetSell(session, sell, balance);
        return null;
    }

    string? OpenMultiSell(ZoneSession session, ZoneEntity player, ZoneEntity npc, int listId, bool inventoryOnly, List<Pending> toSelf)
    {
        if (!_multisells.TryGet(listId, out var list) || !list.Allows(npc.NpcId))
        {
            SendHtml(session, player, npc, ShopProtocol.NoListHtml, toSelf);
            return null;
        }

        Tax(npc, out _, out int castleTax, out bool owned);
        bool applyTax = list.ApplyTaxes && npc.MerchantCastleId > 0 && owned;
        var prepared = ShopTrade.Prepare(list, player.Inventory!, _game.Items, inventoryOnly, applyTax, castleTax);
        player.Shop = new ShopSession { NpcObjectId = npc.ObjectId, Multi = prepared, SellOpen = player.Shop?.SellOpen ?? false, BuyListId = player.Shop?.BuyListId ?? 0 };
        var multi = MultiList(npc, prepared, castleTax);
        var balance = Balance(player, npc);
        toSelf.Add(Pack(session, MsgType.MultiSellList, multi));
        toSelf.Add(Pack(session, MsgType.ShopBalance, balance));
        FanNetMulti(session, multi, balance);
        return null;
    }

    string? Buy(ZoneSession session, RequestBuyItemMsg request, List<Pending> toSelf)
    {
        if (request.Items == null)
            return "bad_request";
        if (!TryShopNpc(session, request.NpcObjectId, out var player, out var npc, out var error))
            return error;
        if (player.Shop == null || player.Shop.NpcObjectId != npc.ObjectId || player.Shop.BuyListId != request.ListId)
            return "bad_request";
        if (!_buyLists.TryGet(request.ListId, out var list) || !list.Allows(npc.NpcId) || player.Inventory == null)
            return "bad_request";

        var lines = new ShopLine[request.Items.Length];
        for (int i = 0; i < lines.Length; i++)
            lines[i] = new ShopLine(request.Items[i].ItemId, request.Items[i].Count);

        Tax(npc, out int baseTax, out int castleTax, out _);
        var mutation = ShopTrade.Buy(
            player.Inventory,
            _game.Items,
            list,
            lines,
            baseTax,
            castleTax,
            ShopTrade.MaxLoad(PlayerClasses, player.ClassId),
            ShopTrade.SlotLimit(player.Race),
            _shopStock,
            _clock().ToUnixTimeMilliseconds());
        if (!mutation.Ok)
        {
            SendDeny(session, mutation, toSelf);
            return null;
        }

        SendTouches(session, mutation, toSelf);
        SendComplete(session, toSelf);
        var buy = BuyList(npc, list, baseTax, castleTax);
        var balance = Balance(player, npc);
        toSelf.Add(Pack(session, MsgType.BuyList, buy));
        toSelf.Add(Pack(session, MsgType.ShopBalance, balance));
        FanNetBuy(session, buy, balance);
        SaveInventory(session, player.Inventory);
        return null;
    }

    string? Sell(ZoneSession session, RequestSellItemMsg request, List<Pending> toSelf)
    {
        if (request.Items == null)
            return "bad_request";
        if (!TryShopNpc(session, request.NpcObjectId, out var player, out var npc, out var error))
            return error;
        if (player.Shop is not { SellOpen: true } || player.Shop.NpcObjectId != npc.ObjectId || player.Inventory == null)
            return "bad_request";
        if (!CanSell(npc))
            return "bad_request";

        var lines = new ShopObjectLine[request.Items.Length];
        for (int i = 0; i < lines.Length; i++)
            lines[i] = new ShopObjectLine(request.Items[i].ObjectId, request.Items[i].Count);

        var mutation = ShopTrade.Sell(player.Inventory, lines);
        if (!mutation.Ok)
        {
            SendDeny(session, mutation, toSelf);
            return null;
        }

        SendTouches(session, mutation, toSelf);
        SendComplete(session, toSelf);
        var sell = SellList(npc, player);
        var balance = Balance(player, npc);
        toSelf.Add(Pack(session, MsgType.SellList, sell));
        toSelf.Add(Pack(session, MsgType.ShopBalance, balance));
        FanNetSell(session, sell, balance);
        SaveInventory(session, player.Inventory);
        return null;
    }

    string? ChooseMultiSell(ZoneSession session, RequestMultiSellChooseMsg request, List<Pending> toSelf)
    {
        if (!TryShopNpc(session, request.NpcObjectId, out var player, out var npc, out var error))
            return error;
        if (player.Shop?.Multi == null || player.Shop.NpcObjectId != npc.ObjectId || player.Shop.Multi.ListId != request.ListId)
            return "bad_request";
        if (player.Inventory == null)
            return "bad_request";
        var entry = player.Shop.Multi.Find(request.EntryId);
        if (entry == null)
            return "bad_request";

        var mutation = ShopTrade.Exchange(
            player.Inventory,
            _game.Items,
            entry,
            request.Amount,
            ShopTrade.MaxLoad(PlayerClasses, player.ClassId),
            ShopTrade.SlotLimit(player.Race));
        if (!mutation.Ok)
        {
            SendDeny(session, mutation, toSelf);
            return null;
        }

        SendTouches(session, mutation, toSelf);
        SendComplete(session, toSelf);
        MultiSellListMsg? multi = null;
        if (player.Shop.Multi.InventoryOnly && _multisells.TryGet(request.ListId, out var list))
        {
            Tax(npc, out _, out int castleTax, out bool owned);
            bool applyTax = list.ApplyTaxes && npc.MerchantCastleId > 0 && owned;
            var prepared = ShopTrade.Prepare(list, player.Inventory, _game.Items, true, applyTax, castleTax);
            player.Shop.Multi = prepared;
            multi = MultiList(npc, prepared, castleTax);
            toSelf.Add(Pack(session, MsgType.MultiSellList, multi));
        }

        var balance = Balance(player, npc);
        toSelf.Add(Pack(session, MsgType.ShopBalance, balance));
        if (multi != null)
            FanNetMulti(session, multi, balance);
        else if (NetBridge != null)
            NetBridge.FanoutShopBalance([session.ObjectId], ToNetBalance(balance));
        SaveInventory(session, player.Inventory);
        return null;
    }

    string? Preview(ZoneSession session, RequestPreviewItemMsg request, List<Pending> toSelf)
    {
        if (!TryShopNpc(session, request.NpcObjectId, out _, out _, out var error))
            return error;
        toSelf.Add(Pack(session, MsgType.SystemMessage, Line(ShopProtocol.WearUnavailable, ShopProtocol.WearUnavailableText)));
        return null;
    }

    bool TryShopNpc(ZoneSession session, int npcObjectId, out ZoneEntity player, out ZoneEntity npc, out string? error)
    {
        player = null!;
        npc = null!;
        error = null;
        if (!TryPlayer(session, out player!) || player.Inventory == null)
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

    bool CanSell(ZoneEntity npc) =>
        npc.NpcType.Equals("Merchant", StringComparison.OrdinalIgnoreCase) ||
        npc.NpcType.Equals("Fisherman", StringComparison.OrdinalIgnoreCase) ||
        _buyLists.NpcListed(npc.NpcId);

    void Tax(ZoneEntity npc, out int baseTax, out int castleTax, out bool owned)
    {
        baseTax = Math.Clamp(npc.MerchantBaseTaxPercent, 0, 100);
        castleTax = 0;
        owned = false;
        if (npc.MerchantCastleId <= 0)
            return;
        var castle = _siege.Find(npc.MerchantCastleId);
        if (castle == null || castle.OwnerClanId <= 0)
            return;
        owned = true;
        castleTax = Math.Clamp(castle.TaxPercent, 0, 100);
    }

    BuyListMsg BuyList(ZoneEntity npc, BuyListDefinition list, int baseTax, int castleTax)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        var items = new BuyListItemMsg[list.Products.Count];
        for (int i = 0; i < items.Length; i++)
        {
            var product = list.Products[i];
            int price = product.Price;
            if (product.ItemId >= ShopTrade.SiegeGuardFirst && product.ItemId <= ShopTrade.SiegeGuardLast)
                price = (int)(price * ShopTrade.SiegeGuardPriceRate);
            items[i] = new BuyListItemMsg
            {
                ItemId = product.ItemId,
                Price = Math.Max(0, ShopTrade.TaxedUnitPrice(price, baseTax, castleTax)),
                Count = product.Limited ? _shopStock.Available(list.Id, product.ItemId, now) : -1
            };
        }

        return new BuyListMsg
        {
            NpcObjectId = npc.ObjectId,
            ListId = list.Id,
            BaseTaxPercent = baseTax,
            CastleTaxPercent = castleTax,
            Items = items
        };
    }

    static SellListMsg SellList(ZoneEntity npc, ZoneEntity player)
    {
        var rows = new List<SellListItemMsg>();
        foreach (var item in player.Inventory!.Items)
        {
            if (item.IsEquipped || !item.Template.CanSell)
                continue;
            rows.Add(new SellListItemMsg
            {
                ObjectId = item.ObjectId,
                ItemId = item.ItemId,
                Count = item.Count,
                Price = item.Template.ReferencePrice / 2,
                EnchantLevel = item.EnchantLevel
            });
        }

        return new SellListMsg { NpcObjectId = npc.ObjectId, Items = rows.ToArray() };
    }

    static MultiSellListMsg MultiList(ZoneEntity npc, PreparedMultiSell prepared, int castleTax)
    {
        var entries = new MultiSellEntryMsg[prepared.Entries.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = prepared.Entries[i];
            entries[i] = new MultiSellEntryMsg
            {
                EntryId = entry.EntryId,
                Ingredients = Parts(entry.Ingredients),
                Products = Parts(entry.Products)
            };
        }

        return new MultiSellListMsg
        {
            NpcObjectId = npc.ObjectId,
            ListId = prepared.ListId,
            MaintainEnchantment = prepared.MaintainEnchantment,
            InventoryOnly = prepared.InventoryOnly,
            CastleTaxPercent = castleTax,
            Entries = entries
        };
    }

    static MultiSellPartMsg[] Parts(IReadOnlyList<PreparedShopPart> parts)
    {
        var rows = new MultiSellPartMsg[parts.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new MultiSellPartMsg
            {
                ItemId = parts[i].ItemId,
                Count = parts[i].Count,
                EnchantLevel = Math.Max(0, parts[i].Enchant)
            };
        }

        return rows;
    }

    ShopBalanceMsg Balance(ZoneEntity player, ZoneEntity npc)
    {
        Tax(npc, out int baseTax, out int castleTax, out _);
        long weight = player.Inventory!.TotalWeight;
        return new ShopBalanceMsg
        {
            Adena = player.Inventory.CountOf(ShopTrade.AdenaItemId),
            Weight = weight > int.MaxValue ? int.MaxValue : (int)weight,
            MaxWeight = ShopTrade.MaxLoad(PlayerClasses, player.ClassId),
            Slots = player.Inventory.Items.Count,
            MaxSlots = ShopTrade.SlotLimit(player.Race),
            BaseTaxPercent = baseTax,
            CastleTaxPercent = castleTax
        };
    }

    void SendTouches(ZoneSession session, ShopMutation mutation, List<Pending> toSelf)
    {
        if (mutation.Touches.Count == 0)
            return;
        var changes = new InventoryUpdateEntryMsg[mutation.Touches.Count];
        for (int i = 0; i < changes.Length; i++)
        {
            var touch = mutation.Touches[i];
            int kind = touch.Kind switch
            {
                ShopTouchKind.Add => InventoryChange.Add,
                ShopTouchKind.Remove => InventoryChange.Remove,
                _ => InventoryChange.Modify
            };
            changes[i] = InventoryMessages.ChangeRow(kind, touch.Item);
        }

        var invUpdate = new InventoryUpdateMsg { Changes = changes };
        toSelf.Add(Pack(session, MsgType.InventoryUpdate, invUpdate));
        if (NetBridge != null)
            NetBridge.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(invUpdate));
    }

    void SendComplete(ZoneSession session, List<Pending> toSelf)
    {
        var message = Line(ShopProtocol.TransactionComplete, ShopProtocol.TransactionCompleteText);
        toSelf.Add(Pack(session, MsgType.SystemMessage, message));
        FanNetSystem(session, message);
    }

    void SendDeny(ZoneSession session, ShopMutation mutation, List<Pending> toSelf)
    {
        SystemMessageMsg message = mutation.Deny switch
        {
            ShopDeny.Adena => Line(ShopProtocol.NotEnoughAdena, ShopProtocol.NotEnoughAdenaText),
            ShopDeny.Weight => Line(ShopProtocol.WeightLimit, ShopProtocol.WeightLimitText),
            ShopDeny.Slots => Line(ShopProtocol.InventoryFull, ShopProtocol.InventoryFullText),
            ShopDeny.Quantity => Line(ShopProtocol.Quantity, ShopProtocol.QuantityText),
            ShopDeny.Pocket => Line(ShopProtocol.PocketLimit, ShopProtocol.PocketLimitText),
            ShopDeny.Stock => Line(ShopProtocol.OutOfStock, ShopProtocol.OutOfStockText),
            ShopDeny.Ingredients => new SystemMessageMsg
            {
                Id = ShopProtocol.Ingredients,
                Params = [mutation.MissingCount.ToString(), mutation.MissingName],
                Text = $"{mutation.MissingCount} unit(s) of the item {mutation.MissingName} is/are required."
            },
            _ => Line(ShopProtocol.NotTradable, ShopProtocol.NotTradableText)
        };
        toSelf.Add(Pack(session, MsgType.SystemMessage, message));
        FanNetSystem(session, message);
    }

    static SystemMessageMsg Line(int id, string text) => new() { Id = id, Params = [], Text = text };

    void FanNetBuy(ZoneSession session, BuyListMsg buy, ShopBalanceMsg balance)
    {
        if (NetBridge == null)
            return;
        int[] self = [session.ObjectId];
        NetBridge.FanoutBuyList(self, ToNetBuy(buy));
        NetBridge.FanoutShopBalance(self, ToNetBalance(balance));
    }

    void FanNetSell(ZoneSession session, SellListMsg sell, ShopBalanceMsg balance)
    {
        if (NetBridge == null)
            return;
        int[] self = [session.ObjectId];
        NetBridge.FanoutSellList(self, ToNetSell(sell));
        NetBridge.FanoutShopBalance(self, ToNetBalance(balance));
    }

    void FanNetMulti(ZoneSession session, MultiSellListMsg multi, ShopBalanceMsg balance)
    {
        if (NetBridge == null)
            return;
        int[] self = [session.ObjectId];
        NetBridge.FanoutMultiSellList(self, ToNetMulti(multi));
        NetBridge.FanoutShopBalance(self, ToNetBalance(balance));
    }

    void FanNetSystem(ZoneSession session, SystemMessageMsg message)
    {
        if (NetBridge == null)
            return;
        NetBridge.FanoutSystemMessage([session.ObjectId], new Fineage.Net.Contract.SystemMessageNetMsg
        {
            Id = message.Id,
            Text = message.Text ?? "",
            Params = message.Params ?? []
        });
    }

    static Fineage.Net.Contract.BuyListNetMsg ToNetBuy(BuyListMsg buy)
    {
        var src = buy.Items ?? [];
        var items = new Fineage.Net.Contract.BuyListItemNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new Fineage.Net.Contract.BuyListItemNet
            {
                ItemId = src[i].ItemId,
                Price = src[i].Price,
                Count = src[i].Count
            };
        }

        return new Fineage.Net.Contract.BuyListNetMsg
        {
            NpcObjectId = buy.NpcObjectId,
            ListId = buy.ListId,
            BaseTaxPercent = buy.BaseTaxPercent,
            CastleTaxPercent = buy.CastleTaxPercent,
            Items = items
        };
    }

    static Fineage.Net.Contract.SellListNetMsg ToNetSell(SellListMsg sell)
    {
        var src = sell.Items ?? [];
        var items = new Fineage.Net.Contract.SellListItemNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new Fineage.Net.Contract.SellListItemNet
            {
                ObjectId = src[i].ObjectId,
                ItemId = src[i].ItemId,
                Count = src[i].Count,
                Price = src[i].Price,
                EnchantLevel = src[i].EnchantLevel
            };
        }

        return new Fineage.Net.Contract.SellListNetMsg { NpcObjectId = sell.NpcObjectId, Items = items };
    }

    static Fineage.Net.Contract.MultiSellListNetMsg ToNetMulti(MultiSellListMsg multi)
    {
        var src = multi.Entries ?? [];
        var entries = new Fineage.Net.Contract.MultiSellEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            entries[i] = new Fineage.Net.Contract.MultiSellEntryNet
            {
                EntryId = src[i].EntryId,
                Ingredients = ToNetParts(src[i].Ingredients),
                Products = ToNetParts(src[i].Products)
            };
        }

        return new Fineage.Net.Contract.MultiSellListNetMsg
        {
            NpcObjectId = multi.NpcObjectId,
            ListId = multi.ListId,
            MaintainEnchantment = multi.MaintainEnchantment,
            InventoryOnly = multi.InventoryOnly,
            CastleTaxPercent = multi.CastleTaxPercent,
            Entries = entries
        };
    }

    static Fineage.Net.Contract.MultiSellPartNet[] ToNetParts(MultiSellPartMsg[]? parts)
    {
        parts ??= [];
        var rows = new Fineage.Net.Contract.MultiSellPartNet[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            rows[i] = new Fineage.Net.Contract.MultiSellPartNet
            {
                ItemId = parts[i].ItemId,
                Count = parts[i].Count,
                EnchantLevel = parts[i].EnchantLevel
            };
        }

        return rows;
    }

    static Fineage.Net.Contract.ShopBalanceNetMsg ToNetBalance(ShopBalanceMsg balance) => new()
    {
        Adena = balance.Adena,
        Weight = balance.Weight,
        MaxWeight = balance.MaxWeight,
        Slots = balance.Slots,
        MaxSlots = balance.MaxSlots,
        BaseTaxPercent = balance.BaseTaxPercent,
        CastleTaxPercent = balance.CastleTaxPercent
    };
}
