using Fineage.Game;
using Fineage.Game.Recipes;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Craft;
using Fineage.Zone.Trade;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void SendRecipeShopSellList(ZoneEntity visitor, ZoneEntity maker)
    {
        if (visitor.Session == null || visitor.Inventory == null)
            return;
        var items = maker.ManufactureList.Select(row => new RecipeShopRowMsg
        {
            RecipeId = row.RecipeId,
            Price = row.Price
        }).ToArray();
        NetBridge?.FanoutRecipeShopSellList([visitor.ObjectId], new RecipeShopSellListMsg
        {
            ManufacturerObjectId = maker.ObjectId,
            CurrentMp = maker.CurMp,
            MaxMp = maker.MaxMp,
            BuyerAdena = visitor.Inventory.CountOf(ShopTrade.AdenaItemId),
            Items = items
        });
    }

    void FanoutRecipeShopItemInfo(int visitorObjectId, ZoneEntity maker, int recipeId, int result)
    {
        NetBridge?.FanoutRecipeShopItemInfo([visitorObjectId], new RecipeShopItemInfoMsg
        {
            ManufacturerObjectId = maker.ObjectId,
            RecipeId = recipeId,
            CurrentMp = maker.CurMp,
            MaxMp = maker.MaxMp,
            Result = result
        });
    }
}
