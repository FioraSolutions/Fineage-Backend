namespace Fineage.Net.Contract
{
    public sealed class PrivateStoreSellRowNet
    {
        public int InventoryObjectId { get; set; }
        public int ItemId { get; set; }
        public long Count { get; set; } = 1;
        public long Price { get; set; }
        public int EnchantLevel { get; set; }
    }

    public sealed class SetPrivateStoreSellNetMsg
    {
        public string Message { get; set; } = "";
        public int Packaged { get; set; }
        public PrivateStoreSellRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreSellRowNet>();
    }

    public sealed class PrivateStoreListRequestNetMsg
    {
        public int StoreObjectId { get; set; }
    }

    public sealed class PrivateStoreBuyNetMsg
    {
        public int StoreObjectId { get; set; }
        public PrivateStoreSellRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreSellRowNet>();
    }

    /// <summary>Broadcast store type + title (Mobius PrivateStoreMsgSell / UserInfo store bit).</summary>

    public sealed class PrivateStoreInfoNetMsg
    {
        public int ObjectId { get; set; }
        public int StoreType { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class PrivateStoreListSellNetMsg
    {
        public int StoreObjectId { get; set; }
        public string StoreName { get; set; } = "";
        public string Message { get; set; } = "";
        public int Packaged { get; set; }
        public long BuyerAdena { get; set; }
        public PrivateStoreSellRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreSellRowNet>();
    }

    public sealed class PrivateStoreBuyRowNet
    {
        public int ItemId { get; set; }
        public long Count { get; set; } = 1;
        public long Price { get; set; }
    }

    public sealed class SetPrivateStoreBuyNetMsg
    {
        public string Message { get; set; } = "";
        public PrivateStoreBuyRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreBuyRowNet>();
    }

    public sealed class PrivateStoreSellToNetMsg
    {
        public int StoreObjectId { get; set; }
        public PrivateStoreSellRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreSellRowNet>();
    }

    public sealed class PrivateStoreListBuyNetMsg
    {
        public int StoreObjectId { get; set; }
        public string StoreName { get; set; } = "";
        public string Message { get; set; } = "";
        public long StoreAdena { get; set; }
        public PrivateStoreBuyRowNet[] Items { get; set; } = System.Array.Empty<PrivateStoreBuyRowNet>();
    }
}
