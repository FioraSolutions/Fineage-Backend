using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Trade;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? TryPrivateStoreSellTo(ZoneSession session, PrivateStoreSellToMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var seller) || seller.Inventory == null)
            return "world";
        if (seller.Dead)
            return InventoryProtocol.Dead;
        if (!_byId.TryGetValue(request.StoreObjectId, out var store) || store.Kind != ZoneEntityKind.Player || store.Inventory == null)
            return PrivateStoreProtocol.Closed;
        if (store.ObjectId == seller.ObjectId)
            return "bad_request";
        if (store.PrivateStoreType != PrivateStoreProtocol.TypeBuy || store.PrivateBuyList.Count == 0)
            return PrivateStoreProtocol.Closed;
        if (!StoreInRange(seller, store))
            return PrivateStoreProtocol.Range;

        var offers = request.Items ?? [];
        if (offers.Length == 0)
            return PrivateStoreProtocol.Item;

        long payout = 0;
        var transfers = new List<(PrivateStoreBuyListing Listing, ItemInstance Stock, long Count)>();
        foreach (var offer in offers)
        {
            if (offer.Count < 1 || offer.Price < 0 || offer.InventoryObjectId < 1)
                return PrivateStoreProtocol.Item;
            var stock = seller.Inventory.Find(offer.InventoryObjectId);
            if (stock == null || stock.IsEquipped || !stock.Template.CanDropTradeStore || stock.Count < offer.Count)
                return PrivateStoreProtocol.Item;
            if (stock.ItemId == ShopTrade.AdenaItemId)
                return PrivateStoreProtocol.Item;
            var listing = store.PrivateBuyList.FirstOrDefault(row => row.ItemId == stock.ItemId && row.Price == offer.Price);
            if (listing == null || listing.Count < offer.Count)
                return PrivateStoreProtocol.Item;
            payout = checked(payout + listing.Price * offer.Count);
            if (payout > ShopTrade.MaxAdena)
                return PrivateStoreProtocol.Adena;
            transfers.Add((listing, stock, offer.Count));
        }

        if (store.Inventory.CountOf(ShopTrade.AdenaItemId) < payout)
            return PrivateStoreProtocol.Adena;
        long sellerAdena = seller.Inventory.CountOf(ShopTrade.AdenaItemId);
        if (sellerAdena + payout > ShopTrade.MaxAdena)
            return PrivateStoreProtocol.Adena;

        var offerProxy = transfers.Select(t => new TradeOfferRow
        {
            InventoryObjectId = t.Stock.ObjectId,
            ItemId = t.Stock.ItemId,
            Count = t.Count,
            EnchantLevel = t.Stock.EnchantLevel
        }).ToList();
        var deny = IncomingDenyOf(store, offerProxy);
        if (deny == TradeRules.IncomingDeny.Weight)
            return PrivateStoreProtocol.Weight;
        if (deny == TradeRules.IncomingDeny.Slots)
            return PrivateStoreProtocol.Slots;

        var sellerChanges = new List<InventoryUpdateEntryMsg>();
        var storeChanges = new List<InventoryUpdateEntryMsg>();

        if (!TransferAdena(store.Inventory, seller.Inventory, (int)payout, storeChanges, sellerChanges))
            return PrivateStoreProtocol.Adena;

        foreach (var (listing, stock, count) in transfers)
        {
            TransferOfferRow(seller.Inventory, store.Inventory,
                new TradeOfferRow
                {
                    InventoryObjectId = stock.ObjectId,
                    ItemId = stock.ItemId,
                    Count = count,
                    EnchantLevel = stock.EnchantLevel
                },
                sellerChanges, storeChanges);
            listing.Count -= count;
            if (listing.Count <= 0)
                store.PrivateBuyList.Remove(listing);
        }

        if (store.Session != null)
            SaveInventory(store.Session, store.Inventory);
        SaveInventory(session, seller.Inventory);

        if (sellerChanges.Count > 0)
        {
            NetBridge?.FanoutInventoryUpdate([seller.ObjectId], new InventoryUpdateMsg { Changes = sellerChanges.ToArray() });
            SelfUserInfo(seller);
        }
        if (storeChanges.Count > 0)
        {
            NetBridge?.FanoutInventoryUpdate([store.ObjectId], new InventoryUpdateMsg { Changes = storeChanges.ToArray() });
            SelfUserInfo(store);
        }

        if (store.PrivateBuyList.Count == 0)
            ClosePrivateStore(store.ObjectId, standUp: false);
        else
        {
            FanoutPrivateStoreInfo(store);
            SendPrivateStoreListBuy(seller, store);
        }

        return null;
    }

    void ClosePrivateStore(int objectId, bool standUp)
    {
        if (!_byId.TryGetValue(objectId, out var player) || player.Kind != ZoneEntityKind.Player)
            return;
        if (player.PrivateStoreType == PrivateStoreProtocol.TypeNone
            && player.PrivateSellList.Count == 0
            && player.PrivateBuyList.Count == 0
            && player.ManufactureList.Count == 0)
            return;
        player.PrivateStoreType = PrivateStoreProtocol.TypeNone;
        player.PrivateStoreMessage = "";
        player.PrivateSellList.Clear();
        player.PrivateBuyList.Clear();
        player.ManufactureList.Clear();
        if (standUp && player.Sitting)
        {
            player.Sitting = false;
            FanoutWaitType(player, ActionProtocol.WaitStanding);
        }
        FanoutPrivateStoreInfo(player);
    }

    bool StoreInRange(ZoneEntity a, ZoneEntity b) =>
        NpcHtmlProtocol.InRange(a.X, a.Y, a.Z, b.X, b.Y, b.Z, PrivateStoreProtocol.Distance);

    static string TruncateStoreMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return "";
        return message.Length <= 29 ? message : message[..29];
    }

    void FanoutPrivateStoreInfo(ZoneEntity player)
    {
        var msg = new PrivateStoreInfoMsg
        {
            ObjectId = player.ObjectId,
            StoreType = player.PrivateStoreType,
            Message = player.PrivateStoreMessage
        };
        NetBridge?.FanoutPrivateStoreInfo(NetWatchersOf(player), msg);
    }

    void SendPrivateStoreListSell(ZoneEntity visitor, ZoneEntity store)
    {
        if (visitor.Session == null || visitor.Inventory == null)
            return;
        var items = store.PrivateSellList.Select(row => new PrivateStoreListedSellRowMsg
        {
            InventoryObjectId = row.InventoryObjectId,
            ItemId = row.ItemId,
            Count = row.Count,
            Price = row.Price,
            EnchantLevel = row.EnchantLevel
        }).ToArray();
        NetBridge?.FanoutPrivateStoreListSell([visitor.ObjectId], new PrivateStoreListSellMsg
        {
            StoreObjectId = store.ObjectId,
            StoreName = store.Name,
            Message = store.PrivateStoreMessage,
            Packaged = store.PrivateStoreType == PrivateStoreProtocol.TypePackageSell ? 1 : 0,
            BuyerAdena = visitor.Inventory.CountOf(ShopTrade.AdenaItemId),
            Items = items
        });
    }

    void SendPrivateStoreListBuy(ZoneEntity visitor, ZoneEntity store)
    {
        if (visitor.Session == null || store.Inventory == null)
            return;
        var items = store.PrivateBuyList.Select(row => new PrivateStoreBuyRowMsg
        {
            ItemId = row.ItemId,
            Count = row.Count,
            Price = row.Price
        }).ToArray();
        NetBridge?.FanoutPrivateStoreListBuy([visitor.ObjectId], new PrivateStoreListBuyMsg
        {
            StoreObjectId = store.ObjectId,
            StoreName = store.Name,
            Message = store.PrivateStoreMessage,
            StoreAdena = store.Inventory.CountOf(ShopTrade.AdenaItemId),
            Items = items
        });
    }

    static bool TransferAdena(
        PlayerInventory from,
        PlayerInventory to,
        int count,
        List<InventoryUpdateEntryMsg> fromChanges,
        List<InventoryUpdateEntryMsg> toChanges)
    {
        if (count < 1)
            return true;
        var stack = from.FindByItemId(ShopTrade.AdenaItemId);
        if (stack == null || !from.TryRemoveFromStack(stack, count, out bool removedEntirely))
            return false;
        if (removedEntirely)
            fromChanges.Add(InventoryMessages.RemoveRow(stack, -1));
        else
            fromChanges.Add(InventoryMessages.ChangeRow(InventoryChange.Modify, stack));

        int before = to.CountOf(ShopTrade.AdenaItemId);
        var received = to.Add(ShopTrade.AdenaItemId, count);
        int change = before == 0 ? InventoryChange.Add : InventoryChange.Modify;
        toChanges.Add(InventoryMessages.ChangeRow(change, received));
        return true;
    }
}
