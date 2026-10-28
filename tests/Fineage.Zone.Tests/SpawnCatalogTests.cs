using Fineage.Zone.Spawns;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class SpawnCatalogTests
{
    [Fact]
    public void Default_datapack_loads_cedric_and_dormant_village_map()
    {
        var catalog = SpawnCatalog.LoadDefault();
        Assert.True(catalog.Maps.ContainsKey(SpawnCatalog.DefaultMapId));
        Assert.True(catalog.Maps.ContainsKey("ti_village_road"));

        var cedric = catalog.GetMap(SpawnCatalog.DefaultMapId);
        Assert.True(cedric.Enabled);
        Assert.Equal(3, cedric.Npcs.Count);
        Assert.All(cedric.Npcs, row => Assert.Equal(TalkingIsland.GremlinNpcId, row.NpcId));

        var village = catalog.GetMap("ti_village_road");
        Assert.False(village.Enabled);
        Assert.Equal(2, village.Npcs.Count);
    }

    [Fact]
    public void Cedric_rows_match_TalkingIsland_gremlin_layout()
    {
        var rows = SpawnCatalog.LoadDefault().GetMap(SpawnCatalog.DefaultMapId).Npcs;
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
