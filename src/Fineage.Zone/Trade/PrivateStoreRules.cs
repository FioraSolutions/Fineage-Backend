using Fineage.Game.Shops;
using Fineage.Protocol;

namespace Fineage.Zone.Trade;

/// <summary>Pure private-store listing checks (row shape, adena caps). World still owns bag ownership.</summary>
public static class PrivateStoreRules
{
    /// <summary>Basic client row shape before bag lookups.</summary>
    public static string? SellRowShapeError(long count, long price, int inventoryObjectId)
    {
        if (count < 1 || price < 0 || inventoryObjectId < 1)
            return PrivateStoreProtocol.Item;
        return null;
    }

    /// <summary>Basic buy-wish row shape before catalog lookups.</summary>
    public static string? BuyRowShapeError(long count, long price, int itemId)
    {
        if (count < 1 || price < 0 || itemId < 1)
            return PrivateStoreProtocol.Item;
        return null;
    }

    /// <summary>Refuse non-stackable multi-count or adena-as-merchandise.</summary>
    public static string? SellStockError(bool found, bool equipped, bool canDropTradeStore, bool stackable, long count, long available, int itemId)
    {
        if (!found || equipped || !canDropTradeStore || available < count)
            return PrivateStoreProtocol.Item;
        if (!stackable && count != 1)
            return PrivateStoreProtocol.Item;
        if (itemId == ShopTrade.AdenaItemId)
            return PrivateStoreProtocol.Item;
        return null;
    }

    /// <summary>Line price and running total vs <see cref="ShopTrade.MaxAdena"/>.</summary>
    public static string? AdenaLineError(long price, long count, long runningTotal)
    {
        long line = checked(price * count);
        if (line > ShopTrade.MaxAdena)
            return PrivateStoreProtocol.Adena;
        long next = checked(runningTotal + line);
        if (next > ShopTrade.MaxAdena)
            return PrivateStoreProtocol.Adena;
        return null;
    }
}
