using Fineage.Zone.Interest;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class GridAoiTests
{
    [Fact]
    public void Same_cell_and_neighbors_are_visible()
    {
        var aoi = new GridAoi(1000);
        aoi.Enter(1, 0, 0);
        aoi.Enter(2, 500, 0);
        aoi.Enter(3, 999, 999);

        var visible = aoi.GetVisible(1);
        Assert.Contains(2, visible);
        Assert.Contains(3, visible);
    }

    [Fact]
    public void Far_cell_is_hidden()
    {
        var aoi = new GridAoi(1000);
        aoi.Enter(1, 0, 0);
        aoi.Enter(2, 3500, 0);

        Assert.DoesNotContain(2, aoi.GetVisible(1));
        Assert.False(aoi.StillKnown(1, 2));
    }

    [Fact]
    public void Move_updates_cell_visibility()
    {
        var aoi = new GridAoi(1000);
        aoi.Enter(1, 0, 0);
        aoi.Enter(2, 500, 0);
        Assert.Contains(2, aoi.GetVisible(1));

        aoi.Move(2, 3500, 0);
        Assert.DoesNotContain(2, aoi.GetVisible(1));
    }

    [Fact]
    public void StillKnown_keeps_one_cell_past_watch()
    {
        var aoi = new GridAoi(1000);
        aoi.Enter(1, 0, 0);
        // Cell (0,0) vs cell (2,0): outside 3×3 watch, inside 5×5 forget.
        aoi.Enter(2, 2500, 0);
        Assert.DoesNotContain(2, aoi.GetVisible(1));
        Assert.True(aoi.StillKnown(1, 2));
    }
}
