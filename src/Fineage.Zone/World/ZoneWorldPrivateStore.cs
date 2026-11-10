using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Trade;
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

/// <summary>One row in a player's private buy list (Mobius TradeList buy wish).</summary>
public sealed class PrivateStoreBuyListing
{
    public int ItemId { get; set; }
    public long Count { get; set; }
    public long Price { get; set; }
}

/// <summary>One manufacture workshop row (Mobius <c>ManufactureItem</c>).</summary>
public sealed class ManufactureListing
{
    public int RecipeId { get; set; }
    public long Price { get; set; }
}

/// <summary>
/// Mobius private store: sell (#275), buy (#277), package-sell (#279). Manufacture → <c>ZoneWorldManufacture</c>.
/// </summary>
public sealed partial class ZoneWorld
{
    public Task<string?> TrySetPrivateStoreSellAsync(ZoneSession session, SetPrivateStoreSellMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        return Task.FromResult(WithDeferredInventoryPersist(() => TrySetPrivateStoreSell(session, request)));
    }

    public Task<string?> TrySetPrivateStoreBuyAsync(ZoneSession session, SetPrivateStoreBuyMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        return Task.FromResult(WithDeferredInventoryPersist(() => TrySetPrivateStoreBuy(session, request)));
    }

    public Task<string?> TryPrivateStoreQuitAsync(ZoneSession session, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        return Task.FromResult(WithDeferredInventoryPersist(() =>
        {
            ClosePrivateStore(session.ObjectId, standUp: false);
            return (string?)null;
        }));
    }

    public Task<string?> TryPrivateStoreListAsync(ZoneSession session, PrivateStoreListRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        return Task.FromResult(WithDeferredInventoryPersist(() => TryPrivateStoreList(session, request)));
    }

    public Task<string?> TryPrivateStoreBuyAsync(ZoneSession session, PrivateStoreBuyMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        // World mutate under _gate; Postgres inventory write after unlock (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() => TryPrivateStoreBuy(session, request)));
    }

    public Task<string?> TryPrivateStoreSellToAsync(ZoneSession session, PrivateStoreSellToMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        return Task.FromResult(WithDeferredInventoryPersist(() => TryPrivateStoreSellTo(session, request)));
    }

    void PrivateStorePlayerLeftWorld(int objectId) => ClosePrivateStore(objectId, standUp: false);

    string? GuardOpenPrivateStore(ZoneEntity player)
    {
        if (player.Dead)
            return InventoryProtocol.Dead;
        long now = _clock().ToUnixTimeMilliseconds();
        if (ClanRules.IsInCombat(player.CombatUntilUnixMs, now) || IsCasting(player, now))
            return PrivateStoreProtocol.Combat;
        if (_tradeByPlayer.ContainsKey(player.ObjectId) || _pendingTradeAsk.ContainsKey(player.ObjectId))
            return PrivateStoreProtocol.Busy;
        if (player.MoveActive)
            return PrivateStoreProtocol.Busy;
        return null;
    }

    void ActivatePrivateStore(ZoneEntity player, byte type, string? message)
    {
        player.PrivateStoreType = type;
        player.PrivateStoreMessage = TruncateStoreMessage(message);
        if (!player.Sitting)
        {
            player.Sitting = true;
            player.Running = false;
            FanoutWaitType(player, ActionProtocol.WaitSitting);
            FanoutMoveType(player);
        }
        FanoutPrivateStoreInfo(player);
    }

    string? TrySetPrivateStoreSell(ZoneSession session, SetPrivateStoreSellMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (GuardOpenPrivateStore(player) is { } blocked)
            return blocked;

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
            if (PrivateStoreRules.SellRowShapeError(row.Count, row.Price, row.InventoryObjectId) is { } shape)
                return shape;
            if (!seen.Add(row.InventoryObjectId))
                return PrivateStoreProtocol.Item;
            var item = player.Inventory.Find(row.InventoryObjectId);
            if (PrivateStoreRules.SellStockError(
                    found: item != null,
                    equipped: item?.IsEquipped == true,
                    canDropTradeStore: item?.Template.CanDropTradeStore == true,
                    stackable: item?.Template.Stackable == true,
                    count: row.Count,
                    available: item?.Count ?? 0,
                    itemId: item?.ItemId ?? 0) is { } stock)
                return stock;
            if (PrivateStoreRules.AdenaLineError(row.Price, row.Count, totalPrice) is { } adena)
                return adena;
            totalPrice = checked(totalPrice + row.Price * row.Count);
            listings.Add(new PrivateStoreSellListing
            {
                InventoryObjectId = item!.ObjectId,
                ItemId = item.ItemId,
                Count = row.Count,
                Price = row.Price,
                EnchantLevel = item.EnchantLevel
            });
        }

        player.PrivateBuyList.Clear();
        player.ManufactureList.Clear();
        player.PrivateSellList.Clear();
        player.PrivateSellList.AddRange(listings);
        byte type = request.Packaged ? PrivateStoreProtocol.TypePackageSell : PrivateStoreProtocol.TypeSell;
        ActivatePrivateStore(player, type, request.Message);
        return null;
    }

    string? TrySetPrivateStoreBuy(ZoneSession session, SetPrivateStoreBuyMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (GuardOpenPrivateStore(player) is { } blocked)
            return blocked;

        var rows = request.Items ?? [];
        if (rows.Length == 0)
        {
            ClosePrivateStore(player.ObjectId, standUp: false);
            return null;
        }

        int limit = PrivateStoreProtocol.BuySlotLimit(player.Race);
        if (rows.Length > limit)
            return PrivateStoreProtocol.Limit;

        var listings = new List<PrivateStoreBuyListing>(rows.Length);
        long totalCost = 0;
        var seen = new HashSet<int>();
        foreach (var row in rows)
        {
            if (PrivateStoreRules.BuyRowShapeError(row.Count, row.Price, row.ItemId) is { } shape)
                return shape;
            if (row.ItemId == ShopTrade.AdenaItemId)
                return PrivateStoreProtocol.Item;
            if (!seen.Add(row.ItemId))
                return PrivateStoreProtocol.Item;
            if (!_game.Items.TryGet(row.ItemId, out var template) || template == null || !template.CanDropTradeStore)
                return PrivateStoreProtocol.Item;
            if (!template.Stackable && row.Count != 1)
                return PrivateStoreProtocol.Item;
            if (PrivateStoreRules.AdenaLineError(row.Price, row.Count, totalCost) is { } adena)
                return adena;
            totalCost = checked(totalCost + row.Price * row.Count);
            listings.Add(new PrivateStoreBuyListing
            {
                ItemId = row.ItemId,
                Count = row.Count,
                Price = row.Price
            });
        }

        if (player.Inventory.CountOf(ShopTrade.AdenaItemId) < totalCost)
            return PrivateStoreProtocol.Adena;

        player.PrivateSellList.Clear();
        player.ManufactureList.Clear();
        player.PrivateBuyList.Clear();
        player.PrivateBuyList.AddRange(listings);
        ActivatePrivateStore(player, PrivateStoreProtocol.TypeBuy, request.Message);
        return null;
    }

}
