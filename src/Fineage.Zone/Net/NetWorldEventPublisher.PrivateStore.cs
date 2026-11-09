using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutPrivateStoreInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreInfoMsg info)
    {
        var payload = NetCodec.EncodePrivateStoreInfo(new PrivateStoreInfoNetMsg
        {
            ObjectId = info.ObjectId,
            StoreType = info.StoreType,
            Message = info.Message ?? ""
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PrivateStoreInfo, payload);
    }

    void IWorldEventPublisher.FanoutPrivateStoreListSell(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreListSellMsg list)
    {
        var payload = NetCodec.EncodePrivateStoreListSell(ToNetPrivateStoreListSell(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PrivateStoreListSell, payload);
    }

    void IWorldEventPublisher.FanoutPrivateStoreListBuy(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PrivateStoreListBuyMsg list)
    {
        var payload = NetCodec.EncodePrivateStoreListBuy(ToNetPrivateStoreListBuy(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PrivateStoreListBuy, payload);
    }

    static PrivateStoreListSellNetMsg ToNetPrivateStoreListSell(Fineage.Protocol.PrivateStoreListSellMsg list)
    {
        var src = list.Items ?? [];
        var items = new PrivateStoreSellRowNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new PrivateStoreSellRowNet
            {
                InventoryObjectId = src[i].InventoryObjectId,
                ItemId = src[i].ItemId,
                Count = src[i].Count,
                Price = src[i].Price,
                EnchantLevel = src[i].EnchantLevel
            };
        }

        return new PrivateStoreListSellNetMsg
        {
            StoreObjectId = list.StoreObjectId,
            StoreName = list.StoreName ?? "",
            Message = list.Message ?? "",
            Packaged = list.Packaged,
            BuyerAdena = list.BuyerAdena,
            Items = items
        };
    }

    static PrivateStoreListBuyNetMsg ToNetPrivateStoreListBuy(Fineage.Protocol.PrivateStoreListBuyMsg list)
    {
        var src = list.Items ?? [];
        var items = new PrivateStoreBuyRowNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new PrivateStoreBuyRowNet
            {
                ItemId = src[i].ItemId,
                Count = src[i].Count,
                Price = src[i].Price
            };
        }

        return new PrivateStoreListBuyNetMsg
        {
            StoreObjectId = list.StoreObjectId,
            StoreName = list.StoreName ?? "",
            Message = list.Message ?? "",
            StoreAdena = list.StoreAdena,
            Items = items
        };
    }
}
