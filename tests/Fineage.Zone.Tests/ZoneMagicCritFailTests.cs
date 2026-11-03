using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Interlude calcMCrit / calcMagicSuccess / calcMagicDam failure branch.</summary>
public sealed class ZoneMagicCritFailTests
{
    sealed class Seq(params int[] values) : Random
    {
        int _i;
        public override int Next(int maxValue) => values[_i++ % values.Length] % maxValue;
    }

    static readonly double Base = 91d * Math.Sqrt(100) / 50 * 20;

    [Fact]
    public void Mcrit_rate_is_floor_wit_bonus_times_ten_and_unarmed_is_ten()
    {
        Assert.Equal(10, ZoneCombat.PlayerMCritRate(false, 1.39));
        Assert.Equal(10, ZoneCombat.PlayerMCritRate(true, 1.39));
        Assert.Equal(20, ZoneCombat.PlayerMCritRate(true, 2.05));
    }

    [Fact]
    public void Magic_success_rate_uses_1_3_pow_level_gap()
    {
        // same level: 100 - 1 = 99 → roll 98 succeeds, 99 fails
        Assert.True(ZoneCombat.RollMagicSuccess(new Seq(98), 20, 20));
        Assert.False(ZoneCombat.RollMagicSuccess(new Seq(99), 20, 20));
        // +5 levels: round(1.3^5 = 3.71) = 4 → rate 96
        Assert.True(ZoneCombat.RollMagicSuccess(new Seq(95), 25, 20));
        Assert.False(ZoneCombat.RollMagicSuccess(new Seq(96), 25, 20));
    }

    [Fact]
    public void Normal_crit_half_and_resist_outcomes()
    {
        // rolls: mcrit(1000), success(100)[, second success(100)]
        var n = ZoneCombat.RollMagicDamage(new Seq(999, 0), 100, 50, 20, 20, 20, 20, 10);
        Assert.Equal((int)Base, n.Damage); Assert.Equal(ZoneCombat.MagicOutcome.Normal, n.Outcome);

        var c = ZoneCombat.RollMagicDamage(new Seq(5, 0), 100, 50, 20, 20, 20, 20, 10);
        Assert.Equal((int)(Base * 3), c.Damage); Assert.Equal(ZoneCombat.MagicOutcome.Critical, c.Outcome);

        var h = ZoneCombat.RollMagicDamage(new Seq(5, 99, 0), 100, 50, 20, 20, 20, 20, 10);
        Assert.Equal((int)(Base / 2), h.Damage); Assert.Equal(ZoneCombat.MagicOutcome.HalfFail, h.Outcome);

        var r = ZoneCombat.RollMagicDamage(new Seq(999, 99, 99), 100, 50, 20, 20, 20, 20, 10);
        Assert.Equal(1, r.Damage); Assert.Equal(ZoneCombat.MagicOutcome.Resisted, r.Outcome);
    }

    [Fact]
    public void Second_roll_success_still_resists_when_target_ten_levels_above()
    {
        var r = ZoneCombat.RollMagicDamage(new Seq(999, 99, 0), 100, 50, 20, 20, 20, 30, 10);
        Assert.Equal(ZoneCombat.MagicOutcome.Resisted, r.Outcome);
    }
}
