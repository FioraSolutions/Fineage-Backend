using Fineage.Game;
using Fineage.Zone.Npcs;
using Xunit;

namespace Fineage.MobiusDiff.Tests;

/// <summary>
/// Differential golden matrix for the six Interlude attributes and derived combat/vital stats (#979).
/// Every case is driven by a Java-generated oracle traced to Mobius CT0 bonus tables, FuncEnchant,
/// and operateType-P passives.
/// </summary>
public sealed class AttrStatsMatrixTests
{
    static readonly string Root = RepoPaths.FindRoot();
    static readonly GameData Data = GameData.LoadDefault();
    static readonly NpcCatalog Npcs = NpcCatalog.LoadDefault();

    public static TheoryData<string> MatrixScenarioIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var id in new[]
            {
                "stats-human-fighter-40",
                "stats-human-mystic-40",
                "stats-elven-fighter-40",
                "stats-elven-mystic-40",
                "stats-dark-fighter-40",
                "stats-dark-mystic-40",
                "stats-orc-fighter-40",
                "stats-orc-mystic-40",
                "stats-dwarf-fighter-40",
                "stats-human-fighter-1",
                "stats-human-mystic-1",
                "stats-human-fighter-40-enchant4",
                "stats-human-fighter-40-passives"
            })
                data.Add(id);
            return data;
        }
    }

    static (Scenario Scenario, OracleFile Oracle) Load(string id)
    {
        string scenarioPath = Path.Combine(Root, "tools", "Fineage.MobiusDiff", "scenarios", id + ".json");
        var scenario = ScenarioJson.Load(scenarioPath);
        string oraclePath = Path.Combine(Root, scenario.Oracle.Golden);
        return (scenario, ScenarioJson.LoadOracle(oraclePath));
    }

    static double Metric(DiffReport report, string name) =>
        Assert.Single(report.Rows, r => r.Parameter == name).Expected;

    [Theory]
    [MemberData(nameof(MatrixScenarioIds))]
    public void Matrix_scenario_matches_the_mobius_formula_oracle(string id)
    {
        var (scenario, oracle) = Load(id);
        var report = ScenarioRunner.Run(scenario, oracle, Data, Npcs);
        Assert.True(report.Pass, string.Join("; ", report.Rows.Where(r => !r.Pass).Select(r => $"{r.Parameter} {r.Actual} != {r.Expected}")));
    }

    [Fact]
    public void Negative_control_mutation_fails_an_attribute_cell()
    {
        var (scenario, oracle) = Load("stats-human-fighter-40");
        var report = ScenarioRunner.Run(scenario, oracle, Data, Npcs, actual => actual["str"] += 1);
        Assert.False(report.Pass);
        Assert.False(Assert.Single(report.Rows, r => r.Parameter == "str").Pass);
    }

    [Fact]
    public void Enchant_plus_four_raises_pAtk_pDef_and_mAtk_but_not_the_six_attributes()
    {
        var bare = ScenarioRunner.Run(Load("stats-human-fighter-40").Scenario,
            Load("stats-human-fighter-40").Oracle, Data, Npcs);
        var enc = ScenarioRunner.Run(Load("stats-human-fighter-40-enchant4").Scenario,
            Load("stats-human-fighter-40-enchant4").Oracle, Data, Npcs);
        Assert.True(bare.Pass && enc.Pass);
        foreach (var attr in new[] { "str", "dex", "con", "int", "wit", "men" })
            Assert.Equal(Metric(bare, attr), Metric(enc, attr));
        Assert.True(Metric(enc, "pAtk") > Metric(bare, "pAtk"));
        Assert.True(Metric(enc, "pDef") > Metric(bare, "pDef"));
        Assert.True(Metric(enc, "mAtk") > Metric(bare, "mAtk"));
    }

    [Fact]
    public void Weapon_and_heavy_armor_mastery_raise_pAtk_and_pDef_only()
    {
        var bare = ScenarioRunner.Run(Load("stats-human-fighter-40").Scenario,
            Load("stats-human-fighter-40").Oracle, Data, Npcs);
        var passives = ScenarioRunner.Run(Load("stats-human-fighter-40-passives").Scenario,
            Load("stats-human-fighter-40-passives").Oracle, Data, Npcs);
        Assert.True(bare.Pass && passives.Pass);
        foreach (var attr in new[] { "str", "dex", "con", "int", "wit", "men" })
            Assert.Equal(Metric(bare, attr), Metric(passives, attr));
        Assert.True(Metric(passives, "pAtk") > Metric(bare, "pAtk"));
        Assert.True(Metric(passives, "pDef") > Metric(bare, "pDef"));
        Assert.Equal(Metric(bare, "mAtk"), Metric(passives, "mAtk"));
    }

    [Fact]
    public void Level_does_not_change_base_attributes_but_changes_derived_stats_and_vitals()
    {
        var lv40 = ScenarioRunner.Run(Load("stats-human-fighter-40").Scenario,
            Load("stats-human-fighter-40").Oracle, Data, Npcs);
        var lv1 = ScenarioRunner.Run(Load("stats-human-fighter-1").Scenario,
            Load("stats-human-fighter-1").Oracle, Data, Npcs);
        Assert.True(lv40.Pass && lv1.Pass);
        foreach (var attr in new[] { "str", "dex", "con", "int", "wit", "men" })
            Assert.Equal(Metric(lv40, attr), Metric(lv1, attr));
        Assert.True(Metric(lv40, "pAtk") > Metric(lv1, "pAtk"));
        Assert.True(Metric(lv40, "maxHp") > Metric(lv1, "maxHp"));
        Assert.True(Metric(lv40, "maxMp") > Metric(lv1, "maxMp"));
        Assert.True(Metric(lv40, "maxCp") > Metric(lv1, "maxCp"));
    }

    [Fact]
    public void Race_grid_uses_distinct_attribute_lines()
    {
        var hf = ScenarioRunner.Run(Load("stats-human-fighter-40").Scenario,
            Load("stats-human-fighter-40").Oracle, Data, Npcs);
        var of = ScenarioRunner.Run(Load("stats-orc-fighter-40").Scenario,
            Load("stats-orc-fighter-40").Oracle, Data, Npcs);
        var hm = ScenarioRunner.Run(Load("stats-human-mystic-40").Scenario,
            Load("stats-human-mystic-40").Oracle, Data, Npcs);
        Assert.True(hf.Pass && of.Pass && hm.Pass);
        Assert.NotEqual(Metric(hf, "con"), Metric(of, "con"));
        Assert.NotEqual(Metric(hf, "dex"), Metric(of, "dex"));
        Assert.NotEqual(Metric(hf, "int"), Metric(hm, "int"));
        Assert.True(Metric(hm, "mAtk") > Metric(hf, "mAtk"));
    }
}
