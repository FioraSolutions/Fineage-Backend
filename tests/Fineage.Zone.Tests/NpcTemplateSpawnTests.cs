using Fineage.Game;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Npcs;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NpcTemplateSpawnTests
{
    const int MobiusTemplateCount = 6519;
    const int TalkingIslandNpcCount = 90;

    [Fact]
    public void Templates_load_and_every_spawn_references_one()
    {
        var templates = NpcCatalog.LoadDefault();
        Assert.Equal(MobiusTemplateCount, templates.ById.Count);

        var lector = templates.Get(30001);
        Assert.Equal("Lector", lector.Name);
        Assert.Equal("Trader", lector.Title);
        Assert.Equal("Merchant", lector.Type);
        Assert.Equal(70, lector.Level);
        Assert.Equal(10f, lector.CollisionRadius);
        Assert.Equal(24f, lector.CollisionHeight);
        Assert.False(lector.Attackable);
        Assert.False(lector.Aggressive);
        Assert.Equal(2444, lector.Hp);
        Assert.Equal(1346, lector.Mp);
        Assert.Equal(120f, lector.RunSpeed);
        Assert.Equal(50f, lector.WalkSpeed);

        var gremlin = templates.Get(TalkingIsland.GremlinNpcId);
        Assert.Equal("Gremlin", gremlin.Name);
        Assert.Equal("", gremlin.Title);
        Assert.Equal("Monster", gremlin.Type);
        Assert.True(gremlin.Attackable);
        Assert.False(gremlin.Aggressive);
        Assert.Equal(40, gremlin.Hp);
        Assert.Equal(50f, gremlin.RunSpeed);
        Assert.Equal(20f, gremlin.WalkSpeed);

        var spawns = SpawnCatalog.LoadDefault();
        var seen = new HashSet<int>();
        foreach (var map in spawns.Maps.Values)
        {
            Assert.NotEmpty(map.Npcs);
            foreach (var row in map.Npcs)
            {
                Assert.True(templates.ById.ContainsKey(row.NpcId), $"map {map.MapId} npc {row.NpcId}");
                Assert.False(string.IsNullOrWhiteSpace(row.Name));
                Assert.False(string.IsNullOrWhiteSpace(row.NpcType));
                Assert.True(seen.Add(row.ObjectId), $"duplicate object id {row.ObjectId}");
                Assert.InRange(row.ObjectId, NpcObjectIdAllocator.LegacyFirst, NpcObjectIdAllocator.ExclusiveEnd - 1);
            }
        }
    }

    [Fact]
    public void Cedric_gremlin_rows_keep_their_inline_stats()
    {
        var rows = SpawnCatalog.LoadDefault().GetMap(SpawnCatalog.DefaultMapId).Npcs;
        Assert.Equal([50001, 50002, 50003], rows.Select(row => row.ObjectId).ToArray());
        Assert.All(rows, row =>
        {
            Assert.Equal("Gremlin", row.Name);
            Assert.Equal("", row.Title);
            Assert.Equal("Monster", row.NpcType);
            Assert.True(row.Attackable);
            Assert.Equal(TalkingIsland.GremlinMaxHp, row.MaxHp);
            Assert.Equal(80f, row.RunSpeed);
            Assert.Equal(40f, row.WalkSpeed);
        });
    }

    [Fact]
    public void Talking_Island_village_matches_Mobius_and_other_towns_stay_disabled()
    {
        var spawns = SpawnCatalog.LoadDefault();
        var village = spawns.GetMap(SpawnCatalog.TalkingIslandVillageMapId);
        Assert.True(village.Enabled);
        Assert.Equal(TalkingIslandNpcCount, village.Npcs.Count);
        Assert.Equal(60000, village.Npcs[0].ObjectId);
        Assert.Equal(31688, village.Npcs[0].NpcId);
        Assert.Equal(-85151, village.Npcs[0].X);
        Assert.Equal(241476, village.Npcs[0].Y);
        Assert.Equal(-3728, village.Npcs[0].Z);
        Assert.Equal(25000, village.Npcs[0].Heading);
        Assert.Equal(60089, village.Npcs[^1].ObjectId);

        var silvia = Assert.Single(village.Npcs, row => row.NpcId == 30003);
        Assert.Equal("Silvia", silvia.Name);
        Assert.Equal("Trader", silvia.Title);
        Assert.Equal("Merchant", silvia.NpcType);
        Assert.False(silvia.Attackable);
        Assert.Equal(2444, silvia.MaxHp);
        Assert.Equal(120f, silvia.RunSpeed);
        Assert.Equal(50f, silvia.WalkSpeed);
        Assert.Equal(-83789, silvia.X);
        Assert.Equal(240799, silvia.Y);
        Assert.Equal(-3717, silvia.Z);
        Assert.Equal(32768, silvia.Heading);
        Assert.Equal(60, silvia.RespawnDelaySeconds);

        var arnold = Assert.Single(village.Npcs, row => row.NpcId == 30041);
        Assert.Equal("Arnold", arnold.Name);
        Assert.Equal("Guard", arnold.Title);
        Assert.Equal("Guard", arnold.NpcType);
        Assert.False(arnold.Attackable);
        Assert.Equal(-82319, arnold.X);
        Assert.Equal(244709, arnold.Y);
        Assert.Equal(-3728, arnold.Z);
        Assert.Equal(9000, arnold.Heading);

        var helper = Assert.Single(village.Npcs, row => row.NpcId == 30009);
        Assert.Equal("Newbie Helper", helper.Name);
        Assert.Equal("Folk", helper.NpcType);
        Assert.Equal(-71424, helper.X);
        Assert.Equal(258336, helper.Y);
        Assert.Equal(-3104, helper.Z);
        Assert.Equal(42000, helper.Heading);

        var expected = new Dictionary<string, int>
        {
            ["aden"] = 89,
            ["dark_elven_village"] = 123,
            ["dion"] = 83,
            ["dwarven_village"] = 70,
            ["elven_village"] = 82,
            ["floran"] = 13,
            ["giran"] = 113,
            ["gludin"] = 84,
            ["gludio"] = 79,
            ["goddard"] = 160,
            ["heine"] = 61,
            ["hunters"] = 63,
            ["orc_village"] = 72,
            ["oren"] = 61,
            ["rune"] = 195,
            ["schuttgart"] = 92
        };
        foreach (var (mapId, count) in expected)
        {
            var map = spawns.GetMap(mapId);
            Assert.False(map.Enabled);
            Assert.Equal(count, map.Npcs.Count);
            Assert.All(map.Npcs, row =>
                Assert.InRange(row.ObjectId, NpcObjectIdAllocator.First, NpcObjectIdAllocator.ExclusiveEnd - 1));
        }
    }

    [Fact]
    public void Default_world_keeps_the_gremlin_smoke_set()
    {
        var ids = new ZoneWorld().NpcObjectIds;
        foreach (var gremlin in TalkingIsland.Gremlins)
            Assert.Contains(gremlin.ObjectId, ids);
        Assert.DoesNotContain(60013, ids);
        Assert.Equal(ids, ZoneAoiSettings.CreateWorld().NpcObjectIds);
    }

    [Fact]
    public void Enabled_maps_include_the_Talking_Island_village()
    {
        var baseline = new ZoneWorld().NpcObjectIds;
        var world = new ZoneWorld(includeEnabledMaps: true);
        Assert.Equal(baseline.Count + TalkingIslandNpcCount, world.NpcObjectIds.Count);
        foreach (int id in baseline)
            Assert.Contains(id, world.NpcObjectIds);
        Assert.Contains(60013, world.NpcObjectIds);

        var hosted = ZoneAoiSettings.CreateWorld(includeEnabledMaps: true);
        Assert.Equal(world.NpcObjectIds, hosted.NpcObjectIds);
    }

    [Fact]
    public async Task Merchant_NpcInfo_is_not_attackable_and_carries_the_title()
    {
        var world = new ZoneWorld(activeMapId: SpawnCatalog.TalkingIslandVillageMapId);
        var info = world.NpcInfoByTemplateForTests(30003);
        Assert.Equal("Silvia", info.Name);
        Assert.Equal("Trader", info.Title);
        Assert.Equal("Merchant", info.NpcType);
        Assert.False(info.Attackable);
        Assert.Equal(30003, info.NpcId);
        Assert.Equal(-83789, info.X);
        Assert.Equal(240799, info.Y);
        Assert.Equal(-3717, info.Z);
        Assert.Equal(32768, info.Heading);
        Assert.Equal(120f, info.RunSpeed);
        Assert.Equal(50f, info.WalkSpeed);
        Assert.InRange(info.ObjectId, NpcObjectIdAllocator.First, NpcObjectIdAllocator.ExclusiveEnd - 1);

        byte[] bytes = MessagePackSerializer.Serialize(info);
        var roundTrip = MessagePackSerializer.Deserialize<NpcInfoMsg>(bytes);
        Assert.Equal(info.Title, roundTrip.Title);
        Assert.Equal(info.NpcType, roundTrip.NpcType);
        Assert.False(roundTrip.Attackable);
        var v2 = MessagePackSerializer.Deserialize<Fineage.Protocol.V2.NpcInfoMsg>(bytes);
        Assert.Equal("Trader", v2.Title);
        Assert.Equal("Merchant", v2.NpcType);
        Assert.False(v2.Attackable);

        var login = new LoginService(JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("merchant", "secret1", "Buyer", HumanClasses.Fighter).Value!;
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.CharacterId, ticket.Account, ticket.CharacterName, ticket.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.Equal("target", await world.TrySelectTargetAsync(session, new SelectTargetMsg { TargetObjectId = info.ObjectId }, CancellationToken.None));
        Assert.Equal("attack", await world.TryAttackAsync(session, new AttackRequestMsg { TargetObjectId = info.ObjectId }, CancellationToken.None));
    }

    [Fact]
    public void Missing_object_id_allocates_in_the_town_band()
    {
        string root = NewDataRoot();
        try
        {
            WriteNpc(root, 30003, "Silvia", "Trader", "Merchant", attackable: false);
            File.WriteAllText(Path.Combine(root, "spawns", "only.json"), """
                {
                  "mapId": "only",
                  "enabled": true,
                  "npcs": [
                    { "npcId": 30003, "x": 1, "y": 2, "z": 3, "heading": 4 },
                    { "objectId": 60000, "npcId": 30003, "x": 5, "y": 6, "z": 7, "heading": 8 },
                    { "objectId": 50001, "npcId": 30003, "x": 9, "y": 10, "z": 11, "heading": 12 }
                  ]
                }
                """);

            var rows = SpawnCatalog.Load(root).GetMap("only").Npcs;
            Assert.Equal(60001, rows[0].ObjectId);
            Assert.Equal(60000, rows[1].ObjectId);
            Assert.Equal(50001, rows[2].ObjectId);
            Assert.Equal("Silvia", rows[0].Name);
            Assert.Equal("Trader", rows[0].Title);
            Assert.False(rows[0].Attackable);
            Assert.Equal(2444, rows[0].MaxHp);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Object_ids_inside_the_character_or_item_range_are_rejected()
    {
        string root = NewDataRoot();
        try
        {
            WriteNpc(root, 30003, "Silvia", "Trader", "Merchant", attackable: false);
            File.WriteAllText(Path.Combine(root, "spawns", "only.json"), """
                {
                  "mapId": "only",
                  "enabled": true,
                  "npcs": [ { "objectId": 100000, "npcId": 30003, "x": 1, "y": 2, "z": 3, "heading": 4 } ]
                }
                """);
            Assert.Throws<InvalidDataException>(() => SpawnCatalog.Load(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string NewDataRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "fineage-npcs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "spawns"));
        return root;
    }

    static void WriteNpc(string root, int npcId, string name, string title, string type, bool attackable)
    {
        File.WriteAllText(Path.Combine(root, "npcs.json"), $$"""
            {
              "npcs": [
                {
                  "npcId": {{npcId}},
                  "name": "{{name}}",
                  "title": "{{title}}",
                  "type": "{{type}}",
                  "level": 70,
                  "collisionRadius": 8,
                  "collisionHeight": 23,
                  "attackable": {{(attackable ? "true" : "false")}},
                  "aggressive": false,
                  "hp": 2444,
                  "mp": 1346,
                  "runSpeed": 120,
                  "walkSpeed": 50
                }
              ]
            }
            """);
    }
}
