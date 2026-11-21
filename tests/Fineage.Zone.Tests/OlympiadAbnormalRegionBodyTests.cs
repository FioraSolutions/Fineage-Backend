using Fineage.Protocol;
using Fineage.Zone.Olympiad;
using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P0-2: abnormals region bodies fan out user info through stats; they must not touch the Olympiad actor.</summary>
public sealed class OlympiadAbnormalRegionBodyTests
{
    static readonly DateTimeOffset Evening = new(2026, 10, 28, 18, 30, 0, TimeSpan.Zero);
    const int Duelist = 88, WindWalk = 1204;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timed_buff_expiry_in_olympiad_arena_fans_out_user_info(bool parallel)
    {
        var observer = new RecordingObserver();
        IRegionExecutor executor = parallel ? new ParallelRegionExecutor(4) : SequentialRegionExecutor.Instance;
        var clock = Evening;
        var bench = new WorldBench(
            clock: () => clock,
            olympiadSettings: new OlympiadSettings { ClassedMin = 2, WaitSeconds = 5, EnchantLimit = 6 },
            regionExecutor: executor,
            simulationObserver: observer);
        var one = await bench.EnterAsync("arena1", "One", classId: Duelist, level: 78);
        var two = await bench.EnterAsync("arena2", "Two", classId: Duelist, level: 78);
        bench.World.TrySetNoble(one.ObjectId, true);
        bench.World.TrySetNoble(two.ObjectId, true);
        Assert.Null(await bench.World.TryOlympiadRegisterAsync(one.Session, new OlympiadRegisterRequestMsg { Classed = 1 },
            CancellationToken.None));
        Assert.Null(await bench.World.TryOlympiadRegisterAsync(two.Session, new OlympiadRegisterRequestMsg { Classed = 1 },
            CancellationToken.None));

        async Task AdvanceAsync(DateTimeOffset when)
        {
            clock = when;
            await bench.World.AdvanceOlympiadAsync(CancellationToken.None);
        }

        await AdvanceAsync(Evening.AddSeconds(61));
        await AdvanceAsync(Evening.AddSeconds(66));
        Assert.True(bench.World.InOlympiadArena(one.ObjectId));

        var wind = bench.Game.Skills.Find(WindWalk) ?? throw new InvalidOperationException($"skill {WindWalk} missing");
        Assert.True(wind.TryLevelData(1, out var windLevel));
        Assert.True(bench.World.ApplyAbnormalFromSkillForTest(wind, windLevel, one.Player));

        clock = clock.AddSeconds(wind.AbnormalTimeSeconds + 1);
        for (int step = 0; step < 6; step++)
            bench.World.StepSimulation();

        Assert.Empty(observer.Failures);
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutUserInfo)));
    }

    sealed class RecordingObserver : ISimulationObserver
    {
        public List<StageFailure> Failures { get; } = [];

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed)
        {
        }

        public void StageFailed(StageFailure failure) => Failures.Add(failure);

        public void TicksSkipped(TickContext tick, long skipped)
        {
        }

        public void EntitiesHandedOff(TickContext tick, IReadOnlyList<RegionTransfer> transfers)
        {
        }

        public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget)
        {
        }
    }
}