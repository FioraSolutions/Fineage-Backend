using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Trade;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class PrivateStoreRulesTests
{
    [Fact]
    public void Row_shape_and_adena_line_guards()
    {
        Assert.Equal(PrivateStoreProtocol.Item, PrivateStoreRules.SellRowShapeError(0, 1, 1));
        Assert.Null(PrivateStoreRules.SellRowShapeError(1, 0, 2));
        Assert.Equal(PrivateStoreProtocol.Item, PrivateStoreRules.BuyRowShapeError(1, 1, 0));
        Assert.Equal(PrivateStoreProtocol.Adena, PrivateStoreRules.AdenaLineError(ShopTrade.MaxAdena, 2, 0));
        Assert.Null(PrivateStoreRules.AdenaLineError(10, 3, 0));
    }

    [Fact]
    public void Sell_stock_rejects_adena_and_bad_counts()
    {
        Assert.Equal(
            PrivateStoreProtocol.Item,
            PrivateStoreRules.SellStockError(true, false, true, true, 1, 5, ShopTrade.AdenaItemId));
        Assert.Equal(
            PrivateStoreProtocol.Item,
            PrivateStoreRules.SellStockError(true, false, true, false, 2, 2, itemId: 1060));
        Assert.Null(PrivateStoreRules.SellStockError(true, false, true, true, 2, 5, itemId: 1060));
    }
}
