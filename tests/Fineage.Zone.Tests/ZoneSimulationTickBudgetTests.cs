using System.Diagnostics;
using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;
using Xunit.Abstractions;

namespace Fineage.Zone.Tests;

/// <summary>
/// Whole-world spawn with no players: combat-step subsystem times must stay within idle budgets (regression guard).
/// </summary>
[Collection("Zone simulation perf")]
public sealed class ZoneSimulationTickBudgetTests(ITestOutputHelper output)
{
    static void DisableAmbientBoats() => Environment.SetEnvironmentVariable("FINEAGE_ALLOW_BOATS", "false");

    [Fact]
    public void Idle_full_world_step_keeps_subsystem_times_within_budget()
    {
        DisableAmbientBoats();
        var observer = new SubsystemTimingObserver();
        var bench = new WorldBench(includeEnabledMaps: true, simulationObserver: observer, combatRandom: new MissRandomWalk());
        Assert.Equal(0, bench.World.ActiveSetCounts.Thinking);

        for (int i = 0; i < 40; i++)
            bench.World.StepSimulation();

        observer.Clear();
        const int samples = 50;
        for (int i = 0; i < samples; i++)
            bench.World.StepSimulation();

        double randomWalkP99 = observer.Percentile("random-walk", 0.99);
        double handoffP99 = observer.Percentile(ZoneWorld.HandoffStage, 0.99);
        double totalP99 = observer.StepPercentile(0.99);

        Assert.Equal(0, bench.World.ActiveSetCounts.Thinking);
        output.WriteLine(
            $"idle full-world (n={samples}): total p99={totalP99:F2} ms; random-walk p99={randomWalkP99:F2} ms; handoff p99={handoffP99:F2} ms; npcs={bench.World.TickNpcDiagnostics().TotalNpcs}");
        Assert.True(randomWalkP99 < 15, $"random-walk p99={randomWalkP99:F1} ms");
        Assert.True(handoffP99 < 15, $"handoff p99={handoffP99:F1} ms");
        Assert.True(totalP99 < 50, $"total step p99={totalP99:F1} ms; random-walk p99={randomWalkP99:F1} ms; handoff p99={handoffP99:F1} ms");
    }

    /// <inheritdoc cref="ZoneActiveSetsTests.MissRandomWalk"/>
    sealed class MissRandomWalk : Random
    {
        readonly Random _inner = new(1);

        public override int Next(int maxValue) => maxValue == 30 ? 1 : _inner.Next(maxValue);
    }

    sealed class SubsystemTimingObserver : ISimulationObserver
    {
        readonly Dictionary<string, List<double>> _stages = new(StringComparer.Ordinal);
        readonly List<double> _steps = [];

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed)
        {
            if (!_stages.TryGetValue(stage, out var list))
            {
                list = [];
                _stages[stage] = list;
            }

            list.Add(elapsed.TotalMilliseconds);
        }

        public void StageFailed(StageFailure failure) =>
            throw new InvalidOperationException($"stage failed: {failure}");

        public void TicksSkipped(TickContext tick, long skipped)
        {
        }

        public void EntitiesHandedOff(TickContext tick, IReadOnlyList<RegionTransfer> transfers)
        {
        }

        public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget) =>
            _steps.Add(elapsed.TotalMilliseconds);

        public void Clear()
        {
            _stages.Clear();
            _steps.Clear();
        }

        public double Percentile(string stage, double p)
        {
            if (!_stages.TryGetValue(stage, out var list) || list.Count == 0)
                return 0;
            list.Sort();
            int index = (int)Math.Clamp(Math.Ceiling(p * list.Count) - 1, 0, list.Count - 1);
            return list[index];
        }

        public double StepPercentile(double p)
        {
            if (_steps.Count == 0)
                return 0;
            var list = _steps.ToList();
            list.Sort();
            int index = (int)Math.Clamp(Math.Ceiling(p * list.Count) - 1, 0, list.Count - 1);
            return list[index];
        }
    }
}