using Fineage.LoadTest.Consistency;
using Xunit;

namespace Fineage.LoadTest.Tests;

/// <summary>
/// <c>extends</c> merging of <c>actionWeights</c> and the reject classes of the economy oracle (#965).
/// </summary>
public sealed class ScenarioMergeTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("scenario-merge").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Write(string name, string yaml)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, yaml);
        return path;
    }

    [Fact]
    public void Child_that_declares_action_weights_replaces_the_parent_mix()
    {
        Write("base.yaml", "name: base\nactionWeights:\n  move: 0.5\n  attack: 0.5\n");
        var child = ScenarioLoader.Load(Write("child.yaml", "name: child\nextends: base.yaml\nactionWeights:\n  say: 1\n"));
        Assert.Equal(["say"], child.ActionWeights.Keys);
    }

    [Fact]
    public void Child_without_action_weights_inherits_the_parent_mix_not_the_class_default()
    {
        Write("base.yaml", "name: base\nactionWeights:\n  move: 0.5\n  sit: 0.5\n");
        var child = ScenarioLoader.Load(Write("child.yaml", "name: child\nextends: base.yaml\nbotCount: 3\n"));
        Assert.Equal(["move", "sit"], child.ActionWeights.Keys.Order());
    }

    [Fact]
    public void Combined_scenario_runs_the_hammer_mix_without_legacy_combat_actions()
    {
        var s = ScenarioLoader.Load(Path.Combine(HeavyPlayRolesTests.ScenarioDir(), "perf-aoe-combined.yaml"));
        Assert.True(s.Economy is { Enabled: true });
        Assert.Contains("economy_shop_buy", s.ActionWeights.Keys);
        Assert.DoesNotContain("skill", s.ActionWeights.Keys);
        Assert.DoesNotContain("attack", s.ActionWeights.Keys);
        Assert.DoesNotContain("pickup", s.ActionWeights.Keys);
    }

    [Fact]
    public void Unknown_action_in_a_structured_scenario_fails_to_load()
    {
        Write("base.yaml", "name: base\neconomy:\n  enabled: true\nactionWeights:\n  economy_teleport: 1\n");
        var ex = Assert.Throws<InvalidOperationException>(() => ScenarioLoader.Load(Path.Combine(_dir, "base.yaml")));
        Assert.Contains("economy_teleport", ex.Message);
    }

    [Theory]
    [InlineData("party_busy", EconomyConsistencyOracle.RejectClass.ExpectedDomain)]
    [InlineData("trade_busy", EconomyConsistencyOracle.RejectClass.ExpectedDomain)]
    [InlineData("trade_range", EconomyConsistencyOracle.RejectClass.ExpectedDomain)]
    [InlineData("pickup", EconomyConsistencyOracle.RejectClass.ExpectedDomain)]
    [InlineData("adena", EconomyConsistencyOracle.RejectClass.Explained)]
    [InlineData("skill_unknown", EconomyConsistencyOracle.RejectClass.Unexpected)]
    [InlineData("unknown_opcode", EconomyConsistencyOracle.RejectClass.Unexpected)]
    public void Reject_codes_are_classified(string code, EconomyConsistencyOracle.RejectClass expected) =>
        Assert.Equal(expected, EconomyConsistencyOracle.Classify(code));
}
