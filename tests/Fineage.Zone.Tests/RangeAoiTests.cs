using Fineage.Zone.Interest;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class RangeAoiTests
{
    [Fact]
    public void Drops_entities_outside_radius()
    {
        var aoi = new RangeAoi(100);
        aoi.Enter(1, 0, 0);
        aoi.Enter(2, 50, 0);
        aoi.Enter(3, 200, 0);

        var visible = aoi.GetVisible(1);
        Assert.Contains(2, visible);
        Assert.DoesNotContain(3, visible);
    }
}
