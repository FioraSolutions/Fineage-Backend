using Fineage.Zone.Drops;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class DropCatalogTests
{
    [Fact]
    public void Loads_gremlin_droplist_from_datapack()
    {
        var catalog = DropCatalog.LoadDefault();
        Assert.False(catalog.TryGet(TalkingIsland.GremlinNpcId, out _));
        Assert.True(catalog.TryGet(MobiusDropTestSeed.TrainingHallGremlinNpcId, out var gremlin));
        Assert.Single(gremlin!.Death.Groups);
        Assert.Equal(57, gremlin.Death.Groups[0].Items[0].ItemId);
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
        catalog.TryGet(MobiusDropTestSeed.TrainingHallGremlinNpcId, out var gremlin);
        var stack = DropRoller.RollDeath(gremlin!, new Random(MobiusDropTestSeed.DefaultSeed)).Single();
        Assert.Equal(57, stack.ItemId);
        Assert.InRange(stack.Count, 7, 13);
    }

    [Fact]
    public void Level_gap_cuts_adena_once_the_killer_is_far_above_the_mob()
    {
        var definition = Adena(chance: 100);
        int full = 0;
        int gapped = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            if (DropRoller.RollDeath(definition, new Random(seed), victimLevel: 10, killerLevel: 10).Count > 0)
                full++;
            if (DropRoller.RollDeath(definition, new Random(seed), victimLevel: 10, killerLevel: 40).Count > 0)
                gapped++;
        }

        Assert.Equal(200, full);
        Assert.InRange(gapped, 8, 40);
        Assert.Equal(10, DropRoller.LevelGapChance(10, 40, adena: true));
        Assert.Equal(100, DropRoller.LevelGapChance(10, 10, adena: false));
    }

    [Fact]
    public void A_normal_mob_keeps_at_most_two_non_guaranteed_stacks()
    {
        var groups = new List<DropGroupDefinition>();
        for (int i = 0; i < 6; i++)
        {
            // Group chance 50 is under 100, so each success spends one of the two occurrence slots.
            groups.Add(new DropGroupDefinition
            {
                Chance = 50,
                Items = [new DropItemDefinition { ItemId = 1000 + i, Min = 1, Max = 1, Chance = 100 }]
            });
        }

        var definition = new DropNpcDefinition
        {
            NpcId = 2,
            Death = new DropDeathDefinition { Groups = groups }
        };
        var stacks = DropRoller.RollDeath(definition, new Random(3), victimLevel: 20, killerLevel: 20);
        Assert.InRange(stacks.Count, 1, DropRoller.DropMaxOccurrencesNormal);
    }

    static DropNpcDefinition Adena(double chance) => new()
    {
        NpcId = 1,
        Death = new DropDeathDefinition
        {
            Groups =
            [
                new DropGroupDefinition
                {
                    Chance = chance,
                    Items = [new DropItemDefinition { ItemId = 57, Min = 1, Max = 1, Chance = 100 }]
                }
            ]
        }
    };

    [Fact]
    public void Seeded_gremlin_death_is_stable()
    {
        var first = MobiusDropTestSeed.ExpectedGremlinDeathDrop();
        var second = MobiusDropTestSeed.ExpectedGremlinDeathDrop();
        Assert.Equal(first, second);
    }
}
