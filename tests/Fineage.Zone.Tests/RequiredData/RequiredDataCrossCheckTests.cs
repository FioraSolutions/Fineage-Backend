using System.Text.Json.Nodes;
using Fineage.Game.RequiredData;
using Fineage.Zone.RequiredData;
using Xunit;

namespace Fineage.Zone.Tests.RequiredData;

/// <summary>#985: boot cross-check names every dangling id; the shipped data has none.</summary>
public sealed class RequiredDataCrossCheckTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "fineage-xref-" + Guid.NewGuid().ToString("N"));

    public RequiredDataCrossCheckTests() => WriteCleanFixture();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    static string RealDataDir() =>
        Path.GetDirectoryName(RequiredDataRegistry.FindFile(RequiredDataRegistry.FileName)
            ?? throw new FileNotFoundException("data/required-data-registry.json"))!;

    /// <summary>CI gate: the repo data must resolve every cross-catalog reference.</summary>
    [Fact]
    public void Shipped_data_has_no_dangling_references()
    {
        string dir = RealDataDir();
        var violations = RequiredDataCrossCheck.Run(dir, RequiredDataRegistry.Load(Path.Combine(dir, RequiredDataRegistry.FileName)));
        Assert.True(violations.Count == 0, "dangling data references:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Clean_fixture_passes()
    {
        Assert.Empty(RequiredDataCrossCheck.Run(_dir, RegistryWithoutHoles()));
    }

    public static TheoryData<string, string, RequiredDataKind, string> Holes => new()
    {
        { "spawns/map.json", """{"npcs":[{"npcId":999001}]}""", RequiredDataKind.Npc, "999001" },
        { "drops/npc-100.json", """{"npcId":100,"death":{"groups":[{"items":[{"itemId":999002}]}]}}""", RequiredDataKind.Item, "999002" },
        { "raidbosses.json", """{"bosses":[{"npcId":999003}]}""", RequiredDataKind.Npc, "999003" },
        { "npc-minions.json", """{"masters":{"100":[{"id":999004,"count":1}]}}""", RequiredDataKind.Npc, "999004" },
        { "summons.json", """{"summons":[{"skillId":1,"levels":[{"npcId":100,"itemId":999005}]}],"npcSkills":{}}""", RequiredDataKind.Item, "999005" },
        { "skill-trees.json", """{"trees":[{"classId":0,"skills":[{"skillId":999006}]}]}""", RequiredDataKind.Skill, "999006" },
        { "fishing.json", """{"skillTree":[{"skillId":1,"items":[{"itemId":999007}]}]}""", RequiredDataKind.Item, "999007" },
        { "pledge-skills.json", """{"skills":[{"skillId":999008,"items":[]}]}""", RequiredDataKind.Skill, "999008" },
        { "item-equip-skills.json", """{"items":[{"itemId":57,"skills":[{"id":999009,"level":1}]}]}""", RequiredDataKind.Skill, "999009" },
        { "herbs.json", """{"items":[{"itemId":57,"skills":[{"skillId":999010}]}]}""", RequiredDataKind.Skill, "999010" },
        { "cursed-weapons.json", """{"blades":[{"itemId":57,"skillId":999011}]}""", RequiredDataKind.Skill, "999011" },
        { "item-skills.json", """{"items":[{"itemId":999012,"effects":[]}]}""", RequiredDataKind.Item, "999012" },
        { "buylists.json", """{"lists":[{"id":1,"npcs":[100],"items":[{"id":999013}]}]}""", RequiredDataKind.Item, "999013" },
        { "multisell.json", """{"lists":[{"id":1,"entries":[{"ingredients":[{"id":-200,"count":1}],"products":[{"id":999014}]}]}]}""", RequiredDataKind.Item, "999014" },
        { "recipes.json", """{"recipes":[{"id":1,"recipeItemId":57,"ingredients":[],"production":{"itemId":999015}}]}""", RequiredDataKind.Item, "999015" },
        { "npc-ai-support.json", """{"npcs":[{"npcId":100,"buff":[{"id":999016}]}]}""", RequiredDataKind.Skill, "999016" },
        { "html/merchant/100.htm", """<a action="bypass -h npc_%objectId%_Buy 999017">Buy</a>""", RequiredDataKind.BuyList, "999017" },
        { "html/merchant/200.htm", """<a action="bypass -h npc_%objectId%_Buy 1">Buy</a>""", RequiredDataKind.BuyList, "1@200" },
        { "html/merchant/100-1.htm", """<a action="bypass -h npc_%objectId%_multisell 999018">Trade</a>""", RequiredDataKind.MultiSell, "999018" },
        { "html/teleporter/100.htm", """<a action="bypass -h npc_%objectId%_teleport NOBLES_TOKEN 0">Go</a>""", RequiredDataKind.Teleport, "100:NOBLES_TOKEN" },
        { "html/default/100.htm", """<a action="bypass -h Script NotPorted">Talk</a>""", RequiredDataKind.NpcScript, "NotPorted" },
        { "html/default/100-2.htm", """<a action="bypass -h Script 999 1">Quest</a>""", RequiredDataKind.Quest, "999" },
    };

    [Theory]
    [MemberData(nameof(Holes))]
    public void Holey_data_names_the_missing_key_and_source(string file, string content, RequiredDataKind kind, string key)
    {
        Write(file, content);
        var violations = RequiredDataCrossCheck.Run(_dir, RegistryWithoutHoles());
        var hit = Assert.Single(violations);
        Assert.Equal(kind, hit.Kind);
        Assert.Equal(key, hit.Key);
        Assert.Equal(file, hit.Source);

        var ex = Assert.Throws<RequiredDataException>(() => RequiredDataCrossCheck.AssertClean(_dir, RegistryWithoutHoles()));
        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Contains(file, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownAbsent_suppresses_listed_hole_and_flags_stale_rows()
    {
        Write("html/default/100.htm", """<a action="bypass -h Script NotPorted">Talk</a>""");
        Assert.Empty(RequiredDataCrossCheck.Run(_dir, Registry(("npc-script", "NotPorted"))));

        File.Delete(Path.Combine(_dir, "html/default/100.htm"));
        var stale = Assert.Single(RequiredDataCrossCheck.Run(_dir, Registry(("npc-script", "NotPorted"))));
        Assert.Equal(RequiredDataKind.NpcScript, stale.Kind);
        Assert.Equal("NotPorted", stale.Key);
        Assert.Contains("knownAbsent", stale.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownAbsent_without_reason_is_rejected()
    {
        var json = RegistryJson();
        foreach (var kind in json["kinds"]!.AsArray())
        {
            if ((string)kind!["id"]! == "skill")
                kind["knownAbsent"] = new JsonArray(new JsonObject { ["key"] = "1" });
        }

        var ex = Assert.Throws<InvalidDataException>(() => RequiredDataRegistry.Parse(json.ToJsonString(), "test"));
        Assert.Contains("reason", ex.Message, StringComparison.Ordinal);
    }

    static JsonNode RegistryJson()
    {
        string path = Path.Combine(RealDataDir(), RequiredDataRegistry.FileName);
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    static RequiredDataRegistry RegistryWithoutHoles() => Registry();

    static RequiredDataRegistry Registry(params (string Kind, string Key)[] holes)
    {
        var json = RegistryJson();
        foreach (var kind in json["kinds"]!.AsArray())
        {
            var rows = new JsonArray();
            foreach (var (id, key) in holes)
            {
                if ((string)kind!["id"]! == id)
                    rows.Add(new JsonObject { ["key"] = key, ["reason"] = "test hole" });
            }

            kind!["knownAbsent"] = rows;
        }

        return RequiredDataRegistry.Parse(json.ToJsonString(), "test");
    }

    void Write(string relative, string content)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    void WriteCleanFixture()
    {
        Write("npcs.json", """{"npcs":[{"npcId":100},{"npcId":200}]}""");
        Write("items.json", """{"items":[{"id":57}]}""");
        Write("skills.json", """{"skills":[{"skillId":1}]}""");
        Write("spawns/map.json", """{"npcs":[{"npcId":100}]}""");
        Write("drops/npc-100.json", """{"npcId":100,"death":{"groups":[{"items":[{"itemId":57}]}]}}""");
        Write("raidbosses.json", """{"bosses":[]}""");
        Write("npc-minions.json", """{"masters":{}}""");
        Write("summons.json", """{"summons":[],"npcSkills":{}}""");
        Write("skill-trees.json", """{"trees":[{"classId":0,"skills":[{"skillId":1,"requiredItems":[{"itemId":57}]}]}]}""");
        Write("fishing.json", """{"skillTree":[]}""");
        Write("pledge-skills.json", """{"skills":[]}""");
        Write("item-equip-skills.json", """{"items":[]}""");
        Write("herbs.json", """{"items":[]}""");
        Write("cursed-weapons.json", """{"blades":[]}""");
        Write("item-skills.json", """{"items":[]}""");
        Write("buylists.json", """{"lists":[{"id":1,"npcs":[100],"items":[{"id":57}]}]}""");
        Write("multisell.json", """{"lists":[{"id":1,"entries":[]}]}""");
        Write("recipes.json", """{"recipes":[]}""");
        Write("npc-ai-support.json", """{"npcs":[]}""");
        Write("teleports.json", """{"gatekeepers":[{"npcId":100,"lists":[{"name":"NORMAL","locations":[]}]}]}""");
        Write("npc-scripts.json", """{"scripts":[{"id":"Known","pipeline":"stub","status":"stub"}]}""");
        Directory.CreateDirectory(Path.Combine(_dir, "quests", "00001_letters_of_love"));
        Write("html/teleporter/100-0.htm", """<a action="bypass -h npc_%objectId%_teleport NORMAL 0">Go</a><a action="bypass -h Script Known">x</a><a action="bypass -h Script 1">q</a>""");
        Write("html/admin/gm.htm", """<a action="bypass -h npc_%objectId%_Buy 424242">GM only</a>""");
    }
}
