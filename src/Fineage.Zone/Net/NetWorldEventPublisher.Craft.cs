using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutRecipeBookList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeBookListMsg list)
    {
        var payload = NetCodec.EncodeRecipeBookList(new RecipeBookListNetMsg
        {
            Dwarven = list.Dwarven,
            RecipeIds = list.RecipeIds ?? []
        });
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.RecipeBookList, payload);
    }

    void IWorldEventPublisher.FanoutRecipeItemMakeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeItemMakeInfoMsg info)
    {
        var payload = NetCodec.EncodeRecipeItemMakeInfo(new RecipeItemMakeInfoNetMsg
        {
            RecipeId = info.RecipeId,
            Result = info.Result,
            CurrentMp = info.CurrentMp,
            MaxMp = info.MaxMp
        });
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.RecipeItemMakeInfo, payload);
    }

    void IWorldEventPublisher.FanoutRecipeShopSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeShopSellListMsg list)
    {
        var src = list.Items ?? [];
        var items = new RecipeShopRowNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new RecipeShopRowNet
            {
                RecipeId = src[i].RecipeId,
                Price = src[i].Price
            };
        }
        var payload = NetCodec.EncodeRecipeShopSellList(new RecipeShopSellListNetMsg
        {
            ManufacturerObjectId = list.ManufacturerObjectId,
            CurrentMp = list.CurrentMp,
            MaxMp = list.MaxMp,
            BuyerAdena = list.BuyerAdena,
            Items = items
        });
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.RecipeShopSellList, payload);
    }

    void IWorldEventPublisher.FanoutRecipeShopItemInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.RecipeShopItemInfoMsg info)
    {
        var payload = NetCodec.EncodeRecipeShopItemInfo(new RecipeShopItemInfoNetMsg
        {
            ManufacturerObjectId = info.ManufacturerObjectId,
            RecipeId = info.RecipeId,
            CurrentMp = info.CurrentMp,
            MaxMp = info.MaxMp,
            Result = info.Result
        });
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.RecipeShopItemInfo, payload);
    }
}
