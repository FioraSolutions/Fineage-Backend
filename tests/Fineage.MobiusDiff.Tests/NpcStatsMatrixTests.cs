using Fineage.Game;
using Fineage.Zone.Npcs;
using Xunit;

namespace Fineage.MobiusDiff.Tests;

/// <summary>
/// Differential golden matrix for the Mobius NPC stat fold (#979 NPC-stat slice). The Java driver reads the Mobius
/// <c>stats/npcs</c> XML directly (never the Fineage JSON) and applies <c>NPC_STD_CALCULATOR</c>: int-truncated base
/// P.Atk/P.Def/M.Atk/M.Def, float HP/MP, STR/CON/INT/MEN/DEX bonuses, <c>levelMod = (level+89)/100</c>,
/// rounded P.Atk.Spd and the non-player accuracy/evasion formulas.
/// </summary>
public sealed class NpcStatsMatrixTests
{
    static readonly string Root = RepoPaths.FindRoot();
    static readonly GameData Data = GameData.LoadDefault();
    static readonly NpcCatalog Npcs = NpcCatalog.LoadDefault();

    // id -> (scenario, npcId). The catalog template must carry the oracle's folded numbers.
    public static TheoryData<string, int> Cases => new()
    {
        { "npc-stats-gremlin-l1", 20001 },
        { "npc-stats-ant-recruit-l33", 20082 },
        { "npc-stats-alligator-l40", 20135 },
        { "npc-stats-validus-l46", 20219 },
        { "npc-stats-hall-keeper-captain-l83", 18329 },
        { "npc-stats-greyclaw-kutus-raid-l23", 25001 },
        { "npc-stats-turek-archer-int41-l29", 25005 },
        { "npc-stats-doll-blader-men25-l58", 29023 },
    };

    static (Scenario Scenario, OracleFile Oracle) Load(string id)
    {
        var scenario = ScenarioJson.Load(Path.Combine(Root, "tools", "Fineage.MobiusDiff", "scenarios", id + ".json"));
        return (scenario, ScenarioJson.LoadOracle(Path.Combine(Root, scenario.Oracle.Golden)));
    }

    static DiffReport Run(string id, Action<ActualMetrics>? mutate = null)
    {
        var (scenario, oracle) = Load(id);
        return ScenarioRunner.Run(scenario, oracle, Data, Npcs, mutate);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Npc_stats_scenario_matches_the_mobius_formula_oracle(string id, int npcId)
    {
        _ = npcId;
        var report = Run(id);
        Assert.True(report.Pass, string.Join("; ", report.Rows.Where(r => !r.Pass).Select(r => $"{r.Parameter} {r.Actual} != {r.Expected}")));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Catalog_template_carries_the_oracle_folded_stats(string id, int npcId)
    {
        var (_, oracle) = Load(id);
        var m = oracle.Metrics;
        var t = Npcs.Get(npcId);
        Assert.Equal(m["pAtk"], t.PAtk, 2);
        Assert.Equal(m["pDef"], t.PDef, 2);
        Assert.Equal(m["mAtk"], t.MAtk, 2);
        Assert.Equal(m["mDef"], t.MDef, 2);
        Assert.Equal(m["pAtkSpd"], t.AttackSpeed);
        Assert.Equal(m["maxHp"], t.Hp);
        Assert.Equal(m["maxMp"], t.Mp);
    }

    // Literal Java-traced values, so the expectations survive an accidental oracle refresh against a bad driver.
    [Theory]
    [InlineData(20001, 62, 44, 8.64, 39.6, 278)]          // Gremlin L1: was 40 HP / 8.475 P.Atk / 40 P.Def
    [InlineData(20135, 1527, 531, 272.448, 201.24, 278)]  // Alligator L40
    [InlineData(20219, 1943, 707, 401.76, 245.7, 278)]    // Validus L46
    [InlineData(18329, 6060, 2168, 2268.336, 607.16, 278)] // Hall Keeper Captain L83
    [InlineData(25001, 95986, 514, 168.7392, 320.32, 410)] // Greyclaw Kutus (raid, non-default DEX)
    public void Common_npcs_carry_hand_traced_mobius_numbers(int npcId, int hp, int mp, double pAtk, double pDef, int pAtkSpd)
    {
        var t = Npcs.Get(npcId);
        Assert.Equal(hp, t.Hp);
        Assert.Equal(mp, t.Mp);
        Assert.Equal(pAtk, t.PAtk, 3);
        Assert.Equal(pDef, t.PDef, 3);
        Assert.Equal(pAtkSpd, t.AttackSpeed);
    }

    [Fact]
    public void Gremlin_p_def_is_44_times_level_mod_0_9_not_the_raw_xml_value()
    {
        // Gremlin XML pDef 44.44 truncates to 44 and levelMod(1) is 0.9 (44 * 0.9 = 39.6).
        Assert.Equal(39.6, NpcCatalog.FoldPDef(44.44, 1), 6);
        Assert.Equal(0.9, NpcCatalog.LevelMod(1), 9);
    }

    [Fact]
    public void Fold_truncates_integer_base_stats_before_multiplying()
    {
        Assert.Equal(44 * 0.9, NpcCatalog.FoldPDef(44.99, 1), 9);
        Assert.Equal(62, NpcCatalog.FoldVital(62.4, 1.0)); // float HP, int-cast after the multiplier
        Assert.Equal(278, NpcCatalog.FoldAttackSpeed(253, 1.1));
    }

    [Theory]
    [InlineData("pAtk")]
    [InlineData("pDef")]
    [InlineData("maxHp")]
    [InlineData("maxMp")]
    [InlineData("pAtkSpd")]
    [InlineData("evasion")]
    public void Negative_control_mutation_fails_a_npc_stat_cell(string metric)
    {
        var report = Run("npc-stats-validus-l46", actual => actual[metric] += 1);
        Assert.False(report.Pass);
        Assert.False(Assert.Single(report.Rows, r => r.Parameter == metric).Pass);
    }
}
