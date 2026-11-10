namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

/// <summary>One sell-store listing row (Mobius TradeList sell item).</summary>
public sealed class PrivateStoreSellRowMsg
{
    public int InventoryObjectId { get; set; }
    public long Count { get; set; } = 1;
    public long Price { get; set; }
}

/// <summary>Open or refresh a private sell store (Mobius SetPrivateStoreListSell).</summary>
public sealed class SetPrivateStoreSellMsg
{
    public string Message { get; set; } = "";
    /// <summary>Mobius package sale: buyer must take the whole list.</summary>
    public bool Packaged { get; set; }
    public PrivateStoreSellRowMsg[] Items { get; set; } = [];
}

/// <summary>Close the active private store (Mobius RequestPrivateStoreQuitSell).</summary>
public sealed class PrivateStoreQuitMsg
{
}

/// <summary>Ask for a player's sell list (Mobius action on a store pawn → PrivateStoreListSell).</summary>
public sealed class PrivateStoreListRequestMsg
{
    public int StoreObjectId { get; set; }
}

/// <summary>Buy from a sell store (Mobius RequestPrivateStoreBuy).</summary>
public sealed class PrivateStoreBuyMsg
{
    public int StoreObjectId { get; set; }
    public PrivateStoreSellRowMsg[] Items { get; set; } = [];
}

/// <summary>One buy-store wish row (Mobius TradeList buy item — template id, not bag object).</summary>
public sealed class PrivateStoreBuyRowMsg
{
    public int ItemId { get; set; }
    public long Count { get; set; } = 1;
    public long Price { get; set; }
}

/// <summary>Open or refresh a private buy store (Mobius SetPrivateStoreListBuy).</summary>
public sealed class SetPrivateStoreBuyMsg
{
    public string Message { get; set; } = "";
    public PrivateStoreBuyRowMsg[] Items { get; set; } = [];
}

/// <summary>Sell items into a buy store (Mobius RequestPrivateStoreSell).</summary>
public sealed class PrivateStoreSellToMsg
{
    public int StoreObjectId { get; set; }
    public PrivateStoreSellRowMsg[] Items { get; set; } = [];
}

/// <summary>Zone → client: store type + title on the pawn (Mobius PrivateStoreMsg).</summary>
public sealed class PrivateStoreInfoMsg
{
    public int ObjectId { get; set; }
    public int StoreType { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>One sell-list row with template metadata for the buyer UI.</summary>
public sealed class PrivateStoreListedSellRowMsg
{
    public int InventoryObjectId { get; set; }
    public int ItemId { get; set; }
    public long Count { get; set; } = 1;
    public long Price { get; set; }
    public int EnchantLevel { get; set; }
}

/// <summary>Zone → client: a sell store's open list for a visitor.</summary>
public sealed class PrivateStoreListSellMsg
{
    public int StoreObjectId { get; set; }
    public string StoreName { get; set; } = "";
    public string Message { get; set; } = "";
    public int Packaged { get; set; }
    public long BuyerAdena { get; set; }
    public PrivateStoreListedSellRowMsg[] Items { get; set; } = [];
}

/// <summary>Zone → client: a buy store's open wish list for a visitor.</summary>
public sealed class PrivateStoreListBuyMsg
{
    public int StoreObjectId { get; set; }
    public string StoreName { get; set; } = "";
    public string Message { get; set; } = "";
    public long StoreAdena { get; set; }
    public PrivateStoreBuyRowMsg[] Items { get; set; } = [];
}
