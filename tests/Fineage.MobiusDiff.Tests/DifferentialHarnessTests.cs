using Fineage.Game;
using Fineage.MobiusDiff;
using Fineage.Zone.Npcs;
using Xunit;

namespace Fineage.MobiusDiff.Tests;

public sealed class DifferentialHarnessTests
{
    static readonly string Root = RepoPaths.FindRoot();
    static readonly GameData Data = GameData.LoadDefault();
    static readonly NpcCatalog Npcs = NpcCatalog.LoadDefault();

    static (Scenario Scenario, OracleFile Oracle) Load(string id)
    {
        string scenarioPath = Path.Combine(Root, "tools", "Fineage.MobiusDiff", "scenarios", id + ".json");
        var scenario = ScenarioJson.Load(scenarioPath);
        string oraclePath = Path.Combine(Root, scenario.Oracle.Golden);
        return (scenario, ScenarioJson.LoadOracle(oraclePath));
    }

    [Fact]
    public void Passing_dev_case_reports_pass()
    {
        var (scenario, oracle) = Load("physical-stats-human-fighter-40");
        var report = ScenarioRunner.Run(scenario, oracle, Data, Npcs);

        Assert.True(report.Pass, string.Join("; ", report.Rows.Where(r => !r.Pass).Select(r => $"{r.Parameter} {r.Actual} != {r.Expected}")));
        Assert.All(report.Rows, row => Assert.True(row.Pass, row.Parameter));
    }

    [Fact]
    public void Negative_control_mutation_makes_the_comparator_fail()
    {
        var (scenario, oracle) = Load("physical-stats-human-fighter-40");

        // Test-only mutation of the Fineage-side metric. No gameplay code is changed or committed.
        var report = ScenarioRunner.Run(scenario, oracle, Data, Npcs, actual => actual["pAtk"] *= 1.5);

        Assert.False(report.Pass);
        var patk = Assert.Single(report.Rows, row => row.Parameter == "pAtk");
        Assert.False(patk.Pass);
        Assert.True(patk.Delta > 0);
    }

    [Fact]
    public void Pilot_physical_attack_uses_the_ct0_76_proximity_core()
    {
        var (scenario, oracle) = Load("physical-attack-human-fighter-40");
        var report = ScenarioRunner.Run(scenario, oracle, Data, Npcs);
        var patk = Assert.Single(report.Rows, row => row.Parameter == "pAtk");
        var damage = Assert.Single(report.Rows, row => row.Parameter == "damageNoCrit");

        Assert.True(patk.Pass, $"pAtk {patk.Actual} != {patk.Expected}");
        Assert.True(Assert.Single(report.Rows, row => row.Parameter == "critPermille").Pass);
        Assert.True(Assert.Single(report.Rows, row => row.Parameter == "hitChancePermille").Pass);
        Assert.Contains("76", damage.Note);

        // The coefficient now matches CT0. Remaining delta is the P.Atk pipeline
        // (Fineage ~42 vs Mobius 42.7248, inside the pAtk tolerance of 1, outside damage's 0.5).
        var npc = Npcs.Get(scenario.Target.NpcId);
        Assert.Equal(Fineage.Zone.Combat.ZoneCombat.PhysicalDamage(patk.Actual, npc.PDef), (int)damage.Actual);
        Assert.Equal(73, (int)damage.Expected);
        Assert.Equal(73, Fineage.Zone.Combat.ZoneCombat.PhysicalDamage(patk.Expected, npc.PDef));
    }

    [Fact]
    public void Same_seed_produces_the_same_sample_mean()
    {
        var (scenario, oracle) = Load("physical-attack-human-fighter-40");
        var first = FineageProbe.Measure(scenario, Data, Npcs);
        var second = FineageProbe.Measure(scenario, Data, Npcs);
        Assert.Equal(first.Keys.Order(), second.Keys.Order());
        foreach (var key in first.Keys)
            Assert.Equal(first[key], second[key]);
    }

    [Fact]
    public void Cp_barrier_pilot_matches_the_playerstatus_golden()
    {
        var (scenario, oracle) = Load("cp-reducehp-human-fighter-40");
        var report = ScenarioRunner.Run(scenario, oracle, Data, Npcs);

        Assert.True(report.Pass, string.Join("; ", report.Rows.Where(r => !r.Pass).Select(r => $"{r.Parameter} {r.Actual} != {r.Expected}")));
        Assert.Equal(new Dictionary<string, double>
        {
            ["cpLoss_fullAp"] = 150,
            ["hpLoss_fullAp"] = 0,
            ["cpLoss_overflow"] = 40,
            ["hpLoss_overflow"] = 60,
            ["cpLoss_npcBypass"] = 0,
            ["hpLoss_npcBypass"] = 100,
            ["cpLoss_explicitIgnore"] = 0,
            ["hpLoss_explicitIgnore"] = 100
        }, report.Rows.ToDictionary(r => r.Parameter, r => r.Expected));
    }

    [Fact]
    public void Cp_barrier_comparator_can_fail_on_a_mutated_actual()
    {
        var (scenario, oracle) = Load("cp-reducehp-human-fighter-40");
        var report = ScenarioRunner.Run(scenario, oracle, Data, Npcs, actual => actual["cpLoss_fullAp"] = 0);
        Assert.False(report.Pass);
        Assert.False(Assert.Single(report.Rows, r => r.Parameter == "cpLoss_fullAp").Pass);
    }
}
