using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutBuyList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BuyListMsg list)
    {
        var payload = NetCodec.EncodeBuyList(ToNetBuyList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.BuyList, payload);
    }

    void IWorldEventPublisher.FanoutSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SellListMsg list)
    {
        var payload = NetCodec.EncodeSellList(ToNetSellList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SellList, payload);
    }

    void IWorldEventPublisher.FanoutMultiSellList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MultiSellListMsg list)
    {
        var payload = NetCodec.EncodeMultiSellList(ToNetMultiSellList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.MultiSellList, payload);
    }

    void IWorldEventPublisher.FanoutShopBalance(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShopBalanceMsg balance)
    {
        var payload = NetCodec.EncodeShopBalance(new ShopBalanceNetMsg
        {
            Adena = balance.Adena,
            Weight = balance.Weight,
            MaxWeight = balance.MaxWeight,
            Slots = balance.Slots,
            MaxSlots = balance.MaxSlots,
            BaseTaxPercent = balance.BaseTaxPercent,
            CastleTaxPercent = balance.CastleTaxPercent
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ShopBalance, payload);
    }

    void IWorldEventPublisher.FanoutWarehouseDepositList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WarehouseDepositListMsg list)
    {
        var payload = NetCodec.EncodeWarehouseDepositList(ToNetWarehouseDepositList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.WarehouseDepositList, payload);
    }

    void IWorldEventPublisher.FanoutWarehouseWithdrawList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.WarehouseWithdrawListMsg list)
    {
        var payload = NetCodec.EncodeWarehouseWithdrawList(ToNetWarehouseWithdrawList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.WarehouseWithdrawList, payload);
    }

    static BuyListNetMsg ToNetBuyList(Fineage.Protocol.BuyListMsg buy)
    {
        var src = buy.Items ?? [];
        var items = new BuyListItemNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new BuyListItemNet
            {
                ItemId = src[i].ItemId,
                Price = src[i].Price,
                Count = src[i].Count
            };
        }

        return new BuyListNetMsg
        {
            NpcObjectId = buy.NpcObjectId,
            ListId = buy.ListId,
            BaseTaxPercent = buy.BaseTaxPercent,
            CastleTaxPercent = buy.CastleTaxPercent,
            Items = items
        };
    }

    static SellListNetMsg ToNetSellList(Fineage.Protocol.SellListMsg sell)
    {
        var src = sell.Items ?? [];
        var items = new SellListItemNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            items[i] = new SellListItemNet
            {
                ObjectId = src[i].ObjectId,
                ItemId = src[i].ItemId,
                Count = src[i].Count,
                Price = src[i].Price,
                EnchantLevel = src[i].EnchantLevel
            };
        }

        return new SellListNetMsg { NpcObjectId = sell.NpcObjectId, Items = items };
    }

    static MultiSellListNetMsg ToNetMultiSellList(Fineage.Protocol.MultiSellListMsg multi)
    {
        var src = multi.Entries ?? [];
        var entries = new MultiSellEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            entries[i] = new MultiSellEntryNet
            {
                EntryId = src[i].EntryId,
                Ingredients = ToNetMultiSellParts(src[i].Ingredients),
                Products = ToNetMultiSellParts(src[i].Products)
            };
        }

        return new MultiSellListNetMsg
        {
            NpcObjectId = multi.NpcObjectId,
            ListId = multi.ListId,
            MaintainEnchantment = multi.MaintainEnchantment,
            InventoryOnly = multi.InventoryOnly,
            CastleTaxPercent = multi.CastleTaxPercent,
            Entries = entries
        };
    }

    static MultiSellPartNet[] ToNetMultiSellParts(Fineage.Protocol.MultiSellPartMsg[]? parts)
    {
        parts ??= [];
        var rows = new MultiSellPartNet[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            rows[i] = new MultiSellPartNet
            {
                ItemId = parts[i].ItemId,
                Count = parts[i].Count,
                EnchantLevel = parts[i].EnchantLevel
            };
        }

        return rows;
    }

    static WarehouseDepositListNetMsg ToNetWarehouseDepositList(Fineage.Protocol.WarehouseDepositListMsg list) => new()
    {
        NpcObjectId = list.NpcObjectId,
        FeePerLine = list.FeePerLine,
        SlotsUsed = list.SlotsUsed,
        SlotsMax = list.SlotsMax,
        Adena = list.Adena,
        Items = ToNetWarehouseItemRows(list.Items)
    };

    static WarehouseWithdrawListNetMsg ToNetWarehouseWithdrawList(Fineage.Protocol.WarehouseWithdrawListMsg list) => new()
    {
        NpcObjectId = list.NpcObjectId,
        SlotsUsed = list.SlotsUsed,
        SlotsMax = list.SlotsMax,
        InventorySlotsUsed = list.InventorySlotsUsed,
        InventorySlotsMax = list.InventorySlotsMax,
        CurrentWeight = list.CurrentWeight,
        MaxWeight = list.MaxWeight,
        Items = ToNetWarehouseItemRows(list.Items)
    };

    static ItemListEntryNet[] ToNetWarehouseItemRows(Fineage.Protocol.ItemListEntryMsg[]? items)
    {
        items ??= [];
        var rows = new ItemListEntryNet[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            rows[i] = new ItemListEntryNet
            {
                ObjectId = items[i].ObjectId,
                ItemId = items[i].ItemId,
                Count = items[i].Count,
                EnchantLevel = items[i].EnchantLevel,
                EquippedSlot = items[i].EquippedSlot
            };
        }

        return rows;
    }
}
