using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class PlayerPassiveSourcesTests
{
    [Fact]
    public void EnumerateOrdered_reports_same_levels_regardless_of_source_insert_order()
    {
        var knownA = new Dictionary<int, int> { [100] = 5, [376] = 1, [500] = 2 };
        var clanA = new Dictionary<int, int> { [200] = 1, [376] = 3 };
        var gearA = new Dictionary<int, int> { [300] = 4, [376] = 2 };

        var knownB = new Dictionary<int, int> { [500] = 2, [376] = 1, [100] = 5 };
        var clanB = new Dictionary<int, int> { [376] = 3, [200] = 1 };
        var gearB = new Dictionary<int, int> { [376] = 2, [300] = 4 };

        var first = PlayerPassiveSources.EnumerateOrdered(knownA, clanA, gearA).Select(p => (p.Key, p.Value)).ToList();
        var second = PlayerPassiveSources.EnumerateOrdered(knownB, clanB, gearB).Select(p => (p.Key, p.Value)).ToList();

        Assert.Equal(first, second);
        Assert.Equal([100, 200, 300, 376, 500], first.Select(p => p.Key).ToArray());
        Assert.Equal(3, first.Single(p => p.Key == 376).Value);
    }

    [Fact]
    public void Passive_collect_yields_same_mods_when_merged_map_built_in_different_orders()
    {
        var cat = SkillStatEffectCatalog.LoadPassives();
        var mergedA = PlayerPassiveSources.Merge(
            new Dictionary<int, int> { [118] = 1, [163] = 1 },
            new Dictionary<int, int> { [200] = 1 },
            new Dictionary<int, int> { [300] = 1 });
        var mergedB = PlayerPassiveSources.Merge(
            new Dictionary<int, int> { [163] = 1, [118] = 1 },
            new Dictionary<int, int> { [200] = 1 },
            new Dictionary<int, int> { [300] = 1 });

        var modsA = cat.CollectPassives(mergedA, wearing: _ => false);
        var modsB = cat.CollectPassives(mergedB, wearing: _ => false);

        Assert.Equal(modsA.Apply(CombatStat.MAtkSpd, 400), modsB.Apply(CombatStat.MAtkSpd, 400));
        Assert.Equal(modsA.Apply(CombatStat.PAtkSpd, 300), modsB.Apply(CombatStat.PAtkSpd, 300));
    }
}
