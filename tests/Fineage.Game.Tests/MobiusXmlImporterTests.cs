using Fineage.Game.Mobius;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class MobiusXmlImporterTests
{
    static string FixtureRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mobius"));

    [Fact]
    public void Npc_reader_converts_gremlin_hp_and_speeds_from_xml()
    {
        var path = Path.Combine(FixtureRoot, "stats", "npcs", "20000-20099.xml");
        var gremlin = MobiusNpcXmlReader.ReadFile(path).Single(n => n.Id == 20001);

        Assert.Equal("Gremlin", gremlin.Name);
        Assert.Equal(40, gremlin.MaxHp);
        Assert.Equal(40f, gremlin.WalkSpeed);
        Assert.Equal(100f, gremlin.RunSpeed);
        Assert.True(gremlin.Attackable);
    }

    [Fact]
    public void Importer_writes_npcs_json_from_fixture_tree()
    {
        var outDir = NewTempDir();
        try
        {
            var result = MobiusCatalogImporter.Run(new MobiusImportOptions
            {
                XmlRoot = FixtureRoot,
                OutputDirectory = outDir,
                WriteNpcs = true,
                NpcIds = [20001]
            });

            Assert.Single(result.WrittenPaths);
            var json = File.ReadAllText(Path.Combine(outDir, "npcs.json"));
            Assert.Contains("\"id\": 20001", json);
            Assert.Contains("\"maxHp\": 40", json);
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Spawn_reader_parses_territory_npc_rows()
    {
        var path = Path.Combine(FixtureRoot, "spawns", "TalkingIsland", "TalkingIslandMonsters.xml");
        var rows = MobiusSpawnXmlReader.ReadFile(path);
        Assert.Single(rows);
        Assert.Equal("fixture_zone", rows[0].ZoneName);
        Assert.Equal(20432, rows[0].Npcs[0].NpcId);
        Assert.Equal(6, rows[0].Npcs[0].Count);
        Assert.Equal(25, rows[0].Npcs[0].RespawnDelaySeconds);
    }

    [Fact]
    public void Xml_root_selftest_entrypoint_succeeds()
    {
        var code = MobiusImportSelfTest.Run();
        Assert.Equal(0, code);
    }

    static string NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "fineage-mobius-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
}
