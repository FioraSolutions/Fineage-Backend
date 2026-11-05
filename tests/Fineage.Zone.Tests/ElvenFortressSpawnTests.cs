using Fineage.Zone.Spawns;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Elven Underground Fortress split out of others_20_20 for Q316 / Q408 (scripts/gen_elven_fortress.py).</summary>
public sealed class ElvenFortressSpawnTests
{
    const string LocalBounds = "20000,20000,80000,80000;5000,60000,35000,90000";

    [Fact]
    public void Fortress_holds_the_quest_mobs_underground()
    {
        var catalog = SpawnCatalog.LoadDefault();
        var map = catalog.GetMap("elven_fortress");
        Assert.True(map.Enabled);
        Assert.Equal(153, map.Npcs.Count);
        Assert.Equal(15, map.Npcs.Count(r => r.NpcId == 20040)); // Sukar Wererat (Q316)
        Assert.Equal(11, map.Npcs.Count(r => r.NpcId == 20047)); // Sukar Wererat Leader (Q316, Q408 amethyst)
        var varool = Assert.Single(map.Npcs, r => r.NpcId == 27020); // Varool Foulclaw (Q316)
        Assert.Equal((13734, 80168, -4080), (varool.X, varool.Y, varool.Z));
        Assert.All(map.Npcs, r => Assert.True(r.Z < -3700 && r.Attackable, $"{r.NpcId}@{r.Z}"));

        var boxes = ZoneMapFilter.ParseBounds(LocalBounds);
        Assert.All(map.Npcs, r => Assert.True(ZoneMapFilter.Contains(boxes, r.X, r.Y)));

        // Moved, not copied: no duplicate object ids with the world tile.
        Assert.DoesNotContain(catalog.GetMap("others_20_20").Npcs, r => r.NpcId is 20040 or 20047 or 27020);
        Assert.Equal(9, catalog.GetMap("others_20_20").Npcs.Count);
    }

    [Fact]
    public void Default_elven_bounds_still_exclude_the_fortress_without_the_second_box()
    {
        var old = ZoneMapFilter.ParseBounds("20000,20000,80000,80000");
        var varool = SpawnCatalog.LoadDefault().GetMap("elven_fortress").Npcs.Single(r => r.NpcId == 27020);
        Assert.False(ZoneMapFilter.Contains(old, varool.X, varool.Y));
    }
}
