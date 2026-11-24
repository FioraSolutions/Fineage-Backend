using Fineage.Zone.Geo;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The resumable route search (#971 step 5) must find the same route as the one-shot search, and the per-step node
/// budget must only spread the work over steps — never change where the mover ends up.
/// </summary>
public sealed class GeoPathSearchTests
{
    // Region 20_18 covers world (0,0)+; the wall sits on geo cell column x=12 (world 192..207).
    static GeoEngine FlatWithWall()
    {
        var region = GeoEngine.Region.Flat1(96);
        region.SetComplexBlock(1, 0, (cx, cy) =>
        {
            byte n = GeoEngine.All;
            if (cx == 3) n = (byte)(n & ~GeoEngine.East);
            if (cx == 4) n = (byte)(n & ~GeoEngine.West);
            return ((short)96, n);
        });
        var geo = new GeoEngine();
        geo.SetRegion(20, 18, region);
        return geo;
    }

    [Fact]
    public void Resumed_search_finds_the_same_route_as_one_shot()
    {
        var geo = FlatWithWall();
        var oneShot = geo.FindPath(8, 40, 96, 150, 400, 96);
        Assert.NotNull(oneShot);

        var search = geo.BeginPath(8, 40, 96, 150, 400, 96);
        int steps = 0;
        while (search.Step(1) == GeoPathSearch.Status.Running)
        {
            steps++;
            Assert.True(steps < 10_000, "a budget of one node per step must still finish");
        }

        Assert.True(steps > 1, "the search must actually be spread over several steps");
        Assert.Equal(oneShot, search.BuildPath());
    }

    [Fact]
    public void Node_budget_is_spent_exactly_as_asked()
    {
        var geo = FlatWithWall();
        var search = geo.BeginPath(8, 40, 96, 400, 40, 96);
        Assert.Equal(GeoPathSearch.Status.Running, search.Step(1));
        Assert.Equal(1, search.Expansions);
        search.Step(3);
        Assert.Equal(4, search.Expansions);
    }

    [Fact]
    public void Search_stops_at_the_node_cap_instead_of_searching_forever()
    {
        var geo = FlatWithWall();
        // The percent-free flat region makes a one-node search trivially succeed; a tiny cap must still end the search.
        var search = geo.BeginPath(8, 40, 96, 150, 40, 96, maxNodes: 1);
        Assert.Equal(GeoPathSearch.Status.Exhausted, search.Step(int.MaxValue));
        Assert.Null(search.BuildPath());
    }

    [Fact]
    public void No_route_reports_NoRoute_and_builds_no_path()
    {
        // A whole block with no open NSWE direction: every neighbour step is illegal, so the open set empties.
        var region = GeoEngine.Region.Flat1(96);
        region.SetComplexBlock(0, 0, (_, _) => ((short)96, (byte)0));
        var walled = new GeoEngine();
        walled.SetRegion(20, 18, region);

        var search = walled.BeginPath(8, 8, 96, 400, 40, 96);
        Assert.Equal(GeoPathSearch.Status.NoRoute, search.Step(int.MaxValue));
        Assert.Null(search.BuildPath());
    }

    [Fact]
    public void Smoothing_probe_budget_spreads_work_and_keeps_resuming()
    {
        var geo = FlatWithWall();
        var search = geo.BeginPath(8, 40, 96, 400, 40, 96);
        Assert.Equal(GeoPathSearch.Status.Found, search.Step(int.MaxValue));

        // A slice smaller than one straight leg still pays one probe per call instead of degrading to A* cells, so the
        // route advances every call and finishes on the exact target.
        var capped = new List<(int X, int Y, int Z)>();
        int calls = 0;
        while (!search.TrySmooth(60, capped))
        {
            calls++;
            Assert.True(calls < 10_000, "a small probe slice must still advance the route every call");
        }

        Assert.True(calls > 0, "the route must actually be spread over several calls");
        Assert.True(search.SmoothingCellsWalked > 0, "each call pays for the legs it collapses");
        Assert.Equal((400, 40, 96), capped[^1]);
        AssertWalkable(geo, capped, startX: 8, startY: 40, startZ: 96);
    }

    [Fact]
    public void Zero_probe_slice_still_advances_and_ends_on_the_target()
    {
        var geo = FlatWithWall();
        var search = geo.BeginPath(8, 40, 96, 400, 40, 96);
        Assert.Equal(GeoPathSearch.Status.Found, search.Step(int.MaxValue));

        // The first probe of a call is always paid: one raycast is a whole leg, and refusing it would turn a village
        // crossing into "no progress". After that the slice falls back to the adjacent A* cell, so the route still
        // finishes and never spins.
        var path = new List<(int X, int Y, int Z)>();
        int calls = 0;
        while (!search.TrySmooth(0, path))
        {
            calls++;
            Assert.True(calls < 10_000, "a zero probe slice must still advance the route every call");
        }

        Assert.Equal((400, 40, 96), path[^1]);
        AssertWalkable(geo, path, startX: 8, startY: 40, startZ: 96);
    }

    static void AssertWalkable(GeoEngine geo, List<(int X, int Y, int Z)> path, int startX, int startY, int startZ)
    {
        int ax = startX, ay = startY, az = startZ;
        foreach (var (x, y, z) in path)
        {
            geo.MoveCheck(ax, ay, az, x, y, z, out bool blocked);
            Assert.False(blocked);
            (ax, ay, az) = (x, y, z);
        }
    }
}
