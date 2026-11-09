using Fineage.Zone.Inventory;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class WarehouseSessionRulesTests
{
    [Fact]
    public void Window_and_line_gates()
    {
        Assert.Equal("bad_request", WarehouseSessionRules.NpcWindowError(false));
        Assert.Null(WarehouseSessionRules.NpcWindowError(true));
        Assert.Equal("bad_request", WarehouseSessionRules.LineDuplicateError(true));
        Assert.Null(WarehouseSessionRules.LineDuplicateError(false));
        Assert.Equal("bad_request", WarehouseSessionRules.NonStackableCountError(false, 2));
        Assert.Null(WarehouseSessionRules.NonStackableCountError(false, 1));
        Assert.Null(WarehouseSessionRules.NonStackableCountError(true, 5));
    }
}
