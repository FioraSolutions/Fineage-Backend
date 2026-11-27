using Fineage.Game.RequiredData;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>#985: holey data for every registry kind must name the missing key.</summary>
public sealed class RequiredDataRegistryTests
{
    public RequiredDataRegistryTests()
    {
        Required.UseRegistry(null);
    }

    [Fact]
    public void Registry_lists_every_kind_with_policy_and_file()
    {
        var registry = RequiredDataRegistry.LoadDefault();
        Assert.Equal(Enum.GetValues<RequiredDataKind>().Length, registry.Entries.Count);
        foreach (RequiredDataKind kind in Enum.GetValues<RequiredDataKind>())
        {
            var entry = registry.Get(kind);
            Assert.False(string.IsNullOrWhiteSpace(entry.File));
            Assert.False(string.IsNullOrWhiteSpace(entry.KeyName));
            Assert.True(entry.Policy is RequiredDataPolicy.FailFastBoot or RequiredDataPolicy.RefuseAction);
        }
    }

    [Fact]
    public void Parse_rejects_unknown_kind_and_missing_enum_member()
    {
        const string missingSpawn = """
            {"version":1,"kinds":[
              {"id":"skill","file":"skills.json","policy":"refuse-action","keyName":"skillId"},
              {"id":"item","file":"items.json","policy":"refuse-action","keyName":"itemId"},
              {"id":"npc","file":"npcs.json","policy":"refuse-action","keyName":"npcId"},
              {"id":"teleport","file":"teleports.json","policy":"refuse-action","keyName":"npcId"},
              {"id":"droplist","file":"drops/","policy":"refuse-action","keyName":"npcId"},
              {"id":"skill-passive","file":"skill-passives.json","policy":"refuse-action","keyName":"skillId"},
              {"id":"npc-ai","file":"npc-ai-support.json","policy":"refuse-action","keyName":"npcId"},
              {"id":"quest","file":"quests/","policy":"refuse-action","keyName":"questId"}
            ]}
            """;
        var ex = Assert.Throws<InvalidDataException>(() => RequiredDataRegistry.Parse(missingSpawn, "test"));
        Assert.Contains("spawn", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RequiredDataKind.Skill, "skillId")]
    [InlineData(RequiredDataKind.Item, "itemId")]
    [InlineData(RequiredDataKind.Npc, "npcId")]
    [InlineData(RequiredDataKind.Teleport, "npcId")]
    [InlineData(RequiredDataKind.Droplist, "npcId")]
    [InlineData(RequiredDataKind.SkillPassive, "skillId")]
    [InlineData(RequiredDataKind.NpcAi, "npcId")]
    [InlineData(RequiredDataKind.Quest, "questId")]
    [InlineData(RequiredDataKind.Spawn, "map")]
    public void Missing_names_kind_key_and_catalog_file(RequiredDataKind kind, string key)
    {
        var registry = RequiredDataRegistry.LoadDefault();
        Required.UseRegistry(registry);
        var ex = Required.Missing(kind, key, "holey fixture");
        Assert.Equal(kind, ex.Kind);
        Assert.Equal(key, ex.Key);
        Assert.Equal(registry.Get(kind).File, ex.CatalogFile);
        Assert.Contains(kind.ToId(), ex.Message, StringComparison.Ordinal);
        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Contains(ex.CatalogFile, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SkillCatalog_Require_refuses_unknown_skill_with_name()
    {
        var catalog = SkillCatalog.Parse("""{"skills":[]}""", "test-skills.json");
        var ex = Assert.Throws<RequiredDataException>(() => catalog.Require(424242));
        Assert.Equal(RequiredDataKind.Skill, ex.Kind);
        Assert.Equal("424242", ex.Key);
        Assert.Contains("skills.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TeleportCatalog_RequireLists_refuses_empty_gatekeeper()
    {
        var ex = Assert.Throws<RequiredDataException>(() => TeleportCatalog.Empty.RequireLists(30177));
        Assert.Equal(RequiredDataKind.Teleport, ex.Kind);
        Assert.Equal("30177", ex.Key);
        Assert.Contains("teleports.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailFastBoot_refuses_missing_spawn_pack()
    {
        var registry = RequiredDataRegistry.LoadDefault();
        string tmp = Path.Combine(Path.GetTempPath(), "fineage-required-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var ex = Assert.Throws<RequiredDataException>(() => registry.AssertBootPacksPresent(tmp));
            Assert.Equal(RequiredDataKind.Spawn, ex.Kind);
            Assert.Contains("spawns", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void NoteSilentFallback_invokes_observer()
    {
        var seen = new List<(RequiredDataKind Kind, string Key)>();
        Required.SilentFallbackObserver = (kind, key) => seen.Add((kind, key));
        try
        {
            Required.NoteSilentFallback(RequiredDataKind.Item, "57");
            Assert.Equal([(RequiredDataKind.Item, "57")], seen);
        }
        finally
        {
            Required.SilentFallbackObserver = null;
        }
    }
}
