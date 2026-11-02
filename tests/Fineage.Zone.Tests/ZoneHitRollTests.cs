using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius calcHitMiss / calcCrit / random damage spread.</summary>
public sealed class ZoneHitRollTests
{
    /// <summary>Returns queued values for Next(max) / Next(min, max) in call order.</summary>
    sealed class Scripted(params int[] values) : Random
    {
        int _i;
        public override int Next(int maxValue) => values[_i++];
        public override int Next(int minValue, int maxValue) => values[_i++];
    }

    [Fact]
    public void Hit_chance_matches_mobius_and_clamps()
    {
        Assert.Equal(800, ZoneCombat.HitChancePermille(10, 10));
        Assert.Equal(900, ZoneCombat.HitChancePermille(15, 10));
        Assert.Equal(980, ZoneCombat.HitChancePermille(100, 0));
        Assert.Equal(200, ZoneCombat.HitChancePermille(0, 100));
        Assert.Equal(33.86, ZoneCombat.AccuracyOrEvasion(0, 30, 1), 2); // sqrt(30)*6 + 1
    }

    [Fact]
    public void Miss_returns_zero_damage_and_miss_flag()
    {
        var (dmg, flags) = ZoneCombat.RollMelee(new Scripted(900), 29, 44.444, 10, 10, 44, 10);
        Assert.Equal(0, dmg);
        Assert.Equal(ZoneCombat.HitFlagMiss, flags);
    }

    [Fact]
    public void Crit_doubles_and_sets_flag_random_spreads()
    {
        // hit (0 < 800), crit (44 > 10), spread +10%
        var (crit, cf) = ZoneCombat.RollMelee(new Scripted(0, 10, 10), 29, 44.444, 10, 10, 44, 10);
        Assert.Equal(ZoneCombat.HitFlagCrit, cf);
        Assert.Equal((int)(70d * 29 / 44.444 * 2 * 1.10), crit);
        // hit, no crit (44 > 999 false), spread −10%
        var (plain, pf) = ZoneCombat.RollMelee(new Scripted(0, 999, -10), 29, 44.444, 10, 10, 44, 10);
        Assert.Equal(0, pf);
        Assert.Equal((int)(70d * 29 / 44.444 * 0.90), plain);
    }

    [Fact]
    public void Crit_rate_is_capped_at_500_permille()
    {
        var (_, flags) = ZoneCombat.RollMelee(new Scripted(0, 600, 0), 10, 10, 10, 10, 2000, 0);
        Assert.Equal(0, flags);
    }
}
