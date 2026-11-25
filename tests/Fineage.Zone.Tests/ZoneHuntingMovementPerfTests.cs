using Fineage.Zone.Interest;
using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;
using Xunit.Abstractions;

namespace Fineage.Zone.Tests;

/// <summary>
/// One player in a populated hunting ground (Giran Death Pass area) with the full enabled spawn set;
/// movement.npc must stay within budget while random-walk and chase are active nearby.
/// </summary>
[Collection("Zone simulation perf")]
[Trait("Category", "perf")]
public sealed class ZoneHuntingMovementPerfTests(ITestOutputHelper output)
{
    public const int HuntingX = 66_400;
    public const int HuntingY = 106_500;
    public const int HuntingZ = -3732;

    [Fact]
    public async Task One_player_hunting_zone_movement_npc_stays_within_budget()
    {
        var observer = new HuntingSubsystemTimingObserver();
        var clock = new MutableClock { Now = ManualSimulationDriver.FixedStartUtc };
        var bench = new WorldBench(
            clock: () => clock.Now,
            aoi: new GridAoi(GridAoi.DefaultCellSize),
            includeEnabledMaps: true,
            simulationObserver: observer,
            simulationSeed: 77,
            allowAmbientBoats: false,
            combatRandom: new Random(77));
        await bench.EnterAsync(x: HuntingX, y: HuntingY, z: HuntingZ, clearRecorder: true);
        bench.World.RunNpcThinkTick(clock.Now.ToUnixTimeMilliseconds());
        var driver = new ManualSimulationDriver(bench.World, clock);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int warmup = 120;
        driver.Step(warmup);

        observer.Clear();
        const int samples = 300;
        driver.Step(samples);

        double moveNpcP99 = observer.Percentile("movement.npc", 0.99);
        double moveNpcP50 = observer.Percentile("movement.npc", 0.5);
        double totalP99 = observer.StepPercentile(0.99);
        double totalP50 = observer.StepPercentile(0.5);
        double randomWalkP99 = observer.Percentile("random-walk", 0.99);
        var (totalNpcs, thinking, regions) = bench.World.TickNpcDiagnostics();
        output.WriteLine(
            $"hunting (n={samples}, npcs={totalNpcs}, thinking={thinking}, playerRegions={regions}): " +
            $"total p50={totalP50:F2} p99={totalP99:F2} ms; movement.npc p50={moveNpcP50:F2} p99={moveNpcP99:F2} ms; random-walk p99={randomWalkP99:F2} ms");

#if DEBUG
        const double moveNpcBudget = 15;
        const double totalBudget = 50;
#else
        const double moveNpcBudget = 5;
        const double totalBudget = 20;
#endif
        Assert.True(moveNpcP99 < moveNpcBudget, $"movement.npc p99={moveNpcP99:F1} ms (budget {moveNpcBudget})");
        Assert.True(totalP99 < totalBudget, $"tick total p99={totalP99:F1} ms (budget {totalBudget})");
        Assert.True(thinking > 0, "hunting ground near the player should activate some NPC think set members");
    }

    sealed class HuntingSubsystemTimingObserver : ISimulationObserver
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

        public void TicksSkipped(TickContext tick, long skipped) { }

        public void EntitiesHandedOff(TickContext tick, IReadOnlyList<RegionTransfer> transfers) { }

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