using Fineage.Game.Services;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class SpoilRulesTests
{
    [Fact]
    public void Data_matches_mobius_ct0()
    {
        var rules = SpoilRules.LoadDefault();
        Assert.True(rules.IsSpoilSkill(254));
        Assert.True(rules.IsSweepSkill(42));
        Assert.Equal(10, rules.MagicLevel(254, 1));
        Assert.Equal(72, rules.MagicLevel(254, 11));
        Assert.Equal(72, rules.MagicLevel(254, 30));
        Assert.Equal(7000, rules.CorpseMs(false));
        Assert.Equal(17000, rules.CorpseMs(true));
        Assert.Equal(612, rules.Messages.SpoilActivated);
        Assert.Equal(683, rules.Messages.NoSweepRights);
    }

    [Fact]
    public void Spoil_and_sweep_follow_mobius_order()
    {
        Assert.Equal(SpoilRefusal.InvalidTarget, SpoilRules.Spoil(false, false, () => true));
        Assert.Equal(SpoilRefusal.AlreadySpoiled, SpoilRules.Spoil(true, true, () => true));
        Assert.Equal(SpoilRefusal.Resisted, SpoilRules.Spoil(true, false, () => false));
        Assert.Equal(SpoilRefusal.None, SpoilRules.Spoil(true, false, () => true));

        var rules = SpoilRules.LoadDefault();
        Assert.Equal(SweepRefusal.InvalidTarget, rules.Sweep(false, 1, 1, false, 9000));
        Assert.Equal(SweepRefusal.NotSpoiled, rules.Sweep(true, 0, 1, false, 9000));
        Assert.Equal(SweepRefusal.NoRights, rules.Sweep(true, 2, 1, false, 9000));
        Assert.Equal(SweepRefusal.None, rules.Sweep(true, 2, 1, true, 9000));
        Assert.Equal(SweepRefusal.CorpseTooOld, rules.Sweep(true, 1, 1, false, 1999));
        Assert.Equal(SweepRefusal.None, rules.Sweep(true, 1, 1, false, 2000));
    }
}
