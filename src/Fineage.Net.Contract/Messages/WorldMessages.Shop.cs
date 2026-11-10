namespace Fineage.Net.Contract
{
    public sealed class ShopCountNet
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    public sealed class ShopObjectCountNet
    {
        public int ObjectId { get; set; }
        public int Count { get; set; }
    }

    public sealed class RequestBuyItemNetMsg
    {
        public int NpcObjectId { get; set; }
        public int ListId { get; set; }
        public ShopCountNet[] Items { get; set; } = System.Array.Empty<ShopCountNet>();
    }

    public sealed class RequestSellItemNetMsg
    {
        public int NpcObjectId { get; set; }
        public ShopObjectCountNet[] Items { get; set; } = System.Array.Empty<ShopObjectCountNet>();
    }

    public sealed class RequestMultiSellChooseNetMsg
    {
        public int NpcObjectId { get; set; }
        public int ListId { get; set; }
        public int EntryId { get; set; }
        public int Amount { get; set; }
    }

    public sealed class BuyListItemNet
    {
        public int ItemId { get; set; }
        public int Price { get; set; }
        public int Count { get; set; } = -1;
    }

    public sealed class BuyListNetMsg
    {
        public int NpcObjectId { get; set; }
        public int ListId { get; set; }
        public int BaseTaxPercent { get; set; }
        public int CastleTaxPercent { get; set; }
        public BuyListItemNet[] Items { get; set; } = System.Array.Empty<BuyListItemNet>();
    }

    public sealed class SellListItemNet
    {
        public int ObjectId { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int Price { get; set; }
        public int EnchantLevel { get; set; }
    }

    public sealed class SellListNetMsg
    {
        public int NpcObjectId { get; set; }
        public SellListItemNet[] Items { get; set; } = System.Array.Empty<SellListItemNet>();
    }

    public sealed class MultiSellPartNet
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EnchantLevel { get; set; }
    }

    public sealed class MultiSellEntryNet
    {
        public int EntryId { get; set; }
        public MultiSellPartNet[] Ingredients { get; set; } = System.Array.Empty<MultiSellPartNet>();
        public MultiSellPartNet[] Products { get; set; } = System.Array.Empty<MultiSellPartNet>();
    }

    public sealed class MultiSellListNetMsg
    {
        public int NpcObjectId { get; set; }
        public int ListId { get; set; }
        public bool MaintainEnchantment { get; set; }
        public bool InventoryOnly { get; set; }
        public int CastleTaxPercent { get; set; }
        public MultiSellEntryNet[] Entries { get; set; } = System.Array.Empty<MultiSellEntryNet>();
    }

    public sealed class ShopBalanceNetMsg
    {
        public int Adena { get; set; }
        public int Weight { get; set; }
        public int MaxWeight { get; set; }
        public int Slots { get; set; }
        public int MaxSlots { get; set; }
        public int BaseTaxPercent { get; set; }
        public int CastleTaxPercent { get; set; }
    }

    public sealed class WarehouseDepositListNetMsg
    {
        public int NpcObjectId { get; set; }
        public int FeePerLine { get; set; }
        public int SlotsUsed { get; set; }
        public int SlotsMax { get; set; }
        public int Adena { get; set; }
        public ItemListEntryNet[] Items { get; set; } = System.Array.Empty<ItemListEntryNet>();
    }

    public sealed class WarehouseWithdrawListNetMsg
    {
        public int NpcObjectId { get; set; }
        public int SlotsUsed { get; set; }
        public int SlotsMax { get; set; }
        public int InventorySlotsUsed { get; set; }
        public int InventorySlotsMax { get; set; }
        public int CurrentWeight { get; set; }
        public int MaxWeight { get; set; }
        public ItemListEntryNet[] Items { get; set; } = System.Array.Empty<ItemListEntryNet>();
    }

    public sealed class WarehouseDepositRequestNetMsg
    {
        public int NpcObjectId { get; set; }
        public ShopObjectCountNet[] Lines { get; set; } = System.Array.Empty<ShopObjectCountNet>();
    }

    public sealed class WarehouseWithdrawRequestNetMsg
    {
        public int NpcObjectId { get; set; }
        public ShopObjectCountNet[] Lines { get; set; } = System.Array.Empty<ShopObjectCountNet>();
    }
}
