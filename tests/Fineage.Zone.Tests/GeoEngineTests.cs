using Fineage.Zone.Geo;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class GeoEngineTests
{
    // Region 20_18 starts at world (0,0); cells are 16 units.
    static GeoEngine FlatWithWall()
    {
        var region = GeoEngine.Region.Flat1(96);
        // Block (1,0) = geo cells x 8..15: a wall on cell column x=12 (no EAST bit on x=11, no WEST bit on x=12).
        region.SetComplexBlock(1, 0, (cx, cy) =>
        {
            byte n = GeoEngine.All;
            if (cx == 3) n = (byte)(n & ~GeoEngine.East);
            if (cx == 4) n = (byte)(n & ~GeoEngine.West);
            return (96, n);
        });
        var geo = new GeoEngine();
        geo.SetRegion(20, 18, region);
        return geo;
    }

    [Fact]
    public void Height_snaps_to_ground_and_missing_region_keeps_z()
    {
        var geo = FlatWithWall();
        Assert.True(geo.HasGeo(40, 40));
        Assert.Equal(96, geo.GetHeight(40, 40, -5000));
        Assert.False(geo.HasGeo(-40, 40));
        Assert.Equal(-5000, geo.GetHeight(-40, 40, -5000));
    }

    [Fact]
    public void Move_through_wall_is_truncated_before_it()
    {
        var geo = FlatWithWall();
        var (x, y, z) = geo.MoveCheck(8, 40, 96, 400, 40, -3000, out bool blocked);
        Assert.True(blocked);
        Assert.Equal(GeoEngine.WorldX(11), x);
        Assert.Equal(40 / 16 * 16 + 8, y);
        Assert.Equal(96, z);
    }

    [Fact]
    public void Open_move_reaches_dest_on_ground_z()
    {
        var geo = FlatWithWall();
        var (x, y, z) = geo.MoveCheck(8, 40, 96, 150, 400, -3000, out bool blocked);
        Assert.False(blocked);
        Assert.Equal((150, 400, 96), (x, y, z));
    }

    [Fact]
    public void Real_talking_island_region_loads_when_present()
    {
        string? dir = FindGeoDir();
        if (dir == null || !File.Exists(Path.Combine(dir, "17_25.l2j")))
            return; // geodata is not committed; optional local check
        var geo = GeoEngine.LoadDirectory(dir);
        Assert.True(geo.HasGeo(TalkingIsland.SpawnX, TalkingIsland.SpawnY));
        int z = geo.GetHeight(TalkingIsland.SpawnX, TalkingIsland.SpawnY, TalkingIsland.SpawnZ);
        Assert.InRange(z, TalkingIsland.SpawnZ - 300, TalkingIsland.SpawnZ + 300);
    }

    static string? FindGeoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            string p = Path.Combine(d.FullName, "data", "geodata");
            if (Directory.Exists(p)) return p;
            d = d.Parent;
        }
        return null;
    }

    [Fact]
    public void Blocked_move_finds_path_around_wall_end()
    {
        var geo = FlatWithWall(); // wall spans geo y 0..7 only
        var path = geo.FindPath(8, 40, 96, 400, 40, 96);
        Assert.NotNull(path);
        Assert.True(path!.Count >= 2);
        Assert.Equal((400, 40, 96), path[^1]);
        int ax = 8, ay = 40, az = 96;
        foreach (var (x, y, z) in path)
        {
            geo.MoveCheck(ax, ay, az, x, y, z, out bool blocked);
            Assert.False(blocked);
            (ax, ay, az) = (x, y, z);
        }
    }

    [Fact]
    public void Open_move_path_is_single_leg()
    {
        var geo = FlatWithWall();
        var path = geo.FindPath(8, 40, 96, 150, 400, 96);
        Assert.Equal([(150, 400, 96)], path!);
    }
}
