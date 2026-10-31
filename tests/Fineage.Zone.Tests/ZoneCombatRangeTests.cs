using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneCombatRangeTests
{
    [Fact]
    public void Player_melee_range_matches_interlude_not_900()
    {
        Assert.True(ZoneCombat.InMeleeRange(0, 0, ZoneCombat.MeleeRange, 0));
        Assert.False(ZoneCombat.InMeleeRange(0, 0, 200, 0));
        Assert.False(ZoneCombat.InMeleeRange(0, 0, 900, 0));
        Assert.Equal(ZoneCombat.MobMeleeRange, ZoneCombat.MeleeRange);
    }

    [Fact]
    public void Approach_stop_point_is_inside_melee_range()
    {
        Assert.True(ZoneCombat.MeleeRange - ZoneCombat.ApproachSlack > 0);
        Assert.True(ZoneCombat.InMeleeRange(0, 0, ZoneCombat.MeleeRange - ZoneCombat.ApproachSlack, 0));
    }

    [Fact]
    public void Pickup_keeps_wide_reach()
    {
        Assert.True(ZoneCombat.InPickupRange(0, 0, 800, 0));
    }
}
