using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

/// <summary>One row in a player's private sell list (Mobius TradeList sell item).</summary>
public sealed class PrivateStoreSellListing
{
    public int InventoryObjectId { get; set; }
    public int ItemId { get; set; }
    public long Count { get; set; }
    public long Price { get; set; }
    public int EnchantLevel { get; set; }
}

/// <summary>
/// Mobius private sell store: SetPrivateStoreListSell / RequestPrivateStoreBuy / Quit.
/// Buy-store, package-sell, and manufacture are out of scope for #275.
/// </summary>
public sealed partial class ZoneWorld
{
    public async Task<string?> TrySetPrivateStoreSellAsync(ZoneSession session, SetPrivateStoreSellMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TrySetPrivateStoreSell(session, request);
        return error;
    }

    public async Task<string?> TryPrivateStoreQuitAsync(ZoneSession session, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";
        lock (_gate)
            ClosePrivateStore(session.ObjectId, standUp: false);
        return null;
    }

    public async Task<string?> TryPrivateStoreListAsync(ZoneSession session, PrivateStoreListRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryPrivateStoreList(session, request);
        return error;
    }

    public async Task<string?> TryPrivateStoreBuyAsync(ZoneSession session, PrivateStoreBuyMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryPrivateStoreBuy(session, request);
        return error;
    }

    void PrivateStorePlayerLeftWorld(int objectId) => ClosePrivateStore(objectId, standUp: false);

    string? TrySetPrivateStoreSell(ZoneSession session, SetPrivateStoreSellMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        long now = _clock().ToUnixTimeMilliseconds();
        if (ClanRules.IsInCombat(player.CombatUntilUnixMs, now) || IsCasting(player, now))
            return PrivateStoreProtocol.Combat;
        if (_tradeByPlayer.ContainsKey(player.ObjectId) || _pendingTradeAsk.ContainsKey(player.ObjectId))
            return PrivateStoreProtocol.Busy;
        if (player.MoveActive)
            return PrivateStoreProtocol.Busy;

        var rows = request.Items ?? [];
        if (rows.Length == 0)
        {
            ClosePrivateStore(player.ObjectId, standUp: false);
            return null;
        }

        int limit = PrivateStoreProtocol.SellSlotLimit(player.Race);
        if (rows.Length > limit)
            return PrivateStoreProtocol.Limit;

        var listings = new List<PrivateStoreSellListing>(rows.Length);
        long totalPrice = player.Inventory.CountOf(ShopTrade.AdenaItemId);
        var seen = new HashSet<int>();
        foreach (var row in rows)
        {
            if (row.Count < 1 || row.Price < 0 || row.InventoryObjectId < 1)
                return PrivateStoreProtocol.Item;
            if (!seen.Add(row.InventoryObjectId))
                return PrivateStoreProtocol.Item;
            var item = player.Inventory.Find(row.InventoryObjectId);
            if (item == null || item.IsEquipped || !item.Template.CanDropTradeStore || item.Count < row.Count)
                return PrivateStoreProtocol.Item;
            if (!item.Template.Stackable && row.Count != 1)
                return PrivateStoreProtocol.Item;
            if (item.ItemId == ShopTrade.AdenaItemId)
                return PrivateStoreProtocol.Item;
            long line = checked(row.Price * row.Count);
            if (line > ShopTrade.MaxAdena)
                return PrivateStoreProtocol.Adena;
            totalPrice = checked(totalPrice + line);
            if (totalPrice > ShopTrade.MaxAdena)
                return PrivateStoreProtocol.Adena;
            listings.Add(new PrivateStoreSellListing
            {
                InventoryObjectId = item.ObjectId,
                ItemId = item.ItemId,
                Count = row.Count,
                Price = row.Price,
                EnchantLevel = item.EnchantLevel
            });
        }

        player.PrivateSellList.Clear();
        player.PrivateSellList.AddRange(listings);
        player.PrivateStoreType = PrivateStoreProtocol.TypeSell;
        player.PrivateStoreMessage = TruncateStoreMessage(request.Message);

        if (!player.Sitting)
        {
            player.Sitting = true;
            player.Running = false;
            FanoutWaitType(player, ActionProtocol.WaitSitting);
            FanoutMoveType(player);
        }

        FanoutPrivateStoreInfo(player);
        return null;
    }

    string? TryPrivateStoreList(ZoneSession session, PrivateStoreListRequestMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var buyer) || buyer.Inventory == null)
            return "world";
        if (!_byId.TryGetValue(request.StoreObjectId, out var store) || store.Kind != ZoneEntityKind.Player)
            return PrivateStoreProtocol.Closed;
        if (store.PrivateStoreType != PrivateStoreProtocol.TypeSell || store.PrivateSellList.Count == 0)
            return PrivateStoreProtocol.Closed;
        if (!StoreInRange(buyer, store))
            return PrivateStoreProtocol.Range;

        SendPrivateStoreList(buyer, store);
        return null;
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
        if (store.PrivateStoreType != PrivateStoreProtocol.TypeSell || store.PrivateSellList.Count == 0)
            return PrivateStoreProtocol.Closed;
        if (!StoreInRange(buyer, store))
            return PrivateStoreProtocol.Range;
        if (store.Session != null && (_tradeByPlayer.ContainsKey(store.ObjectId) || _pendingTradeAsk.ContainsKey(store.ObjectId)))
            return PrivateStoreProtocol.Busy;

        var wants = request.Items ?? [];
        if (wants.Length == 0)
            return PrivateStoreProtocol.Item;

        long cost = 0;
        var transfers = new List<(PrivateStoreSellListing Listing, long Count)>();
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
        }

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
        if (deny == TradeIncomingDeny.Weight)
            return PrivateStoreProtocol.Weight;
        if (deny == TradeIncomingDeny.Slots)
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
            NetBridge?.FanoutInventoryUpdate([buyer.ObjectId],
                ToNetInventoryUpdate(new InventoryUpdateMsg { Changes = buyerChanges.ToArray() }));
            SelfUserInfo(buyer);
        }
        if (sellerChanges.Count > 0)
        {
            NetBridge?.FanoutInventoryUpdate([store.ObjectId],
                ToNetInventoryUpdate(new InventoryUpdateMsg { Changes = sellerChanges.ToArray() }));
            SelfUserInfo(store);
        }

        if (store.PrivateSellList.Count == 0)
            ClosePrivateStore(store.ObjectId, standUp: false);
        else
        {
            FanoutPrivateStoreInfo(store);
            SendPrivateStoreList(buyer, store);
        }

        return null;
    }

    void ClosePrivateStore(int objectId, bool standUp)
    {
        if (!_byId.TryGetValue(objectId, out var player) || player.Kind != ZoneEntityKind.Player)
            return;
        if (player.PrivateStoreType == PrivateStoreProtocol.TypeNone && player.PrivateSellList.Count == 0)
            return;
        player.PrivateStoreType = PrivateStoreProtocol.TypeNone;
        player.PrivateStoreMessage = "";
        player.PrivateSellList.Clear();
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
        var msg = new Fineage.Net.Contract.PrivateStoreInfoNetMsg
        {
            ObjectId = player.ObjectId,
            StoreType = player.PrivateStoreType,
            Message = player.PrivateStoreMessage
        };
        NetBridge?.FanoutPrivateStoreInfo(NetWatchersOf(player), msg);
    }

    void SendPrivateStoreList(ZoneEntity buyer, ZoneEntity store)
    {
        if (buyer.Session == null || buyer.Inventory == null)
            return;
        var items = store.PrivateSellList.Select(row => new Fineage.Net.Contract.PrivateStoreSellRowNet
        {
            InventoryObjectId = row.InventoryObjectId,
            ItemId = row.ItemId,
            Count = row.Count,
            Price = row.Price,
            EnchantLevel = row.EnchantLevel
        }).ToArray();
        NetBridge?.FanoutPrivateStoreListSell([buyer.ObjectId], new Fineage.Net.Contract.PrivateStoreListSellNetMsg
        {
            StoreObjectId = store.ObjectId,
            StoreName = store.Name,
            Message = store.PrivateStoreMessage,
            BuyerAdena = buyer.Inventory.CountOf(ShopTrade.AdenaItemId),
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
