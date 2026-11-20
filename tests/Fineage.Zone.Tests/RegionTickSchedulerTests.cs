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

        scheduler.Step(nowUnixMs: 1_000);

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
    public void StepCombat_runs_only_period_one_stages()
    {
        var ran = new List<string>();
        var scheduler = Scheduler(SequentialRegionExecutor.Instance);
        scheduler.AddWorldStage("fast", tick => ran.Add($"fast@{tick.Tick}"));
        scheduler.AddWorldStage("slow", tick => ran.Add($"slow@{tick.Tick}"), periodTicks: 3);

        scheduler.StepCombat(0);
        scheduler.StepCombat(0);

        Assert.Equal(["fast@1", "fast@2"], ran);
        Assert.Equal(2, scheduler.Tick);
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
        var scheduler = new RegionTickScheduler(100, Cells, SequentialRegionExecutor.Instance, observer);
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
        var scheduler = new RegionTickScheduler(100, Cells, SequentialRegionExecutor.Instance, observer);
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
        var scheduler = new RegionTickScheduler(100, Cells, SequentialRegionExecutor.Instance, observer);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionTickScheduler(0, Cells, SequentialRegionExecutor.Instance, new RecordingObserver()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParallelRegionExecutor(0));
    }

    [Fact]
    public void Observer_sees_each_stage_and_the_step_budget()
    {
        var observer = new RecordingObserver();
        var scheduler = new RegionTickScheduler(250, Cells, SequentialRegionExecutor.Instance, observer);
        scheduler.AddRegionStage(RecordingStage.At("visit", [(1, 0, 0), (2, 5_000, 0), (3, 5_001, 0)]));
        scheduler.AddWorldStage("global", _ => { });

        scheduler.Step(0);

        Assert.Equal([("visit", 2, 3), ("global", 1, 0)], observer.Stages);
        var step = Assert.Single(observer.Steps);
        Assert.Equal(1, step.Tick.Tick);
        Assert.Equal(TimeSpan.FromMilliseconds(250), step.Budget);
    }

    static RegionTickScheduler Scheduler(IRegionExecutor executor) =>
        new(100, Cells, executor, new RecordingObserver());

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

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed) =>
            Stages.Add((stage, regions, entities));

        public void StageFailed(StageFailure failure) => Failures.Add(failure);

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
