using Fineage.Zone.Geo;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Port of Mobius GeoEngine.canSeeTarget (used by mob aggro / thinkAttack).</summary>
public sealed class GeoLosTests
{
    static GeoEngine WithPillar(short pillarHeight)
    {
        var region = GeoEngine.Region.Flat1(96);
        // Block (1,0) = cells x 8..15; column x=12 raised (closed sides) like a wall.
        region.SetComplexBlock(1, 0, (cx, cy) =>
            cx == 4 ? (pillarHeight, (byte)0) : ((short)96, GeoEngine.All));
        var geo = new GeoEngine();
        geo.SetRegion(20, 18, region);
        return geo;
    }

    [Fact]
    public void Open_flat_ground_is_visible()
    {
        var geo = WithPillar(96);
        Assert.True(geo.CanSeeTarget(8, 40, 96, 400, 40, 96));
        Assert.True(geo.CanSeeTarget(8, 8, 96, 8, 900, 96));
    }

    [Fact]
    public void Tall_wall_blocks_both_ways()
    {
        var geo = WithPillar(400);
        Assert.False(geo.CanSeeTarget(8, 40, 96, 400, 40, 96));
        Assert.False(geo.CanSeeTarget(400, 40, 96, 8, 40, 96));
    }

    [Fact]
    public void Low_ledge_under_48_is_seen_over()
    {
        var geo = WithPillar(130);
        Assert.True(geo.CanSeeTarget(8, 40, 96, 400, 40, 96));
    }

    [Fact]
    public void Line_beside_the_wall_is_clear_and_no_geo_is_see_through()
    {
        var geo = WithPillar(400);
        // Wall column only spans block rows y 0..7 (world y < 128); a line at y=600 passes beside it.
        Assert.True(geo.CanSeeTarget(8, 600, 96, 400, 600, 96));
        Assert.True(new GeoEngine().CanSeeTarget(0, 0, 0, 5000, 5000, 0));
    }
}
