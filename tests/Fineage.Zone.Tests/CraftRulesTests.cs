using Fineage.Protocol;
using Fineage.Zone.Craft;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class CraftRulesTests
{
    [Fact]
    public void Make_self_rejects_dead_busy_unknown_unlearned_skill_and_mp()
    {
        Assert.Equal(InventoryProtocol.Dead, CraftRules.MakeSelfError(true, false, false, true, true, true, 10, 5));
        Assert.Equal(CraftProtocol.Busy, CraftRules.MakeSelfError(false, true, false, true, true, true, 10, 5));
        Assert.Equal(CraftProtocol.Busy, CraftRules.MakeSelfError(false, false, true, true, true, true, 10, 5));
        Assert.Equal(CraftProtocol.Unknown, CraftRules.MakeSelfError(false, false, false, false, false, false, 10, 5));
        Assert.Equal(CraftProtocol.NotLearned, CraftRules.MakeSelfError(false, false, false, true, false, true, 10, 5));
        Assert.Equal(CraftProtocol.Skill, CraftRules.MakeSelfError(false, false, false, true, true, false, 10, 5));
        Assert.Equal(CraftProtocol.Mp, CraftRules.MakeSelfError(false, false, false, true, true, true, 4, 5));
        Assert.Null(CraftRules.MakeSelfError(false, false, false, true, true, true, 5, 5));
    }

    [Fact]
    public void Learn_scroll_rejects_unresolved_and_already_known()
    {
        Assert.Equal(InventoryProtocol.Blocked, CraftRules.LearnScrollError(false, false));
        Assert.Equal(CraftProtocol.Already, CraftRules.LearnScrollError(true, true));
        Assert.Null(CraftRules.LearnScrollError(true, false));
    }

    [Fact]
    public void Succeeds_uses_rate_and_roll()
    {
        Assert.True(CraftRules.Succeeds(100, 99));
        Assert.True(CraftRules.Succeeds(50, 49));
        Assert.False(CraftRules.Succeeds(50, 50));
    }
}
