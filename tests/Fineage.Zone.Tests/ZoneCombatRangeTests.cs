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

    /// <summary>
    /// Mobius <c>PlayerAI.thinkPickUp</c> → <c>maybeMoveToPawn(target, 36)</c>: a dropped item is not a Creature,
    /// so the gate is 36 + the player's collision radius (9) = 45. Farther loot must be walked to first.
    /// Dropping keeps its own, wider <c>RequestDropItem</c> radius of 150.
    /// </summary>
    [Fact]
    public void Pickup_needs_the_pawn_on_top_of_the_item_but_drop_keeps_150()
    {
        Assert.Equal(ZoneCombat.PickupReach + 9, ZoneCombat.PickupRange);
        Assert.True(ZoneCombat.InPickupRange(0, 0, ZoneCombat.PickupRange, 0));
        Assert.False(ZoneCombat.InPickupRange(0, 0, 60, 0));
        Assert.False(ZoneCombat.InPickupRange(0, 0, 150, 0));
        Assert.False(ZoneCombat.InPickupRange(0, 0, 800, 0));
        Assert.True(ZoneCombat.InDropRange(0, 0, 0, 100, 0, 30));
        Assert.False(ZoneCombat.InDropRange(0, 0, 0, 200, 0, 0));
        Assert.False(ZoneCombat.InDropRange(0, 0, 0, 10, 0, 60));
    }
}

public sealed class ZoneStickyApproachTests
{
    [Fact]
    public void Range_aoi_keeps_known_object_inside_forget_slack()
    {
        var aoi = new Fineage.Zone.Interest.RangeAoi(3500);
        aoi.Enter(1, 0, 0);
        aoi.Enter(2, 3500 + Fineage.Zone.Interest.RangeAoi.ForgetSlack - 10, 0);
        Assert.DoesNotContain(2, aoi.GetVisible(1));
        Assert.True(aoi.StillKnown(1, 2));
        aoi.Move(2, 3500 + Fineage.Zone.Interest.RangeAoi.ForgetSlack + 10, 0);
        Assert.False(aoi.StillKnown(1, 2));
    }

    [Fact]
    public void Approach_path_sync_detects_points_on_segment()
    {
        Assert.True(ZoneCombat.DistanceToSegment(50, 10, 0, 0, 100, 0) <= ZoneCombat.ApproachPathTolerance);
        Assert.True(ZoneCombat.DistanceToSegment(50, 500, 0, 0, 100, 0) > ZoneCombat.ApproachPathTolerance);
    }
}
