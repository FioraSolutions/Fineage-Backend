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

public sealed class DropItemCoverageTests
{
    [Fact]
    public void Every_droplist_item_has_a_template_so_rolls_spawn()
    {
        var items = Fineage.Game.GameData.LoadDefault().Items;
        var missing = DropCatalog.LoadDefault().ByNpcId.Values
            .SelectMany(d => d.Death.Groups.SelectMany(g => g.Items).Concat(d.Corpse?.Items ?? []))
            .Select(i => i.ItemId).Distinct().Where(id => !items.TryGet(id, out _)).ToList();
        Assert.Empty(missing);
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
