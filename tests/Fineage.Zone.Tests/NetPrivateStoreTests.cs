using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Private sell store (#275): open → list → buy → auto-close when empty.</summary>
public sealed class NetPrivateStoreTests
{
    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static object? Last(NetBridgeRecorder rec, string method) =>
        rec.Of(method).Count == 0 ? null : rec.Of(method).Last()[1];

    static ItemInstance Tradable(ZoneEntity player) =>
        player.Inventory!.Items.First(item =>
            !item.IsEquipped && item.Template.CanDropTradeStore && item.ItemId != ShopTrade.AdenaItemId && item.Count >= 1);

    static void GiveAdena(ZoneEntity player, int amount)
    {
        player.Inventory!.Add(ShopTrade.AdenaItemId, amount);
    }

    [Fact]
    public async Task Seller_opens_store_sits_and_buyer_purchases()
    {
        var (kit, rec) = World();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, buyerEntity) = await kit.EnterAsync(classId: 1);

        var offered = Tradable(sellerEntity);
        int itemId = offered.ItemId;
        int objectId = offered.ObjectId;
        const long price = 100;
        GiveAdena(buyerEntity, 500);

        Assert.Null(await kit.World.TrySetPrivateStoreSellAsync(seller, new SetPrivateStoreSellMsg
        {
            Message = "WTS",
            Items =
            [
                new PrivateStoreSellRowMsg { InventoryObjectId = objectId, Count = 1, Price = price }
            ]
        }, default));

        Assert.Equal(PrivateStoreProtocol.TypeSell, sellerEntity.PrivateStoreType);
        Assert.True(sellerEntity.Sitting);
        Assert.Equal("WTS", sellerEntity.PrivateStoreMessage);

        var info = Assert.IsType<Fineage.Net.Contract.PrivateStoreInfoNetMsg>(
            Last(rec, nameof(INetPresenceBridge.FanoutPrivateStoreInfo)));
        Assert.Equal(sellerEntity.ObjectId, info.ObjectId);
        Assert.Equal(PrivateStoreProtocol.TypeSell, info.StoreType);

        Assert.Null(await kit.World.TryPrivateStoreListAsync(buyer, new PrivateStoreListRequestMsg
        {
            StoreObjectId = sellerEntity.ObjectId
        }, default));

        var list = Assert.IsType<Fineage.Net.Contract.PrivateStoreListSellNetMsg>(
            Last(rec, nameof(INetPresenceBridge.FanoutPrivateStoreListSell)));
        Assert.Equal(sellerEntity.ObjectId, list.StoreObjectId);
        Assert.Single(list.Items);
        Assert.Equal(objectId, list.Items[0].InventoryObjectId);

        Assert.Null(await kit.World.TryPrivateStoreBuyAsync(buyer, new PrivateStoreBuyMsg
        {
            StoreObjectId = sellerEntity.ObjectId,
            Items =
            [
                new PrivateStoreSellRowMsg { InventoryObjectId = objectId, Count = 1, Price = price }
            ]
        }, default));

        Assert.DoesNotContain(sellerEntity.Inventory!.Items, item => item.ObjectId == objectId);
        Assert.Contains(buyerEntity.Inventory!.Items, item => item.ItemId == itemId);
        Assert.Equal(400, buyerEntity.Inventory.CountOf(ShopTrade.AdenaItemId));
        Assert.True(sellerEntity.Inventory.CountOf(ShopTrade.AdenaItemId) >= price);
        Assert.Equal(PrivateStoreProtocol.TypeNone, sellerEntity.PrivateStoreType);
        Assert.Empty(sellerEntity.PrivateSellList);
    }

    [Fact]
    public async Task Quit_clears_store_without_standing()
    {
        var (kit, _) = World();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var offered = Tradable(sellerEntity);

        Assert.Null(await kit.World.TrySetPrivateStoreSellAsync(seller, new SetPrivateStoreSellMsg
        {
            Items = [new PrivateStoreSellRowMsg { InventoryObjectId = offered.ObjectId, Count = 1, Price = 10 }]
        }, default));
        Assert.True(sellerEntity.Sitting);

        Assert.Null(await kit.World.TryPrivateStoreQuitAsync(seller, default));
        Assert.Equal(PrivateStoreProtocol.TypeNone, sellerEntity.PrivateStoreType);
        Assert.True(sellerEntity.Sitting);
    }

    [Fact]
    public async Task Buy_rejects_wrong_price_and_insufficient_adena()
    {
        var (kit, _) = World();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, buyerEntity) = await kit.EnterAsync(classId: 1);
        var offered = Tradable(sellerEntity);

        Assert.Null(await kit.World.TrySetPrivateStoreSellAsync(seller, new SetPrivateStoreSellMsg
        {
            Items = [new PrivateStoreSellRowMsg { InventoryObjectId = offered.ObjectId, Count = 1, Price = 50 }]
        }, default));

        Assert.Equal(PrivateStoreProtocol.Item, await kit.World.TryPrivateStoreBuyAsync(buyer, new PrivateStoreBuyMsg
        {
            StoreObjectId = sellerEntity.ObjectId,
            Items = [new PrivateStoreSellRowMsg { InventoryObjectId = offered.ObjectId, Count = 1, Price = 1 }]
        }, default));

        GiveAdena(buyerEntity, 10);
        Assert.Equal(PrivateStoreProtocol.Adena, await kit.World.TryPrivateStoreBuyAsync(buyer, new PrivateStoreBuyMsg
        {
            StoreObjectId = sellerEntity.ObjectId,
            Items = [new PrivateStoreSellRowMsg { InventoryObjectId = offered.ObjectId, Count = 1, Price = 50 }]
        }, default));
        Assert.Equal(PrivateStoreProtocol.TypeSell, sellerEntity.PrivateStoreType);
    }

    [Fact]
    public async Task Buyer_opens_store_and_visitor_sells_into_it()
    {
        var (kit, rec) = World();
        // Fighter starter bag has unequipped tradables; mystic (1) often does not.
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, buyerEntity) = await kit.EnterAsync(classId: 1);

        var offered = Tradable(sellerEntity);
        int itemId = offered.ItemId;
        int objectId = offered.ObjectId;
        const long price = 75;
        GiveAdena(buyerEntity, 500);

        Assert.Equal(PrivateStoreProtocol.Adena, await kit.World.TrySetPrivateStoreBuyAsync(buyer, new SetPrivateStoreBuyMsg
        {
            Message = "WTB",
            Items = [new PrivateStoreBuyRowMsg { ItemId = itemId, Count = 1, Price = 600 }]
        }, default));

        Assert.Null(await kit.World.TrySetPrivateStoreBuyAsync(buyer, new SetPrivateStoreBuyMsg
        {
            Message = "WTB",
            Items = [new PrivateStoreBuyRowMsg { ItemId = itemId, Count = 1, Price = price }]
        }, default));

        Assert.Equal(PrivateStoreProtocol.TypeBuy, buyerEntity.PrivateStoreType);
        Assert.True(buyerEntity.Sitting);
        Assert.Empty(buyerEntity.PrivateSellList);
        Assert.Single(buyerEntity.PrivateBuyList);

        Assert.Null(await kit.World.TryPrivateStoreListAsync(seller, new PrivateStoreListRequestMsg
        {
            StoreObjectId = buyerEntity.ObjectId
        }, default));

        var list = Assert.IsType<Fineage.Net.Contract.PrivateStoreListBuyNetMsg>(
            Last(rec, nameof(INetPresenceBridge.FanoutPrivateStoreListBuy)));
        Assert.Equal(buyerEntity.ObjectId, list.StoreObjectId);
        Assert.Equal(itemId, Assert.Single(list.Items).ItemId);

        Assert.Null(await kit.World.TryPrivateStoreSellToAsync(seller, new PrivateStoreSellToMsg
        {
            StoreObjectId = buyerEntity.ObjectId,
            Items =
            [
                new PrivateStoreSellRowMsg { InventoryObjectId = objectId, Count = 1, Price = price }
            ]
        }, default));

        Assert.DoesNotContain(sellerEntity.Inventory!.Items, item => item.ObjectId == objectId);
        Assert.Contains(buyerEntity.Inventory!.Items, item => item.ItemId == itemId);
        Assert.Equal(425, buyerEntity.Inventory.CountOf(ShopTrade.AdenaItemId));
        Assert.True(sellerEntity.Inventory.CountOf(ShopTrade.AdenaItemId) >= price);
        Assert.Equal(PrivateStoreProtocol.TypeNone, buyerEntity.PrivateStoreType);
        Assert.Empty(buyerEntity.PrivateBuyList);
    }

    [Fact]
    public async Task Package_sell_rejects_partial_and_buys_all()
    {
        var (kit, rec) = World();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, buyerEntity) = await kit.EnterAsync(classId: 1);

        var rows = sellerEntity.Inventory!.Items
            .Where(item => !item.IsEquipped && item.Template.CanDropTradeStore && item.ItemId != ShopTrade.AdenaItemId && item.Count >= 1)
            .Take(2)
            .ToList();
        Assert.True(rows.Count >= 2, "fighter starter needs two unequipped tradables for package sell");
        GiveAdena(buyerEntity, 1000);

        Assert.Null(await kit.World.TrySetPrivateStoreSellAsync(seller, new SetPrivateStoreSellMsg
        {
            Message = "PKG",
            Packaged = true,
            Items =
            [
                new PrivateStoreSellRowMsg { InventoryObjectId = rows[0].ObjectId, Count = 1, Price = 40 },
                new PrivateStoreSellRowMsg { InventoryObjectId = rows[1].ObjectId, Count = 1, Price = 60 }
            ]
        }, default));

        Assert.Equal(PrivateStoreProtocol.TypePackageSell, sellerEntity.PrivateStoreType);

        Assert.Null(await kit.World.TryPrivateStoreListAsync(buyer, new PrivateStoreListRequestMsg
        {
            StoreObjectId = sellerEntity.ObjectId
        }, default));
        Assert.Equal(1, Assert.IsType<Fineage.Net.Contract.PrivateStoreListSellNetMsg>(
            Last(rec, nameof(INetPresenceBridge.FanoutPrivateStoreListSell))).Packaged);

        Assert.Equal(PrivateStoreProtocol.Package, await kit.World.TryPrivateStoreBuyAsync(buyer, new PrivateStoreBuyMsg
        {
            StoreObjectId = sellerEntity.ObjectId,
            Items =
            [
                new PrivateStoreSellRowMsg { InventoryObjectId = rows[0].ObjectId, Count = 1, Price = 40 }
            ]
        }, default));
        Assert.Equal(PrivateStoreProtocol.TypePackageSell, sellerEntity.PrivateStoreType);

        Assert.Null(await kit.World.TryPrivateStoreBuyAsync(buyer, new PrivateStoreBuyMsg
        {
            StoreObjectId = sellerEntity.ObjectId,
            Items =
            [
                new PrivateStoreSellRowMsg { InventoryObjectId = rows[0].ObjectId, Count = 1, Price = 40 },
                new PrivateStoreSellRowMsg { InventoryObjectId = rows[1].ObjectId, Count = 1, Price = 60 }
            ]
        }, default));

        Assert.Equal(PrivateStoreProtocol.TypeNone, sellerEntity.PrivateStoreType);
        Assert.Empty(sellerEntity.PrivateSellList);
        Assert.Equal(900, buyerEntity.Inventory!.CountOf(ShopTrade.AdenaItemId));
        Assert.True(buyerEntity.Inventory.CountOf(rows[0].ItemId) >= 1);
        Assert.True(buyerEntity.Inventory.CountOf(rows[1].ItemId) >= 1);
    }
}
