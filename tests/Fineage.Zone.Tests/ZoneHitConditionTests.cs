using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius HitConditionBonusData + isBehind/isInFrontOf + calcHitMiss multiplier.</summary>
public sealed class ZoneHitConditionTests
{
    // Target at origin facing +X (heading 0).
    [Fact]
    public void Back_side_front_bonuses()
    {
        Assert.Equal(1.10, ZoneCombat.HitConditionBonus(-100, 0, 0, 0, 0, 0, 0), 6); // attacker behind
        Assert.Equal(1.00, ZoneCombat.HitConditionBonus(100, 0, 0, 0, 0, 0, 0), 6);  // in front
        Assert.Equal(1.05, ZoneCombat.HitConditionBonus(0, 100, 0, 0, 0, 0, 0), 6);  // side
    }

    [Fact]
    public void Height_and_night_bonuses()
    {
        Assert.Equal(1.03, ZoneCombat.HitConditionBonus(100, 0, 51, 0, 0, 0, 0), 6);
        Assert.Equal(0.97, ZoneCombat.HitConditionBonus(100, 0, -51, 0, 0, 0, 0), 6);
        Assert.Equal(1.00, ZoneCombat.HitConditionBonus(100, 0, 50, 0, 0, 0, 0), 6);
        Assert.Equal(0.90, ZoneCombat.HitConditionBonus(100, 0, 0, 0, 0, 0, 0, night: true), 6);
    }

    [Fact]
    public void Heading_wraps_and_behind_uses_60_degree_cone()
    {
        int h = ZoneCombat.HeadingFrom(0, 0, 0, -100); // facing -Y → 270°
        Assert.Equal((int)(270 * 182.044444444), h);
        Assert.True(ZoneCombat.IsBehind(0, 100, 0, 0, h));
        Assert.True(ZoneCombat.IsBehind(50, 100, 0, 0, h));   // ~26° off
        Assert.False(ZoneCombat.IsBehind(100, 10, 0, 0, h));  // ~84° off → side
        // heading ~359° vs attacker angle ~1° wraps
        Assert.True(ZoneCombat.IsBehind(-100, -2, 0, 0, (int)(359 * 182.044444444)));
    }

    [Fact]
    public void Bonus_multiplies_chance_before_clamp()
    {
        Assert.Equal(800, ZoneCombat.HitChancePermille(10, 10));
        Assert.Equal(880, ZoneCombat.HitChancePermille(10, 10, 1.10));
        Assert.Equal(980, ZoneCombat.HitChancePermille(20, 10, 1.10)); // 1000·1.1 → clamp
        Assert.Equal(200, ZoneCombat.HitChancePermille(-40, 10, 0.9));
    }
}
