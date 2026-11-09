using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Shops;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryBuyAsync(
        ZoneSession session, int npcObjectId, int listId, ShopLine[] items, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        string? error;
        lock (_gate)
            error = Buy(session, npcObjectId, listId, items);
        return Task.FromResult<string?>(error);
    }

    public Task<string?> TrySellAsync(
        ZoneSession session, int npcObjectId, ShopObjectLine[] items, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        string? error;
        lock (_gate)
            error = Sell(session, npcObjectId, items);
        return Task.FromResult<string?>(error);
    }

    public Task<string?> TryMultiSellAsync(
        ZoneSession session, int npcObjectId, int listId, int entryId, int amount, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        string? error;
        lock (_gate)
            error = ChooseMultiSell(session, npcObjectId, listId, entryId, amount);
        return Task.FromResult<string?>(error);
    }

    public Task<string?> TryPreviewAsync(
        ZoneSession session, int npcObjectId, int listId, int itemId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        string? error;
        lock (_gate)
            error = Preview(session, npcObjectId);
        return Task.FromResult<string?>(error);
    }

    string? OpenBuyList(ZoneSession session, ZoneEntity player, ZoneEntity npc, int listId)
    {
        if (!_buyLists.TryGet(listId, out var list) || !list.Allows(npc.NpcId))
        {
            SendHtml(session, player, npc, ShopProtocol.NoListHtml);
            return null;
        }

        player.Shop = new ShopSession { NpcObjectId = npc.ObjectId, BuyListId = listId, SellOpen = player.Shop?.SellOpen ?? false };
        Tax(npc, out int baseTax, out int castleTax, out _);
        var buy = BuyList(npc, list, baseTax, castleTax);
        var balance = Balance(player, npc);
        FanNetBuy(session, buy, balance);
        return null;
    }

    string? OpenSellList(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (!CanSell(npc))
        {
            SendHtml(session, player, npc, ShopProtocol.NotMerchantHtml);
            return null;
        }

        var shop = player.Shop ?? new ShopSession { NpcObjectId = npc.ObjectId };
        if (shop.NpcObjectId != npc.ObjectId)
            shop = new ShopSession { NpcObjectId = npc.ObjectId };
        shop.SellOpen = true;
        player.Shop = shop;
        var sell = SellList(npc, player);
        var balance = Balance(player, npc);
        FanNetSell(session, sell, balance);
        return null;
    }

    string? OpenMultiSell(ZoneSession session, ZoneEntity player, ZoneEntity npc, int listId, bool inventoryOnly)
    {
        if (!_multisells.TryGet(listId, out var list) || !list.Allows(npc.NpcId))
        {
            SendHtml(session, player, npc, ShopProtocol.NoListHtml);
            return null;
        }

        Tax(npc, out _, out int castleTax, out bool owned);
        bool applyTax = list.ApplyTaxes && npc.MerchantCastleId > 0 && owned;
        var prepared = ShopTrade.Prepare(list, player.Inventory!, _game.Items, inventoryOnly, applyTax, castleTax);
        player.Shop = new ShopSession { NpcObjectId = npc.ObjectId, Multi = prepared, SellOpen = player.Shop?.SellOpen ?? false, BuyListId = player.Shop?.BuyListId ?? 0 };
        var multi = MultiList(npc, prepared, castleTax);
        var balance = Balance(player, npc);
        FanNetMulti(session, multi, balance);
        return null;
    }

    string? Buy(ZoneSession session, int npcObjectId, int listId, ShopLine[] items)
    {
        if (!TryPlayer(session, out var player) || player.Inventory == null)
            return "world";

        // Mobius admin_buy: NpcObjectId 0 + ShopSession.IsAdmin. Price-0 GM lists are free grants
        // (ShopTrade.Buy refuses unit price 0 as NotTradable).
        if (player.Shop is { IsAdmin: true } adminShop
            && npcObjectId == 0
            && adminShop.NpcObjectId == 0
            && adminShop.BuyListId == listId)
        {
            if (items == null)
                return "bad_request";
            if (player.Dead)
                return InventoryProtocol.Dead;
            if (!ZoneAdmin.IsAdminCharacter(player.Name))
                return "forbidden";
            if (!_buyLists.TryGet(listId, out var adminList))
                return "bad_request";
            return CompleteAdminBuy(session, player, adminList, items);
        }

        if (!TryShopNpc(session, npcObjectId, out player, out var npc, out var error))
            return error;
        bool haveList = _buyLists.TryGet(listId, out var list);
        bool listOk = haveList && list!.Allows(npc.NpcId) && player.Inventory != null;
        bool shopMatches = player.Shop != null && player.Shop.NpcObjectId == npc.ObjectId && player.Shop.BuyListId == listId;
        if (ShopSessionRules.BuyWindowError(items != null, player.Dead, shopMatches, listOk) is { } buyWindow)
            return buyWindow;
        var liveList = list!;
        var liveItems = items!;

        Tax(npc, out int baseTax, out int castleTax, out _);
        var mutation = ShopTrade.Buy(
            player.Inventory!,
            _game.Items,
            liveList,
            liveItems,
            baseTax,
            castleTax,
            MaxLoadOf(player),
            ShopTrade.SlotLimit(player.Race),
            _shopStock,
            _clock().ToUnixTimeMilliseconds());
        if (!mutation.Ok)
        {
            SendDeny(session, mutation);
            return null;
        }

        SendTouches(session, mutation);
        SendComplete(session);
        var buy = BuyList(npc, liveList, baseTax, castleTax);
        var balance = Balance(player, npc);
        FanNetBuy(session, buy, balance);
        SaveInventory(session, player.Inventory!);
        return null;
    }

    string? Sell(ZoneSession session, int npcObjectId, ShopObjectLine[] items)
    {
        if (!TryShopNpc(session, npcObjectId, out var player, out var npc, out var error))
            return error;
        bool sellOpen = player.Shop is { SellOpen: true } && player.Shop.NpcObjectId == npc.ObjectId && player.Inventory != null;
        if (ShopSessionRules.SellWindowError(items != null, sellOpen, CanSell(npc)) is { } sellWindow)
            return sellWindow;
        var liveItems = items!;

        var mutation = ShopTrade.Sell(player.Inventory!, liveItems);
        if (!mutation.Ok)
        {
            SendDeny(session, mutation);
            return null;
        }

        SendTouches(session, mutation);
        SendComplete(session);
        var sell = SellList(npc, player);
        var balance = Balance(player, npc);
        FanNetSell(session, sell, balance);
        SaveInventory(session, player.Inventory!);
        return null;
    }

    string? ChooseMultiSell(ZoneSession session, int npcObjectId, int listId, int entryId, int amount)
    {
        if (!TryShopNpc(session, npcObjectId, out var player, out var npc, out var error))
            return error;
        bool multiMatches = player.Shop?.Multi != null
            && player.Shop.NpcObjectId == npc.ObjectId
            && player.Shop.Multi.ListId == listId;
        var entry = multiMatches ? player.Shop!.Multi!.Find(entryId) : null;
        if (ShopSessionRules.MultiWindowError(multiMatches, player.Inventory != null, entry != null) is { } multiWindow)
            return multiWindow;
        var liveEntry = entry!;
        var liveInventory = player.Inventory!;
        var liveShop = player.Shop!;

        var mutation = ShopTrade.Exchange(
            liveInventory,
            _game.Items,
            liveEntry,
            amount,
            MaxLoadOf(player),
            ShopTrade.SlotLimit(player.Race));
        if (!mutation.Ok)
        {
            SendDeny(session, mutation);
            return null;
        }

        SendTouches(session, mutation);
        SendComplete(session);
        MultiSellListMsg? multi = null;
        if (liveShop.Multi!.InventoryOnly && _multisells.TryGet(listId, out var list))
        {
            Tax(npc, out _, out int castleTax, out bool owned);
            bool applyTax = list.ApplyTaxes && npc.MerchantCastleId > 0 && owned;
            var prepared = ShopTrade.Prepare(list, liveInventory, _game.Items, true, applyTax, castleTax);
            liveShop.Multi = prepared;
            multi = MultiList(npc, prepared, castleTax);
        }

        var balance = Balance(player, npc);
        if (multi != null)
            FanNetMulti(session, multi, balance);
        else if (NetBridge != null)
            NetBridge.FanoutShopBalance([session.ObjectId], balance);
        SaveInventory(session, liveInventory);
        return null;
    }

}
