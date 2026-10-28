using Fineage.Zone.Drops;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class DropCatalogTests
{
    [Fact]
    public void Loads_gremlin_droplist_from_datapack()
    {
        var catalog = DropCatalog.LoadDefault();
        Assert.True(catalog.TryGet(TalkingIsland.GremlinNpcId, out var gremlin));
        Assert.Single(gremlin!.Death.Groups);
        Assert.Equal(57, gremlin.Death.Groups[0].Items[0].ItemId);
        Assert.NotNull(gremlin.Corpse);
        Assert.Equal(1864, gremlin.Corpse!.Items[0].ItemId);
    }
}

public sealed class DropRollerTests
{
    [Fact]
    public void Gremlin_death_adena_stays_in_mobius_range()
    {
        var catalog = DropCatalog.LoadDefault();
        catalog.TryGet(TalkingIsland.GremlinNpcId, out var gremlin);
        var stack = DropRoller.RollDeath(gremlin!, new Random(MobiusDropTestSeed.DefaultSeed)).Single();
        Assert.Equal(57, stack.ItemId);
        Assert.InRange(stack.Count, 7, 13);
    }

    [Fact]
    public void Seeded_gremlin_death_is_stable()
    {
        var first = MobiusDropTestSeed.ExpectedGremlinDeathDrop();
        var second = MobiusDropTestSeed.ExpectedGremlinDeathDrop();
        Assert.Equal(first, second);
    }
}
