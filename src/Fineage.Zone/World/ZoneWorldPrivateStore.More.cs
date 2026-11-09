using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Trade;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? TryPrivateStoreList(ZoneSession session, PrivateStoreListRequestMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var visitor) || visitor.Inventory == null)
            return "world";
        if (!_byId.TryGetValue(request.StoreObjectId, out var store) || store.Kind != ZoneEntityKind.Player)
            return PrivateStoreProtocol.Closed;
        if (!StoreInRange(visitor, store))
            return PrivateStoreProtocol.Range;

        if (PrivateStoreProtocol.IsSellStore(store.PrivateStoreType) && store.PrivateSellList.Count > 0)
        {
            SendPrivateStoreListSell(visitor, store);
            return null;
        }
        if (store.PrivateStoreType == PrivateStoreProtocol.TypeBuy && store.PrivateBuyList.Count > 0)
        {
            SendPrivateStoreListBuy(visitor, store);
            return null;
        }
        if (store.PrivateStoreType == PrivateStoreProtocol.TypeManufacture && store.ManufactureList.Count > 0)
        {
            SendRecipeShopSellList(visitor, store);
            return null;
        }
        return PrivateStoreProtocol.Closed;
    }

    string? TryPrivateStoreBuy(ZoneSession session, PrivateStoreBuyMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var buyer) || buyer.Inventory == null)
            return "world";
        if (buyer.Dead)
            return InventoryProtocol.Dead;
        if (!_byId.TryGetValue(request.StoreObjectId, out var store) || store.Kind != ZoneEntityKind.Player || store.Inventory == null)
            return PrivateStoreProtocol.Closed;
        if (store.ObjectId == buyer.ObjectId)
            return "bad_request";
        if (!PrivateStoreProtocol.IsSellStore(store.PrivateStoreType) || store.PrivateSellList.Count == 0)
            return PrivateStoreProtocol.Closed;
        if (!StoreInRange(buyer, store))
            return PrivateStoreProtocol.Range;
        if (store.Session != null && (_tradeByPlayer.ContainsKey(store.ObjectId) || _pendingTradeAsk.ContainsKey(store.ObjectId)))
            return PrivateStoreProtocol.Busy;

        var wants = request.Items ?? [];
        if (wants.Length == 0)
            return PrivateStoreProtocol.Item;
        // Mobius RequestPrivateStoreBuy: package must request every listed row.
        if (store.PrivateStoreType == PrivateStoreProtocol.TypePackageSell
            && wants.Length < store.PrivateSellList.Count)
            return PrivateStoreProtocol.Package;

        long cost = 0;
        var transfers = new List<(PrivateStoreSellListing Listing, long Count)>();
        var covered = new HashSet<int>();
        foreach (var want in wants)
        {
            if (want.Count < 1 || want.Price < 0)
                return PrivateStoreProtocol.Item;
            var listing = store.PrivateSellList.FirstOrDefault(row => row.InventoryObjectId == want.InventoryObjectId);
            if (listing == null || listing.Count < want.Count || listing.Price != want.Price)
                return PrivateStoreProtocol.Item;
            var stock = store.Inventory.Find(listing.InventoryObjectId);
            if (stock == null || stock.IsEquipped || stock.Count < want.Count || stock.ItemId != listing.ItemId)
                return PrivateStoreProtocol.Item;
            cost = checked(cost + listing.Price * want.Count);
            if (cost > ShopTrade.MaxAdena)
                return PrivateStoreProtocol.Adena;
            transfers.Add((listing, want.Count));
            covered.Add(listing.InventoryObjectId);
        }
        if (store.PrivateStoreType == PrivateStoreProtocol.TypePackageSell
            && (covered.Count != store.PrivateSellList.Count
                || transfers.Any(t => t.Count != t.Listing.Count)))
            return PrivateStoreProtocol.Package;

        if (buyer.Inventory.CountOf(ShopTrade.AdenaItemId) < cost)
            return PrivateStoreProtocol.Adena;
        long sellerAdena = store.Inventory.CountOf(ShopTrade.AdenaItemId);
        if (sellerAdena + cost > ShopTrade.MaxAdena)
            return PrivateStoreProtocol.Adena;

        var offerProxy = transfers.Select(t => new TradeOfferRow
        {
            InventoryObjectId = t.Listing.InventoryObjectId,
            ItemId = t.Listing.ItemId,
            Count = t.Count,
            EnchantLevel = t.Listing.EnchantLevel
        }).ToList();
        var deny = IncomingDenyOf(buyer, offerProxy);
        if (deny == TradeRules.IncomingDeny.Weight)
            return PrivateStoreProtocol.Weight;
        if (deny == TradeRules.IncomingDeny.Slots)
            return PrivateStoreProtocol.Slots;

        var buyerChanges = new List<InventoryUpdateEntryMsg>();
        var sellerChanges = new List<InventoryUpdateEntryMsg>();

        // Take adena from buyer → seller.
        if (!TransferAdena(buyer.Inventory, store.Inventory, (int)cost, buyerChanges, sellerChanges))
            return PrivateStoreProtocol.Adena;

        foreach (var (listing, count) in transfers)
        {
            TransferOfferRow(store.Inventory, buyer.Inventory,
                new TradeOfferRow
                {
                    InventoryObjectId = listing.InventoryObjectId,
                    ItemId = listing.ItemId,
                    Count = count,
                    EnchantLevel = listing.EnchantLevel
                },
                sellerChanges, buyerChanges);
            listing.Count -= count;
            if (listing.Count <= 0)
                store.PrivateSellList.Remove(listing);
        }

        if (store.Session != null)
            SaveInventory(store.Session, store.Inventory);
        SaveInventory(session, buyer.Inventory);

        if (buyerChanges.Count > 0)
        {
            NetBridge?.FanoutInventoryUpdate([buyer.ObjectId], new InventoryUpdateMsg { Changes = buyerChanges.ToArray() });
            SelfUserInfo(buyer);
        }
        if (sellerChanges.Count > 0)
        {
            NetBridge?.FanoutInventoryUpdate([store.ObjectId], new InventoryUpdateMsg { Changes = sellerChanges.ToArray() });
            SelfUserInfo(store);
        }

        if (store.PrivateSellList.Count == 0)
            ClosePrivateStore(store.ObjectId, standUp: false);
        else
        {
            FanoutPrivateStoreInfo(store);
            SendPrivateStoreListSell(buyer, store);
        }

        return null;
    }

}
