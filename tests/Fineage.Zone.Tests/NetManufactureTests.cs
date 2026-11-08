using Fineage.Game.Recipes;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Manufacture shop (#283): open workshop → list → make wooden arrows for a fee.</summary>
public sealed class NetManufactureTests
{
    const int RecipeListId = 1;
    const int Stem = 1864;
    const int IronOre = 1869;
    const int WoodenArrow = 17;

    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static object? Last(NetBridgeRecorder rec, string method) =>
        rec.Of(method).Count == 0 ? null : rec.Of(method).Last()[1];

    static void GrantCraft(NetTestKit kit, ZoneEntity player)
    {
        player.LearnedSkills[RecipeCatalog.CreateDwarvenSkillId] = 1;
        kit.World.RefreshKnownSkillsForTest(player);
        player.KnownRecipes.Add(RecipeListId);
        player.CurMp = Math.Max(player.CurMp, 50);
    }

    [Fact]
    public async Task Manufacturer_opens_shop_and_crafts_for_customer()
    {
        var (kit, rec) = World();
        var (makerSession, maker) = await kit.EnterAsync(classId: 0);
        var (customerSession, customer) = await kit.EnterAsync(classId: 1);
        GrantCraft(kit, maker);

        customer.Inventory!.Add(Stem, 4);
        customer.Inventory.Add(IronOre, 2);
        customer.Inventory.Add(ShopTrade.AdenaItemId, 500);
        int makerMpBefore = maker.CurMp;
        const long fee = 100;

        Assert.Null(await kit.World.TrySetRecipeShopAsync(makerSession, new SetRecipeShopMsg
        {
            Message = "Craft",
            Items = [new RecipeShopRowMsg { RecipeId = RecipeListId, Price = fee }]
        }, default));

        Assert.Equal(PrivateStoreProtocol.TypeManufacture, maker.PrivateStoreType);
        Assert.True(maker.Sitting);
        var info = Assert.IsType<Fineage.Net.Contract.PrivateStoreInfoNetMsg>(
            Last(rec, nameof(IWorldEventPublisher.FanoutPrivateStoreInfo)));
        Assert.Equal(PrivateStoreProtocol.TypeManufacture, info.StoreType);

        Assert.Null(await kit.World.TryPrivateStoreListAsync(customerSession, new PrivateStoreListRequestMsg
        {
            StoreObjectId = maker.ObjectId
        }, default));
        var list = Assert.IsType<Fineage.Net.Contract.RecipeShopSellListNetMsg>(
            Last(rec, nameof(IWorldEventPublisher.FanoutRecipeShopSellList)));
        Assert.Equal(maker.ObjectId, list.ManufacturerObjectId);
        Assert.Single(list.Items);
        Assert.Equal(RecipeListId, list.Items[0].RecipeId);
        Assert.Equal(fee, list.Items[0].Price);

        Assert.Null(await kit.World.TryRecipeShopMakeItemAsync(customerSession, new RecipeShopMakeItemMsg
        {
            ManufacturerObjectId = maker.ObjectId,
            RecipeId = RecipeListId
        }, default));

        var result = Assert.IsType<Fineage.Net.Contract.RecipeShopItemInfoNetMsg>(
            Last(rec, nameof(IWorldEventPublisher.FanoutRecipeShopItemInfo)));
        Assert.Equal(0, result.Result);
        Assert.Equal(0, customer.Inventory.CountOf(Stem));
        Assert.Equal(0, customer.Inventory.CountOf(IronOre));
        Assert.Equal(500, customer.Inventory.CountOf(WoodenArrow));
        Assert.Equal(400, customer.Inventory.CountOf(ShopTrade.AdenaItemId));
        Assert.True(maker.Inventory!.CountOf(ShopTrade.AdenaItemId) >= fee);
        Assert.True(maker.CurMp < makerMpBefore);
        Assert.Equal(PrivateStoreProtocol.TypeManufacture, maker.PrivateStoreType);
    }

    [Fact]
    public async Task Make_without_materials_is_refused()
    {
        var (kit, _) = World();
        var (makerSession, maker) = await kit.EnterAsync(classId: 0);
        var (customerSession, customer) = await kit.EnterAsync(classId: 1);
        GrantCraft(kit, maker);
        customer.Inventory!.Add(ShopTrade.AdenaItemId, 500);

        Assert.Null(await kit.World.TrySetRecipeShopAsync(makerSession, new SetRecipeShopMsg
        {
            Items = [new RecipeShopRowMsg { RecipeId = RecipeListId, Price = 50 }]
        }, default));

        Assert.Equal(CraftProtocol.Materials,
            await kit.World.TryRecipeShopMakeItemAsync(customerSession, new RecipeShopMakeItemMsg
            {
                ManufacturerObjectId = maker.ObjectId,
                RecipeId = RecipeListId
            }, default));
    }

    [Fact]
    public async Task Empty_set_closes_shop()
    {
        var (kit, _) = World();
        var (makerSession, maker) = await kit.EnterAsync(classId: 0);
        GrantCraft(kit, maker);

        Assert.Null(await kit.World.TrySetRecipeShopAsync(makerSession, new SetRecipeShopMsg
        {
            Items = [new RecipeShopRowMsg { RecipeId = RecipeListId, Price = 1 }]
        }, default));
        Assert.Equal(PrivateStoreProtocol.TypeManufacture, maker.PrivateStoreType);

        Assert.Null(await kit.World.TrySetRecipeShopAsync(makerSession, new SetRecipeShopMsg { Items = [] }, default));
        Assert.Equal(PrivateStoreProtocol.TypeNone, maker.PrivateStoreType);
        Assert.Empty(maker.ManufactureList);
    }
}
