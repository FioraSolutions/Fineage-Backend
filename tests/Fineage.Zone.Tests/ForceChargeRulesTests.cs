using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ForceChargeRulesTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void MaxCharges_table_resolves_to_skill_level(int level, int expected)
    {
        var row = new SkillEffectCatalog.Effect("FocusEnergy", "", 100, 1) { Extra = new Dictionary<string, string> { ["charge"] = "#maxCharges" } };
        Assert.True(ForceChargeRules.TryResolveCap(row, 8, level, out int cap));
        Assert.Equal(expected, cap);
    }

    [Fact]
    public void Literal_charge_param_is_the_cap()
    {
        var row = new SkillEffectCatalog.Effect("FocusEnergy", "", 100, 1) { Extra = new Dictionary<string, string> { ["charge"] = "7" } };
        Assert.True(ForceChargeRules.TryResolveCap(row, 345, 1, out int cap));
        Assert.Equal(7, cap);
    }

    [Fact]
    public void Decay_interval_matches_mobius_reset_charges_task()
    {
        Assert.Equal(600_000, ForceChargeRules.DecayMs);
    }

    [Fact]
    public void Missing_charge_param_fails()
    {
        var row = new SkillEffectCatalog.Effect("FocusEnergy", "", 100, 1);
        Assert.False(ForceChargeRules.TryResolveCap(row, 8, 1, out _));
    }
}