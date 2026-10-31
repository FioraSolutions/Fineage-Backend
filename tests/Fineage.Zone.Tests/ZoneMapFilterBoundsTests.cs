using Fineage.Zone;
using Xunit;

namespace Fineage.Zone.Tests;

[Collection("EnvSerial")]
public sealed class ZoneMapFilterBoundsTests
{
    [Fact]
    public void MultipleBoxes_KeepSpawnInsideAny()
    {
        string? old = Environment.GetEnvironmentVariable("FINEAGE_ZONE_BOUNDS");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_BOUNDS", "-135000,200000,-40000,275000; 20000,20000,80000,80000");
            var boxes = ZoneMapFilter.BoundsList;
            Assert.Equal(2, boxes.Count);
            Assert.True(ZoneMapFilter.Contains(boxes, -84000, 244000)); // Talking Island
            Assert.True(ZoneMapFilter.Contains(boxes, 44947, 50556));   // Elven Village
            Assert.False(ZoneMapFilter.Contains(boxes, 83000, 148000)); // Giran
            Assert.Equal(boxes[0], ZoneMapFilter.Bounds);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_BOUNDS", old);
        }
    }

    [Fact]
    public void NoBounds_KeepsEverything()
    {
        Assert.True(ZoneMapFilter.Contains([], 1, 2));
    }
}
