using Fineage.MobiusDiff;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Game;
using Xunit;

namespace Fineage.MobiusDiff.Tests;

/// <summary>
/// Differential golden matrix for plain physical auto-attack (#979). Every case is driven by a
/// Java-generated oracle traced to Mobius CT0 <c>Formulas.calcPhysDam</c> / <c>Creature</c>.
/// </summary>
public sealed class PhysAutoMatrixTests
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
                "phys-auto-p2m-sword",
                "phys-auto-p2m-blunt",
                "phys-auto-p2m-dagger",
                "phys-auto-p2m-pole",
                "phys-auto-p2m-bow",
                "phys-auto-p2m-fist",
                "phys-auto-p2m-dual",
                "phys-auto-p2m-bigsword",
                "phys-auto-p2m-bigblunt",
                "phys-auto-p2p-sword-shield",
                "phys-auto-m2p-gremlin",
                "phys-auto-s2m-kat",
                "phys-auto-m2s-gremlin-kat",
                "phys-auto-p2s-sword-kat",
                "phys-auto-p2m-sword-lv1",
                "phys-auto-p2m-sword-alligator"
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

    [Theory]
    [MemberData(nameof(MatrixScenarioIds))]
    public void Matrix_scenario_matches_the_mobius_formula_oracle(string id)
    {
        var (scenario, oracle) = Load(id);
        var report = ScenarioRunner.Run(scenario, oracle, Data, Npcs);
        Assert.True(report.Pass, string.Join("; ", report.Rows.Where(r => !r.Pass).Select(r => $"{r.Parameter} {r.Actual} != {r.Expected}")));
    }

    [Fact]
    public void Negative_control_mutation_fails_a_matrix_cell()
    {
        var (scenario, oracle) = Load("phys-auto-p2m-sword");
        var report = ScenarioRunner.Run(scenario, oracle, Data, Npcs, actual => actual["damageFront"] += 10);
        Assert.False(report.Pass);
        Assert.False(Assert.Single(report.Rows, r => r.Parameter == "damageFront").Pass);
    }

    [Fact]
    public void Bow_reuse_is_the_ct0_default_1500_times_333_over_pspd()
    {
        var (scenario, oracle) = Load("phys-auto-p2m-bow");
        var report = ScenarioRunner.Run(scenario, oracle, Data, Npcs);
        Assert.True(report.Pass);
        double pAtkSpd = Assert.Single(report.Rows, r => r.Parameter == "pAtkSpd").Expected;
        int reuse = (int)Assert.Single(report.Rows, r => r.Parameter == "bowReuseMs").Expected;
        int timeAtk = (int)Assert.Single(report.Rows, r => r.Parameter == "attackTimeMs").Expected;
        int interval = (int)Assert.Single(report.Rows, r => r.Parameter == "attackIntervalMs").Expected;
        Assert.Equal(ZoneCombat.WeaponReuseMs(ZoneCombat.DefaultBowReuseDelayMs, pAtkSpd), reuse);
        Assert.Equal(timeAtk + reuse, interval);
        Assert.Equal(500, (int)Assert.Single(report.Rows, r => r.Parameter == "bowRange").Expected);
    }

    [Fact]
    public void Player_target_spends_cp_for_a_playable_attacker_and_bypasses_it_for_an_npc()
    {
        var pvp = ScenarioRunner.Run(Load("phys-auto-p2p-sword-shield").Scenario,
            Load("phys-auto-p2p-sword-shield").Oracle, Data, Npcs);
        Assert.True(pvp.Pass);
        Assert.Equal(1, Assert.Single(pvp.Rows, r => r.Parameter == "pvp").Expected);
        Assert.Equal(9, Assert.Single(pvp.Rows, r => r.Parameter == "cpLoss_front").Expected);
        Assert.Equal(0, Assert.Single(pvp.Rows, r => r.Parameter == "hpLoss_front").Expected);
        Assert.Equal(0, Assert.Single(pvp.Rows, r => r.Parameter == "cpLoss_npcBypass").Expected);
        Assert.Equal(9, Assert.Single(pvp.Rows, r => r.Parameter == "hpLoss_npcBypass").Expected);

        var mob = ScenarioRunner.Run(Load("phys-auto-m2p-gremlin").Scenario,
            Load("phys-auto-m2p-gremlin").Oracle, Data, Npcs);
        Assert.True(mob.Pass);
        Assert.Equal(0, Assert.Single(mob.Rows, r => r.Parameter == "pvp").Expected);
        Assert.Equal(0, Assert.Single(mob.Rows, r => r.Parameter == "cpLoss_front").Expected);
        Assert.Equal(2, Assert.Single(mob.Rows, r => r.Parameter == "hpLoss_front").Expected);
    }

    [Fact]
    public void Level_gap_changes_hit_chance_but_not_the_76_proximity_core()
    {
        var lv40 = ScenarioRunner.Run(Load("phys-auto-p2m-sword").Scenario,
            Load("phys-auto-p2m-sword").Oracle, Data, Npcs);
        var lv1 = ScenarioRunner.Run(Load("phys-auto-p2m-sword-lv1").Scenario,
            Load("phys-auto-p2m-sword-lv1").Oracle, Data, Npcs);
        var sameLevelMob = ScenarioRunner.Run(Load("phys-auto-p2m-sword-alligator").Scenario,
            Load("phys-auto-p2m-sword-alligator").Oracle, Data, Npcs);
        Assert.True(lv40.Pass && lv1.Pass && sameLevelMob.Pass);
        int hit40v1 = (int)Assert.Single(lv40.Rows, r => r.Parameter == "hitChanceFront").Expected;
        int hit1v1 = (int)Assert.Single(lv1.Rows, r => r.Parameter == "hitChanceFront").Expected;
        int hit40v40 = (int)Assert.Single(sameLevelMob.Rows, r => r.Parameter == "hitChanceFront").Expected;
        Assert.Equal(980, hit40v1);
        Assert.Equal(hit1v1, hit40v40);
        Assert.True(hit1v1 < hit40v1);
    }
}
