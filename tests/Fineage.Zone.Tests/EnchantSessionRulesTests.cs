using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class EnchantSessionRulesTests
{
    [Fact]
    public void Request_rejects_unowned_and_non_scroll()
    {
        Assert.Equal(InventoryProtocol.NotOwned, EnchantSessionRules.RequestError(false, false));
        Assert.Equal(EnchantProtocol.NotScroll, EnchantSessionRules.RequestError(true, false));
        Assert.Null(EnchantSessionRules.RequestError(true, true));
    }

    [Fact]
    public void Cancel_and_confirm_session_gates()
    {
        Assert.Equal(EnchantProtocol.NoRequest, EnchantSessionRules.CancelError(false));
        Assert.Null(EnchantSessionRules.CancelError(true));

        Assert.Equal(
            EnchantProtocol.NoRequest,
            EnchantSessionRules.ConfirmError(false, true, true, true));
        Assert.Equal(
            InventoryProtocol.NotOwned,
            EnchantSessionRules.ConfirmError(true, false, false, true));
        Assert.Equal(
            EnchantProtocol.NotScroll,
            EnchantSessionRules.ConfirmError(true, true, false, true));
        Assert.Equal(
            InventoryProtocol.NotOwned,
            EnchantSessionRules.ConfirmError(true, true, true, false));
        Assert.Null(EnchantSessionRules.ConfirmError(true, true, true, true));
    }
}
