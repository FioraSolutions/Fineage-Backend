using Fineage.Zone.Drops;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class EventDropTests
{
    const string ActiveEvent = """
        {
          "events": [{
            "id": "test",
            "startUnixMs": 1000,
            "endUnixMs": 5000,
            "maxKillerLevelGap": 9,
            "groups": [{
              "chancePerMillion": 1000000,
              "items": [{ "itemId": 6392, "min": 2, "max": 3, "weight": 100 }]
            }]
          }]
        }
        """;

    [Fact]
    public void Inactive_window_yields_no_extra_drops()
    {
        var catalog = EventDropCatalog.Parse(ActiveEvent);
        var rng = new Random(1);
        Assert.Empty(catalog.RollExtra(rng, null, nowUnixMs: 0, victimLevel: 10, killerLevel: 10));
    }

    [Fact]
    public void Active_window_rolls_configured_stacks_without_hardcoded_ids()
    {
        var catalog = EventDropCatalog.Parse(ActiveEvent);
        var rng = new Random(1);
        var stacks = catalog.RollExtra(rng, null, nowUnixMs: 2000, victimLevel: 20, killerLevel: 20);
        Assert.Single(stacks);
        Assert.Equal(6392, stacks[0].ItemId);
        Assert.InRange(stacks[0].Count, 2, 3);
    }

    [Fact]
    public void Level_gap_beyond_config_skips_event_rows()
    {
        var catalog = EventDropCatalog.Parse(ActiveEvent);
        var rng = new Random(1);
        Assert.Empty(catalog.RollExtra(rng, null, 2000, victimLevel: 10, killerLevel: 30));
    }

    [Fact]
    public void DropRoller_event_pipeline_rolls_independent_groups()
    {
        var groups = new[]
        {
            new DropGroupDefinition
            {
                Chance = 100,
                Items = [new DropItemDefinition { ItemId = 57, Min = 10, Max = 10, Chance = 100 }]
            }
        };
        var stacks = DropRoller.RollEventDeath(groups, new Random(2), victimLevel: 5, killerLevel: 40);
        Assert.Single(stacks);
        Assert.Equal(10, stacks[0].Count);
    }
}