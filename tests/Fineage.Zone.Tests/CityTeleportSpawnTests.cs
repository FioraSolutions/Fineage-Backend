using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Npcs;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Town, village, and outdoor spawn maps the live zone materializes so gatekeepers stand
/// and a destination within <see cref="NpcServiceProtocol.LoadedZoneRadius"/> loads.
/// </summary>
public sealed class CityTeleportSpawnTests
{
    static readonly string[] TownMaps =
    [
        "aden",
        "dark_elven_village",
        "dion",
        "dwarven_village",
        "elven_village",
        "floran",
        "giran",
        "gludin",
        "gludio",
        "goddard",
        "heine",
        "hunters",
        "orc_village",
        "oren",
        "rune",
        "schuttgart"
    ];

    static readonly (int NpcId, string MapId, string Name)[] Gatekeepers =
    [
        (30006, SpawnCatalog.TalkingIslandVillageMapId, "Roxxy"),
        (30059, "dion", "Trisha"),
        (30080, "giran", "Clarissa"),
        (30134, "dark_elven_village", "Jasmine"),
        (30146, "elven_village", "Mirabel"),
        (30177, "oren", "Valentina"),
        (30233, "hunters", "Esmeralda"),
        (30256, "gludio", "Bella"),
        (30320, "gludin", "Richlin"),
        (30540, "dwarven_village", "Wirphy"),
        (30576, "orc_village", "Tamil"),
        (30836, "giran", "Minerva"),
        (30848, "aden", "Elisa"),
        (30899, "heine", "Flauen"),
        (31275, "goddard", "Tatiana"),
        (31320, "rune", "Ilyana"),
        (31964, "schuttgart", "Bilia")
    ];

    [Fact]
    public void Town_and_village_maps_are_enabled_and_the_road_stays_off()
    {
        var catalog = SpawnCatalog.LoadDefault();
        Assert.True(catalog.GetMap(SpawnCatalog.DefaultMapId).Enabled);
        Assert.True(catalog.GetMap(SpawnCatalog.TalkingIslandVillageMapId).Enabled);
        foreach (string mapId in TownMaps)
        {
            var map = catalog.GetMap(mapId);
            Assert.True(map.Enabled, mapId);
            Assert.NotEmpty(map.Npcs);
        }

        Assert.False(catalog.GetMap("ti_village_road").Enabled);
    }

    [Fact]
    public void Enabled_world_stands_gatekeepers_and_loads_town_squares()
    {
        var templates = NpcCatalog.LoadDefault();
        var catalog = SpawnCatalog.LoadDefault();
        var teleports = TeleportCatalog.LoadDefault();
        var world = new ZoneWorld(includeEnabledMaps: true);
        var spawned = world.NpcObjectIds.ToHashSet();
        var anchors = new List<(int X, int Y)>();

        foreach (var map in catalog.Maps.Values)
        {
            if (!map.Enabled)
                continue;
            foreach (var npc in map.Npcs)
            {
                Assert.Contains(npc.ObjectId, spawned);
                anchors.Add((npc.X, npc.Y));
            }
        }

        foreach (var (npcId, mapId, name) in Gatekeepers)
        {
            var template = templates.Get(npcId);
            Assert.Equal(name, template.Name);
            Assert.Equal("Teleporter", template.Type);
            var row = Assert.Single(catalog.GetMap(mapId).Npcs, npc => npc.NpcId == npcId);
            Assert.Equal(name, row.Name);
            Assert.Equal("Teleporter", row.NpcType);
            Assert.Contains(row.ObjectId, spawned);
            Assert.NotNull(teleports.Find(npcId, TeleportRules.Normal));
        }

        var roxxy = teleports.Find(30006, TeleportRules.Normal);
        Assert.NotNull(roxxy);
        foreach (var loc in roxxy.Locations)
            Assert.True(Loaded(loc, anchors), loc.Name);

        AssertLoaded(teleports, anchors, 30080, "The Town of Dion");
        AssertLoaded(teleports, anchors, 30080, "Town of Aden");
        AssertLoaded(teleports, anchors, 30080, "Town of Oren");
        AssertLoaded(teleports, anchors, 30080, "Heine");
        AssertLoaded(teleports, anchors, 30080, "The Town of Gludio");
        AssertLoaded(teleports, anchors, 30080, "Town of Goddard");
        AssertLoaded(teleports, anchors, 30080, "Rune Township");
        AssertLoaded(teleports, anchors, 30080, "Town of Schuttgart");
        AssertLoaded(teleports, anchors, 30006, "The Village of Gludin");
        AssertLoaded(teleports, anchors, 30006, "Dark Elf Village");
        AssertLoaded(teleports, anchors, 30006, "Elven Village");
        AssertLoaded(teleports, anchors, 30006, "Dwarven Village");
        AssertLoaded(teleports, anchors, 30006, "Orc Village");
        AssertLoaded(teleports, anchors, 30177, "Hunters Village");
        AssertLoaded(teleports, anchors, 30836, "Hunter's Village");
        AssertLoaded(teleports, anchors, 30540, "Town of Schuttgart");
        AssertLoaded(teleports, anchors, 35403, "Entrance to Floran Village", "tel2");

        AssertLoaded(teleports, anchors, 30080, "Giran Harbor");
        AssertLoaded(teleports, anchors, 30059, "Cruma Marshlands");
        AssertLoaded(teleports, anchors, 30540, "Mithril Mines");
        Assert.False(NpcServiceProtocol.InLoadedZone(-200_000, 0, anchors));
    }

    static void AssertLoaded(
        TeleportCatalog teleports,
        IReadOnlyList<(int X, int Y)> anchors,
        int npcId,
        string destination,
        string listName = TeleportRules.Normal)
    {
        var loc = Find(teleports, npcId, destination, listName);
        Assert.True(Loaded(loc, anchors), destination);
    }

    static TeleportLocation Find(TeleportCatalog teleports, int npcId, string destination, string listName)
    {
        var list = teleports.Find(npcId, listName);
        Assert.NotNull(list);
        return Assert.Single(list.Locations, loc => loc.Name == destination);
    }

    static bool Loaded(TeleportLocation loc, IReadOnlyList<(int X, int Y)> anchors) =>
        NpcServiceProtocol.InLoadedZone(loc.X, loc.Y, anchors);
}
