using Fineage.Game;
using Fineage.Zone.Shops;
using Fineage.Zone.Spawns;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class RegionSpawnTests
{
    static readonly (int X, int Y)[] CedricGremlinRing =
    [
        (-71944, 258332),
        (-71316, 257772),
        (-70728, 258468),
        (-71364, 259000)
    ];

    [Fact]
    public void Talking_island_outdoor_fauna_matches_the_mobius_monster_list()
    {
        var map = SpawnCatalog.LoadDefault().GetMap("ti_monsters");
        Assert.True(map.Enabled);
        Assert.Equal(442, map.Npcs.Count);
        Assert.Equal(8, Count(map, 20432));
        Assert.Equal(31, Count(map, 20481));
        Assert.Equal(29, Count(map, 20544));
        Assert.Equal(32, Count(map, 20120));
        Assert.Equal(18, Count(map, 20442));
        Assert.Equal(20, Count(map, 20326));
        Assert.DoesNotContain(map.Npcs, row => row.NpcId == TalkingIsland.GremlinNpcId);
        Assert.All(map.Npcs, row =>
        {
            Assert.InRange(row.X, -130_000, -60_000);
            Assert.InRange(row.Y, 200_000, 270_000);
            Assert.Equal("Monster", row.NpcType);
            Assert.True(row.Attackable);
        });

        var keltirs = map.Npcs.Where(row => row.NpcId == 20481).Take(10).ToArray();
        Assert.Equal(10, keltirs.Length);
        Assert.All(keltirs, row => Assert.Equal(-3451, row.Z));
    }

    [Fact]
    public void Training_hall_gremlins_stay_inside_the_cedric_territory()
    {
        var map = SpawnCatalog.LoadDefault().GetMap("training_hall_gremlins");
        Assert.True(map.Enabled);
        Assert.Equal(80, map.Npcs.Count);
        var cedric = map.Npcs.Where(row => row.X is > -73_000 and < -70_000 && row.Y is > 257_000 and < 260_000).ToArray();
        Assert.Equal(10, cedric.Length);
        Assert.All(cedric, row =>
        {
            Assert.Equal(18342, row.NpcId);
            Assert.Equal("Gremlin", row.Name);
            Assert.Equal(-3148, row.Z);
            Assert.True(Inside(row.X, row.Y, CedricGremlinRing));
        });
    }

    [Fact]
    public void Talking_island_quest_monsters_keep_fixed_coordinates()
    {
        var rows = SpawnCatalog.LoadDefault().GetMap("ti_quest_monsters").Npcs;
        Assert.Equal(3, rows.Count);
        var spirit = Assert.Single(rows, row => row.NpcId == 27004);
        Assert.Equal(-99679, spirit.X);
        Assert.Equal(235855, spirit.Y);
        Assert.Equal(-3384, spirit.Z);
        Assert.Equal(0, spirit.Heading);
        Assert.Equal(180, spirit.RespawnDelaySeconds);
    }

    [Fact]
    public void Region_maps_are_enabled_without_colliding_with_towns_or_bosses()
    {
        var catalog = SpawnCatalog.LoadDefault();
        foreach (string mapId in new[]
        {
            "gludio_wasteland",
            "orc_starting",
            "elven_starting",
            "dark_elf_starting",
            "dwarven_starting",
            "giran_devils_isle",
            "aden_tower_of_insolence",
            "others_19_22",
            "oren_ivory_tower_npcs",
            "castles_aden"
        })
        {
            var map = catalog.GetMap(mapId);
            Assert.True(map.Enabled, mapId);
            Assert.NotEmpty(map.Npcs);
        }

        Assert.False(catalog.GetMap("ti_village_road").Enabled);
        Assert.Equal(90, catalog.GetMap(SpawnCatalog.TalkingIslandVillageMapId).Npcs.Count);
        Assert.Equal(21, Count(catalog.GetMap("others_19_22"), 20506));
        Assert.Equal(25, Count(catalog.GetMap("orc_starting"), 20311));

        var ids = catalog.Maps.Values.SelectMany(map => map.Npcs).Select(row => row.ObjectId).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.DoesNotContain(ids, id => id is >= 80_001 and < 82_000);
        Assert.Equal(15, MerchantTowns.For("ti_monsters").BaseTaxPercent);
        Assert.Equal(1, MerchantTowns.For("ti_monsters").CastleId);
        Assert.Equal(20, MerchantTowns.For("gludio_wasteland").BaseTaxPercent);
        Assert.Equal(15, MerchantTowns.For("ti_village").BaseTaxPercent);
        Assert.Equal(0, MerchantTowns.For("others_19_22").BaseTaxPercent);
    }

    static int Count(SpawnMapDefinition map, int npcId) => map.Npcs.Count(row => row.NpcId == npcId);

    static bool Inside(int x, int y, IReadOnlyList<(int X, int Y)> ring)
    {
        bool inside = false;
        int j = ring.Count - 1;
        for (int i = 0; i < ring.Count; i++)
        {
            var (xi, yi) = ring[i];
            var (xj, yj) = ring[j];
            if ((yi > y) != (yj > y))
            {
                double cross = (xj - xi) * (y - yi) / (double)(yj - yi) + xi;
                if (x < cross)
                    inside = !inside;
            }

            j = i;
        }

        return inside;
    }
}
