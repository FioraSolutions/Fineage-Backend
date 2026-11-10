using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Craft;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ManufactureRulesTests
{
    [Fact]
    public void Open_row_and_list_limit()
    {
        Assert.Equal(PrivateStoreProtocol.Item, ManufactureRules.OpenRowError(0, 1, false, true, true));
        Assert.Equal(PrivateStoreProtocol.Item, ManufactureRules.OpenRowError(1, -1, false, true, true));
        Assert.Equal(PrivateStoreProtocol.Item, ManufactureRules.OpenRowError(1, 1, true, true, true));
        Assert.Equal(CraftProtocol.Unknown, ManufactureRules.OpenRowError(1, 1, false, false, false));
        Assert.Equal(CraftProtocol.NotLearned, ManufactureRules.OpenRowError(1, 1, false, true, false));
        Assert.Equal(
            PrivateStoreProtocol.Adena,
            ManufactureRules.OpenRowError(1, ShopTrade.MaxAdena + 1, false, true, true));
        Assert.Null(ManufactureRules.OpenRowError(1, 1, false, true, true));
        Assert.Equal(CraftProtocol.Limit, ManufactureRules.OpenListLimitError(CraftProtocol.ManufactureSlots + 1));
        Assert.Null(ManufactureRules.OpenListLimitError(CraftProtocol.ManufactureSlots));
    }

    [Fact]
    public void Make_info_closed_or_range()
    {
        Assert.Equal(CraftProtocol.Closed, ManufactureRules.MakeInfoError(false, false, false));
        Assert.Equal(CraftProtocol.Closed, ManufactureRules.MakeInfoError(true, false, true));
        Assert.Equal(CraftProtocol.Range, ManufactureRules.MakeInfoError(true, true, false));
        Assert.Null(ManufactureRules.MakeInfoError(true, true, true));
    }

    [Fact]
    public void Make_item_rejects_dead_busy_closed_range_skill_mp_adena()
    {
        Assert.Equal(
            InventoryProtocol.Dead,
            ManufactureRules.MakeItemError(true, false, false, true, false, true, false, true, true, true, true, 10, 5, 0, true, true));
        Assert.Equal(
            CraftProtocol.Busy,
            ManufactureRules.MakeItemError(false, true, false, true, false, true, false, true, true, true, true, 10, 5, 0, true, true));
        Assert.Equal(
            "bad_request",
            ManufactureRules.MakeItemError(false, false, false, true, true, true, false, true, true, true, true, 10, 5, 0, true, true));
        Assert.Equal(
            CraftProtocol.Closed,
            ManufactureRules.MakeItemError(false, false, false, false, false, true, false, true, true, true, true, 10, 5, 0, true, true));
        Assert.Equal(
            CraftProtocol.Range,
            ManufactureRules.MakeItemError(false, false, false, true, false, false, false, true, true, true, true, 10, 5, 0, true, true));
        Assert.Equal(
            CraftProtocol.Adena,
            ManufactureRules.MakeItemError(false, false, false, true, false, true, false, true, true, true, true, 10, 5, 1, false, true));
        Assert.Null(
            ManufactureRules.MakeItemError(false, false, false, true, false, true, false, true, true, true, true, 10, 5, 0, true, true));
    }
}
