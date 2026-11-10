using Fineage.Protocol;
using Fineage.Zone.Shops;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ShopSessionRulesTests
{
    [Fact]
    public void Buy_window_rejects_empty_dead_and_mismatch()
    {
        Assert.Equal("bad_request", ShopSessionRules.BuyWindowError(false, false, true, true));
        Assert.Equal(InventoryProtocol.Dead, ShopSessionRules.BuyWindowError(true, true, true, true));
        Assert.Equal("bad_request", ShopSessionRules.BuyWindowError(true, false, false, true));
        Assert.Equal("bad_request", ShopSessionRules.BuyWindowError(true, false, true, false));
        Assert.Null(ShopSessionRules.BuyWindowError(true, false, true, true));
    }

    [Fact]
    public void Sell_and_multi_window_reject_mismatch()
    {
        Assert.Equal("bad_request", ShopSessionRules.SellWindowError(false, true, true));
        Assert.Equal("bad_request", ShopSessionRules.SellWindowError(true, false, true));
        Assert.Equal("bad_request", ShopSessionRules.SellWindowError(true, true, false));
        Assert.Null(ShopSessionRules.SellWindowError(true, true, true));

        Assert.Equal("bad_request", ShopSessionRules.MultiWindowError(false, true, true));
        Assert.Equal("bad_request", ShopSessionRules.MultiWindowError(true, false, true));
        Assert.Equal("bad_request", ShopSessionRules.MultiWindowError(true, true, false));
        Assert.Null(ShopSessionRules.MultiWindowError(true, true, true));
    }
}
