using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ConsumableRulesTests
{
    [Fact]
    public void Use_precheck_rejects_unknown_and_reuse()
    {
        Assert.Equal(InventoryProtocol.NotEquippable, ConsumableRules.UsePrecheckError(false, false));
        Assert.Equal(ConsumableRules.InventoryReuse, ConsumableRules.UsePrecheckError(true, true));
        Assert.Null(ConsumableRules.UsePrecheckError(true, false));
    }

    [Fact]
    public void Escape_town_rejects_dead_and_busy()
    {
        Assert.Equal(InventoryProtocol.Dead, ConsumableRules.EscapeTownError(true, false));
        Assert.Equal("busy", ConsumableRules.EscapeTownError(false, true));
        Assert.Null(ConsumableRules.EscapeTownError(false, false));
    }

    [Fact]
    public void Escape_hit_time_blessed_is_near_instant()
    {
        Assert.Equal(200, ConsumableRules.EscapeHitTimeMs(2036));
        Assert.Equal(20_000, ConsumableRules.EscapeHitTimeMs(2013));
        Assert.Equal(20_000, ConsumableRules.EscapeHitTimeMs(0));
    }

    [Fact]
    public void Percent_heal_clamps_to_room()
    {
        Assert.Equal(0, ConsumableRules.PercentHeal(100, 100, 50));
        Assert.Equal(50, ConsumableRules.PercentHeal(50, 100, 50));
        Assert.Equal(0, ConsumableRules.PercentHeal(0, 0, 50));
        Assert.Equal(0, ConsumableRules.PercentHeal(10, 100, 0));
    }
}
