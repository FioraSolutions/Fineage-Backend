using Fineage.Zone.Boats;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class WalkingBoatTests
{
    [Fact]
    public void Walking_datapack_loads()
    {
        var routes = WalkingRouteCatalog.LoadDefault();
        Assert.True(routes.Count >= 10);
        var match = routes.Match(31356, -81920, 243892, -3714);
        Assert.NotNull(match);
        Assert.Equal("porter_remy", match!.Name);
    }

    [Fact]
    public void Boat_datapack_loads()
    {
        var boats = BoatCatalog.LoadDefault();
        Assert.Equal(5, boats.Count);
        Assert.NotNull(boats.Get(1));
    }

}