using Fineage.Zone.Spawns;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class SpawnCatalogTests
{
    [Fact]
    public void Default_datapack_loads_village_smoke_hall_and_dormant_road()
    {
        var catalog = SpawnCatalog.LoadDefault();
        Assert.True(catalog.Maps.ContainsKey(SpawnCatalog.DefaultMapId));
        Assert.True(catalog.Maps.ContainsKey(SpawnCatalog.SmokeMapId));
        Assert.True(catalog.Maps.ContainsKey("ti_village_road"));

        var village = catalog.GetMap(SpawnCatalog.DefaultMapId);
        Assert.True(village.Enabled);
        Assert.Equal("ti_village", village.MapId);

        var cedric = catalog.GetMap(SpawnCatalog.SmokeMapId);
        Assert.True(cedric.Enabled);
        Assert.Equal(3, cedric.Npcs.Count);
        Assert.All(cedric.Npcs, row => Assert.Equal(TalkingIsland.GremlinNpcId, row.NpcId));

        var road = catalog.GetMap("ti_village_road");
        Assert.False(road.Enabled);
        Assert.Equal(2, road.Npcs.Count);
    }

    [Fact]
    public void Cedric_rows_match_TalkingIsland_gremlin_layout()
    {
        var rows = SpawnCatalog.LoadDefault().GetMap(SpawnCatalog.SmokeMapId).Npcs;
        Assert.Equal(TalkingIsland.Gremlins.Length, rows.Count);
        for (int i = 0; i < TalkingIsland.Gremlins.Length; i++)
        {
            var expected = TalkingIsland.Gremlins[i];
            var row = rows[i];
            Assert.Equal(expected.ObjectId, row.ObjectId);
            Assert.Equal(expected.X, row.X);
            Assert.Equal(expected.Y, row.Y);
            Assert.Equal(expected.Z, row.Z);
            Assert.Equal(expected.Heading, row.Heading);
        }
    }
}
