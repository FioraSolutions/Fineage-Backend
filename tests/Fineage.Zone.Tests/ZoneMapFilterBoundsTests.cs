using Fineage.Zone;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneMapFilterBoundsTests
{
    [Fact]
    public void MultipleBoxes_KeepSpawnInsideAny()
    {
        var boxes = ZoneMapFilter.ParseBounds("-135000,200000,-40000,275000; 20000,20000,80000,80000");
        Assert.Equal(2, boxes.Count);
        Assert.True(ZoneMapFilter.Contains(boxes, -84000, 244000)); // Talking Island
        Assert.True(ZoneMapFilter.Contains(boxes, 44947, 50556));   // Elven Village
        Assert.False(ZoneMapFilter.Contains(boxes, 83000, 148000)); // Giran
    }

    [Fact]
    public void SingleBox_LegacyFormat()
    {
        var boxes = ZoneMapFilter.ParseBounds("-135000,200000,-40000,275000");
        Assert.Single(boxes);
        Assert.False(ZoneMapFilter.Contains(boxes, 44947, 50556));
    }

    [Fact]
    public void NoBounds_KeepsEverything()
    {
        Assert.Empty(ZoneMapFilter.ParseBounds(null));
        Assert.True(ZoneMapFilter.Contains([], 1, 2));
    }
}
