using Fineage.Zone.Simulation;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class RegionTickSchedulerTests
{
    const int CellSize = 1000;
    static readonly GridRegionPartition Cells = new(CellSize);

    [Fact]
    public void Region_stage_runs_regions_and_entities_in_ascending_order_whatever_the_collect_order()
    {
        var stage = RecordingStage.At("visit", [(5, 2500, 0), (3, -10, 0), (9, 2100, 0), (1, 10, 900), (7, -10, 50)]);
        var scheduler = Scheduler(SequentialRegionExecutor.Instance);
        scheduler.AddRegionStage(stage);

        scheduler.Step(wallUnixMs: 100);

        Assert.Equal(
            [
                (new RegionId(0, -1, 0), 3), (new RegionId(0, -1, 0), 7),
                (new RegionId(0, 0, 0), 1),
                (new RegionId(0, 2, 0), 5), (new RegionId(0, 2, 0), 9)
            ],
            stage.Visits);
    }

    [Fact]
    public void Instances_are_separate_regions_even_on_the_same_cell()
    {
        var stage = new RecordingStage("visit", [(2, 10, 10, 7), (1, 10, 10, 0)]);
        var scheduler = Scheduler(SequentialRegionExecutor.Instance);
        scheduler.AddRegionStage(stage);

        scheduler.Step(0);

        Assert.Equal([(new RegionId(0, 0, 0), 1), (new RegionId(7, 0, 0), 2)], stage.Visits);
    }

    [Fact]
    public void Stages_run_in_registration_order_and_every_step_samples_one_time()
    {
        var log = new List<string>();
        var scheduler = Scheduler(SequentialRegionExecutor.Instance);
        scheduler.AddWorldStage("first", tick => log.Add($"first@{tick.Tick}:{tick.NowUnixMs}"));
        scheduler.AddRegionStage(new RegionStage("middle",
            (_, work) => work.Add(1, 0, 0, 0),
            (tick, id) => log.Add($"middle@{tick.Tick}:{tick.NowUnixMs}")));
        scheduler.AddWorldStage("last", tick => log.Add($"last@{tick.Tick}:{tick.NowUnixMs}"));

        scheduler.Step(100);
        scheduler.Step(200);

        Assert.Equal(["first", "middle", "last"], scheduler.StageNames);
        Assert.Equal(
            ["first@1:100", "middle@1:100", "last@1:100", "first@2:200", "middle@2:200", "last@2:200"],
            log);
        Assert.Equal(2, scheduler.Tick);
    }

    [Fact]
    public void A_stage_runs_only_on_ticks_that_are_a_multiple_of_its_period()
    {
        var ran = new List<long>();
        var scheduler = Scheduler(SequentialRegionExecutor.Instance);
        scheduler.AddWorldStage("every-third", tick => ran.Add(tick.Tick), periodTicks: 3);

        for (int i = 0; i < 10; i++)
            scheduler.Step(i);

        Assert.Equal([3L, 6L, 9L], ran);
    }

    [Fact]
    public void RunPeriod_runs_only_that_cadence_at_the_current_tick_and_rethrows_failures()
    {
        var ran = new List<string>();
        var observer = new RecordingObserver();
        var scheduler = new RegionTickScheduler(Clock(100), Cells, SequentialRegionExecutor.Instance, observer);
        scheduler.AddWorldStage("fast", tick => ran.Add($"fast@{tick.Tick}:{tick.NowUnixMs}"));
        scheduler.AddWorldStage("slow", tick => ran.Add($"slow@{tick.Tick}"), periodTicks: 3);
        scheduler.AddWorldStage("fast.boom", _ => throw new InvalidOperationException("boom"));
        scheduler.Step(1);

        var error = Assert.Throws<AggregateException>(() => scheduler.RunPeriod(1, 42));

        Assert.Equal(["fast@1:100", "fast@1:100"], ran);
        Assert.Equal(1, scheduler.Tick);
        Assert.Contains("fast.boom", error.Message);
        Assert.Equal(["fast.boom", "fast.boom"], observer.Failures.Select(f => f.Stage));
        Assert.Throws<InvalidOperationException>(() => scheduler.RunPeriod(7, 0));
    }

    [Fact]
    public void Parallel_regions_finish_before_the_next_stage_starts()
    {
        int finished = 0;
        int seenByNext = -1;
        var points = Enumerable.Range(0, 64).Select(i => (i + 1, i * CellSize, 0)).ToArray();
        var scheduler = Scheduler(new ParallelRegionExecutor(8));
        scheduler.AddRegionStage(new RegionStage("work",
            (_, work) =>
            {
                foreach (var (id, x, y) in points)
                    work.Add(id, 0, x, y);
            },
            (_, _) =>
            {
                Thread.SpinWait(2_000);
                Interlocked.Increment(ref finished);
            }));
        scheduler.AddWorldStage("after", _ => seenByNext = Volatile.Read(ref finished));

        scheduler.Step(0);

        Assert.Equal(points.Length, seenByNext);
    }

    /// <summary>
    /// Region-local state driven by per-region randomness: the parallel executor must reach exactly the state the
    /// sequential one does, step after step.
    /// </summary>
    [Fact]
    public void Parallel_and_sequential_executors_reach_identical_state()
    {
        var sequential = RunLocalSimulation(SequentialRegionExecutor.Instance);
        var parallel = RunLocalSimulation(new ParallelRegionExecutor(Environment.ProcessorCount));

        Assert.Equal(sequential, parallel);
    }

    [Fact]
    public void A_failing_region_is_reported_and_the_other_regions_and_stages_still_run()
    {
        var observer = new RecordingObserver();
        var visited = new List<int>();
        bool laterStageRan = false;
        var scheduler = new RegionTickScheduler(Clock(100), Cells, SequentialRegionExecutor.Instance, observer);
        scheduler.AddRegionStage(new RegionStage("fragile",
            (_, work) =>
            {
                work.Add(1, 0, 0, 0);
                work.Add(2, 0, 5_000, 0);
                work.Add(3, 0, 9_000, 0);
            },
            (_, id) =>
            {
                if (id == 2)
                    throw new InvalidOperationException("boom");
                visited.Add(id);
            }));
        scheduler.AddWorldStage("later", _ => laterStageRan = true);

        scheduler.Step(0);

        Assert.Equal([1, 3], visited);
        Assert.True(laterStageRan);
        var failure = Assert.Single(observer.Failures);
        Assert.Equal("fragile", failure.Stage);
        Assert.Equal(new RegionId(0, 5, 0).ToString(), failure.Scope);
        Assert.Equal(1, failure.Tick);
        Assert.Equal("boom", failure.Error.Message);
        Assert.Contains("stage=fragile", failure.ToString());
    }

    [Fact]
    public void A_failing_world_stage_or_collect_is_reported_with_its_scope()
    {
        var observer = new RecordingObserver();
        var scheduler = new RegionTickScheduler(Clock(100), Cells, SequentialRegionExecutor.Instance, observer);
        scheduler.AddWorldStage("global", _ => throw new InvalidOperationException("global down"));
        scheduler.AddRegionStage(new RegionStage("gather",
            (_, work) =>
            {
                work.Add(1, 0, 0, 0);
                throw new InvalidOperationException("gather down");
            },
            (_, _) => throw new InvalidOperationException("must not run after a failed collect")));

        scheduler.Step(0);

        Assert.Equal(
            [("global", "world", "global down"), ("gather", "collect", "gather down")],
            observer.Failures.Select(f => (f.Stage, f.Scope, f.Error.Message)).ToList());
    }

    [Fact]
    public void RunNow_runs_one_stage_without_advancing_and_rethrows_failures()
    {
        var observer = new RecordingObserver();
        var stage = new RegionStage("hook",
            (_, work) => work.Add(1, 0, 0, 0),
            (_, _) => throw new InvalidOperationException("hook down"));
        var scheduler = new RegionTickScheduler(Clock(100), Cells, SequentialRegionExecutor.Instance, observer);
        scheduler.AddRegionStage(stage);

        var error = Assert.Throws<AggregateException>(() => scheduler.RunNow(stage, 0));

        Assert.Equal(0, scheduler.Tick);
        Assert.Equal("hook down", Assert.Single(error.InnerExceptions).Message);
        Assert.Single(observer.Failures);
    }

    [Fact]
    public void RunNow_refuses_a_stage_that_was_never_registered()
    {
        var scheduler = Scheduler(SequentialRegionExecutor.Instance);
        var stranger = RecordingStage.At("stranger", []);

        var error = Assert.Throws<InvalidOperationException>(() => scheduler.RunNow(stranger, 0));
        Assert.Contains("stranger", error.Message);
    }

    [Fact]
    public void Registration_rejects_duplicate_names_and_non_positive_periods()
    {
        var scheduler = Scheduler(SequentialRegionExecutor.Instance);
        scheduler.AddWorldStage("once", _ => { });

        Assert.Throws<InvalidOperationException>(() => scheduler.AddRegionStage(RecordingStage.At("once", [])));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.AddWorldStage("never", _ => { }, periodTicks: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClock(0, () => 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParallelRegionExecutor(0));
    }

    [Fact]
    public void Observer_sees_each_stage_and_the_step_budget()
    {
        var observer = new RecordingObserver();
        var scheduler = new RegionTickScheduler(Clock(250), Cells, SequentialRegionExecutor.Instance, observer);
        scheduler.AddRegionStage(RecordingStage.At("visit", [(1, 0, 0), (2, 5_000, 0), (3, 5_001, 0)]));
        scheduler.AddWorldStage("global", _ => { });

        scheduler.Step(0);

        Assert.Equal([("visit", 2, 3), ("global", 1, 0)], observer.Stages);
        var step = Assert.Single(observer.Steps);
        Assert.Equal(1, step.Tick.Tick);
        Assert.Equal(TimeSpan.FromMilliseconds(250), step.Budget);
    }

    [Fact]
    public void Ticks_count_steps_since_the_epoch_and_stage_time_is_the_tick_time_not_the_wall_sample()
    {
        var seen = new List<TickContext>();
        var scheduler = new RegionTickScheduler(Clock(200, wallUnixMs: 1_000_150), Cells, SequentialRegionExecutor.Instance,
            new RecordingObserver());
        scheduler.AddWorldStage("seen", seen.Add);

        Assert.Equal(5_000, scheduler.Tick);
        Assert.Equal(50, scheduler.MillisecondsUntilNextTick(1_000_150));
        scheduler.Step(1_000_230);

        Assert.Equal(new TickContext(5_001, 1_000_200, 1_000_230), Assert.Single(seen));
    }

    [Fact]
    public void A_late_step_jumps_to_the_wall_tick_reports_the_skip_and_runs_a_crossed_period_once()
    {
        var observer = new RecordingObserver();
        var fast = new List<long>();
        var slow = new List<long>();
        var scheduler = new RegionTickScheduler(Clock(100), Cells, SequentialRegionExecutor.Instance, observer);
        scheduler.AddWorldStage("fast", tick => fast.Add(tick.Tick));
        scheduler.AddWorldStage("slow", tick => slow.Add(tick.Tick), periodTicks: 5);

        scheduler.Step(100);
        scheduler.Step(1_250);
        scheduler.Step(1_260);

        Assert.Equal([1L, 12L, 13L], fast);
        Assert.Equal([12L], slow);
        Assert.Equal([(12L, 10L)], observer.Skipped);
    }

    [Fact]
    public void A_step_while_the_wall_clock_is_behind_still_advances_exactly_one_tick()
    {
        var observer = new RecordingObserver();
        var scheduler = new RegionTickScheduler(Clock(100, wallUnixMs: 1_000), Cells, SequentialRegionExecutor.Instance, observer);
        scheduler.AddWorldStage("noop", _ => { });

        scheduler.Step(500);
        scheduler.Step(500);

        Assert.Equal(12, scheduler.Tick);
        Assert.Equal([(11L, 1_100L, 500L), (12L, 1_200L, 500L)], observer.Steps.Select(s => (s.Tick.Tick, s.Tick.NowUnixMs, s.Tick.WallUnixMs)));
        Assert.Empty(observer.Skipped);
        Assert.Equal(100, scheduler.MillisecondsUntilNextTick(500));
        Assert.Equal(0, scheduler.MillisecondsUntilNextTick(9_999));
    }

    [Fact]
    public void Resume_moves_only_forward_and_runs_nothing()
    {
        var observer = new RecordingObserver();
        int ran = 0;
        var scheduler = new RegionTickScheduler(Clock(100), Cells, SequentialRegionExecutor.Instance, observer);
        scheduler.AddWorldStage("count", _ => ran++);

        scheduler.Resume(5_050);
        scheduler.Resume(1_000);

        Assert.Equal(50, scheduler.Tick);
        Assert.Equal(0, ran);
        Assert.Empty(observer.Skipped);
        Assert.Empty(observer.Steps);
    }

    [Fact]
    public void Hooks_resume_to_the_wall_tick_and_run_at_its_time_without_a_skip_report()
    {
        var observer = new RecordingObserver();
        var seen = new List<TickContext>();
        var scheduler = new RegionTickScheduler(Clock(100), Cells, SequentialRegionExecutor.Instance, observer);
        scheduler.AddWorldStage("seen", seen.Add);

        scheduler.RunPeriod(1, 730);

        Assert.Equal(new TickContext(7, 700, 730), Assert.Single(seen));
        Assert.Equal(7, scheduler.Tick);
        Assert.Empty(observer.Skipped);
    }

    [Fact]
    public void Stage_code_reads_the_pinned_tick_time_and_wall_sample_and_the_pin_ends_with_the_step()
    {
        long wall = 1_234;
        var clock = new SimulationClock(100, () => wall);
        var scheduler = new RegionTickScheduler(clock, Cells, SequentialRegionExecutor.Instance, new RecordingObserver());
        (long Now, long Wall) inside = default;
        long otherThread = 0;
        scheduler.AddWorldStage("read", _ =>
        {
            wall = 1_390;
            inside = (clock.NowUnixMs(), clock.WallUnixMs());
            var thread = new Thread(() => otherThread = clock.WallUnixMs());
            thread.Start();
            thread.Join();
        });

        wall = 1_377;
        scheduler.Step(wall);

        Assert.Equal((1_300L, 1_377L), inside);
        Assert.Equal(1_390, otherThread);
        Assert.Equal(1_390, clock.NowUnixMs());
        Assert.Equal(1_390, clock.WallUnixMs());
    }

    [Fact]
    public void Pins_nest_and_restore_the_outer_time()
    {
        var clock = new SimulationClock(100, () => 9);
        using (clock.Pin(new TickContext(0, 1, 11)))
        {
            using (clock.Pin(new TickContext(0, 2, 12)))
                Assert.Equal((2L, 12L), (clock.NowUnixMs(), clock.WallUnixMs()));
            Assert.Equal((1L, 11L), (clock.NowUnixMs(), clock.WallUnixMs()));
        }

        Assert.Equal((9L, 9L), (clock.NowUnixMs(), clock.WallUnixMs()));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(199, 0)]
    [InlineData(200, 1)]
    [InlineData(-1, -1)]
    [InlineData(-200, -1)]
    [InlineData(-201, -2)]
    public void TickAt_floors_also_before_the_epoch(long unixMs, long tick) =>
        Assert.Equal(tick, new SimulationClock(200, () => 0).TickAt(unixMs));

    static RegionTickScheduler Scheduler(IRegionExecutor executor) =>
        new(Clock(100), Cells, executor, new RecordingObserver());

    static SimulationClock Clock(int stepMs, long wallUnixMs = 0) => new(stepMs, () => wallUnixMs);

    static List<long> RunLocalSimulation(IRegionExecutor executor)
    {
        const int regions = 32;
        const int perRegion = 50;
        var state = new long[regions * perRegion];
        var scheduler = Scheduler(executor);
        scheduler.AddRegionStage(new LocalStage(state, regions, perRegion));
        for (int i = 0; i < 20; i++)
            scheduler.Step(i * 100L);
        return [.. state];
    }

    /// <summary>Each region owns a contiguous slice of <c>state</c> and rolls its own seeded randomness.</summary>
    sealed class LocalStage(long[] state, int regions, int perRegion) : IRegionStage
    {
        public string Name => "local";

        public void Collect(TickContext tick, RegionWorkCollector work)
        {
            for (int id = state.Length - 1; id >= 0; id--)
                work.Add(id, 0, id / perRegion * CellSize, 0);
        }

        public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds)
        {
            var random = new Random(region.X * 1_000_003 + (int)tick.Tick);
            long carry = 0;
            foreach (int id in objectIds)
            {
                carry = carry * 31 + random.Next(1000);
                state[id] = state[id] * 7 + carry;
            }

            Assert.InRange(region.X, 0, regions - 1);
        }
    }

    sealed class RecordingStage : IRegionStage
    {
        readonly (int Id, int X, int Y, int Instance)[] _entities;

        public static RecordingStage At(string name, (int Id, int X, int Y)[] entities) =>
            new(name, entities.Select(e => (e.Id, e.X, e.Y, 0)).ToArray());

        public RecordingStage(string name, (int Id, int X, int Y, int Instance)[] entities)
        {
            Name = name;
            _entities = entities;
        }

        public string Name { get; }
        public List<(RegionId Region, int Id)> Visits { get; } = [];

        public void Collect(TickContext tick, RegionWorkCollector work)
        {
            foreach (var (id, x, y, instance) in _entities)
                work.Add(id, instance, x, y);
        }

        public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds)
        {
            foreach (int id in objectIds)
                Visits.Add((region, id));
        }
    }

    sealed class RecordingObserver : ISimulationObserver
    {
        public List<StageFailure> Failures { get; } = [];
        public List<(string Stage, int Regions, int Entities)> Stages { get; } = [];
        public List<(TickContext Tick, TimeSpan Budget)> Steps { get; } = [];
        public List<(long Tick, long Skipped)> Skipped { get; } = [];

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed) =>
            Stages.Add((stage, regions, entities));

        public void StageFailed(StageFailure failure) => Failures.Add(failure);

        public void TicksSkipped(TickContext tick, long skipped) => Skipped.Add((tick.Tick, skipped));

        public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget) => Steps.Add((tick, budget));
    }
}

public sealed class GridRegionPartitionTests
{
    [Theory]
    [InlineData(83_400, 148_000, 22, 22)]     // Giran
    [InlineData(-84_000, 243_000, 17, 25)]    // Talking Island
    [InlineData(46_045, 41_251, 21, 19)]      // Elven Village
    [InlineData(0, 0, 20, 18)]
    [InlineData(-1, -1, 19, 17)]
    [InlineData(32_767, 32_768, 20, 19)]
    public void Map_tiles_match_interlude_geodata_tile_names(int x, int y, int tileX, int tileY) =>
        Assert.Equal(new RegionId(0, tileX, tileY), GridRegionPartition.MapTiles.RegionOf(0, x, y));

    [Fact]
    public void Negative_coordinates_floor_instead_of_truncating_toward_zero()
    {
        var cells = new GridRegionPartition(1000);
        Assert.Equal(new RegionId(0, -1, -2), cells.RegionOf(0, -1, -1001));
        Assert.Equal(new RegionId(0, -1, -1), cells.RegionOf(0, -1000, -1000));
        Assert.Equal(new RegionId(0, 0, 0), cells.RegionOf(0, 999, 0));
    }

    [Fact]
    public void Cell_size_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridRegionPartition(0));

    [Fact]
    public void Region_order_is_instance_then_x_then_y()
    {
        var ids = new[] { new RegionId(1, 0, 0), new RegionId(0, 2, 1), new RegionId(0, 2, 0), new RegionId(0, -3, 9) };
        Array.Sort(ids);
        Assert.Equal([new RegionId(0, -3, 9), new RegionId(0, 2, 0), new RegionId(0, 2, 1), new RegionId(1, 0, 0)], ids);
    }
}
