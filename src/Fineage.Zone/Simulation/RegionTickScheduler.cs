using System.Diagnostics;

namespace Fineage.Zone.Simulation;

/// <summary>
/// Fixed-step simulation clock. Each <see cref="Step"/> moves <see cref="Tick"/> to the next tick (or, when the caller
/// ran late, to the tick the wall clock is in, reporting the skipped ones) and runs every due stage in registration
/// order, with a barrier between stages. A stage with period <c>p</c> is due when the step crosses a multiple of
/// <c>p</c>, so skipped ticks coalesce into one run instead of losing it. A world stage runs once; a region stage
/// collects its entities into regions and runs each region through the <see cref="IRegionExecutor"/> in ascending
/// <see cref="RegionId"/> order. A throwing stage or region is reported to the <see cref="ISimulationObserver"/> and the
/// rest of the step still runs. Not thread-safe: one caller drives the clock.
/// </summary>
public sealed class RegionTickScheduler
{
    readonly List<ScheduledStage> _stages = [];
    readonly HashSet<string> _names = new(StringComparer.Ordinal);
    readonly RegionWorkCollector _collector;
    readonly IRegionExecutor _executor;
    readonly ISimulationObserver _observer;

    /// <summary>Starts at the tick the clock's wall time is in; the first <see cref="Step"/> runs the tick after it.</summary>
    public RegionTickScheduler(
        SimulationClock clock,
        IRegionPartition partition,
        IRegionExecutor executor,
        ISimulationObserver observer)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _collector = new RegionWorkCollector(partition);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        Tick = clock.TickAt(clock.WallUnixMs());
    }

    public SimulationClock Clock { get; }

    public int StepMilliseconds => Clock.StepMilliseconds;

    /// <summary>The last tick the clock reached. Its time is <see cref="SimulationClock.TimeOf"/>(Tick).</summary>
    public long Tick { get; private set; }

    public IReadOnlyList<string> StageNames => _stages.Select(s => s.Name).ToList();

    /// <summary>A single-actor stage (global systems and ticks not yet split by region).</summary>
    public void AddWorldStage(string name, Action<TickContext> run, int periodTicks = 1)
    {
        ArgumentNullException.ThrowIfNull(run);
        Add(new WorldStage(name, periodTicks, run));
    }

    /// <summary>A per-region stage.</summary>
    public void AddRegionStage(IRegionStage stage, int periodTicks = 1)
    {
        ArgumentNullException.ThrowIfNull(stage);
        Add(new PerRegionStage(stage, periodTicks));
    }

    /// <summary>
    /// Milliseconds from <paramref name="wallUnixMs"/> until the next tick is due, clamped to one step so a wall clock
    /// set backwards cannot stall the loop.
    /// </summary>
    public long MillisecondsUntilNextTick(long wallUnixMs) =>
        Math.Clamp(Clock.TimeOf(Tick + 1) - wallUnixMs, 0, StepMilliseconds);

    public void Step(long wallUnixMs)
    {
        long started = Stopwatch.GetTimestamp();
        long previous = Tick;
        long next = Math.Max(previous + 1, Clock.TickAt(wallUnixMs));
        Tick = next;
        var tick = new TickContext(next, Clock.TimeOf(next), wallUnixMs);
        if (next > previous + 1)
            _observer.TicksSkipped(tick, next - previous - 1);

        using (Clock.Pin(tick.NowUnixMs))
        {
            foreach (var stage in _stages)
            {
                if (next / stage.PeriodTicks == previous / stage.PeriodTicks)
                    continue;
                Report(stage.Run(this, tick));
            }
        }

        _observer.StepCompleted(tick, Stopwatch.GetElapsedTime(started), TimeSpan.FromMilliseconds(StepMilliseconds));
    }

    /// <summary>
    /// Moves the clock forward to the tick <paramref name="wallUnixMs"/> is in without running the ticks in between:
    /// the loop starting after the world was built, and the test and admin hooks. Never moves it back.
    /// </summary>
    public void Resume(long wallUnixMs) => Tick = Math.Max(Tick, Clock.TickAt(wallUnixMs));

    /// <summary>
    /// Runs one registered region stage now, after <see cref="Resume"/>, at the resulting tick (test and admin hooks).
    /// Failures are reported like in <see cref="Step"/> and then rethrown so the caller sees them.
    /// </summary>
    public void RunNow(IRegionStage stage, long wallUnixMs)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var entry = _stages.OfType<PerRegionStage>().FirstOrDefault(s => ReferenceEquals(s.Stage, stage))
            ?? throw new InvalidOperationException($"Region stage '{stage.Name}' is not registered on this scheduler.");
        RunHook([entry], $"Region stage '{stage.Name}'", wallUnixMs);
    }

    /// <summary>
    /// Runs the registered stage named <paramref name="name"/> now, after <see cref="Resume"/>, at the resulting tick
    /// (hooks for one world stage). Failures are reported and rethrown.
    /// </summary>
    public void RunNow(string name, long wallUnixMs)
    {
        var entry = _stages.FirstOrDefault(s => s.Name == name)
            ?? throw new InvalidOperationException($"Simulation stage '{name}' is not registered on this scheduler.");
        RunHook([entry], $"Stage '{name}'", wallUnixMs);
    }

    /// <summary>
    /// Runs every stage registered with <paramref name="periodTicks"/>, in registration order, after
    /// <see cref="Resume"/>, at the resulting tick (test and admin hooks for one cadence). Failures are reported and
    /// rethrown.
    /// </summary>
    public void RunPeriod(int periodTicks, long wallUnixMs)
    {
        var entries = _stages.Where(s => s.PeriodTicks == periodTicks).ToList();
        if (entries.Count == 0)
            throw new InvalidOperationException($"No simulation stage is registered with period {periodTicks}.");
        RunHook(entries, $"Period-{periodTicks} stages", wallUnixMs);
    }

    void RunHook(IReadOnlyList<ScheduledStage> entries, string what, long wallUnixMs)
    {
        Resume(wallUnixMs);
        var tick = new TickContext(Tick, Clock.TimeOf(Tick), wallUnixMs);
        var failures = new List<StageFailure>();
        using (Clock.Pin(tick.NowUnixMs))
        {
            foreach (var entry in entries)
                failures.AddRange(entry.Run(this, tick));
        }

        Report(failures);
        if (failures.Count > 0)
            throw new AggregateException(
                $"{what} failed in {failures.Count} scope(s): {string.Join("; ", failures)}",
                failures.Select(f => f.Error));
    }

    void Add(ScheduledStage stage)
    {
        if (string.IsNullOrWhiteSpace(stage.Name))
            throw new ArgumentException("Simulation stage needs a name.", nameof(stage));
        if (stage.PeriodTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(stage), stage.PeriodTicks,
                $"Simulation stage '{stage.Name}' period must be at least one tick.");
        if (!_names.Add(stage.Name))
            throw new InvalidOperationException($"Simulation stage '{stage.Name}' is already registered.");
        _stages.Add(stage);
    }

    void Report(IReadOnlyList<StageFailure> failures)
    {
        foreach (var failure in failures)
            _observer.StageFailed(failure);
    }

    abstract class ScheduledStage(string name, int periodTicks)
    {
        public string Name { get; } = name;
        public int PeriodTicks { get; } = periodTicks;

        public abstract IReadOnlyList<StageFailure> Run(RegionTickScheduler owner, TickContext tick);
    }

    sealed class WorldStage(string name, int periodTicks, Action<TickContext> run) : ScheduledStage(name, periodTicks)
    {
        public override IReadOnlyList<StageFailure> Run(RegionTickScheduler owner, TickContext tick)
        {
            long started = Stopwatch.GetTimestamp();
            StageFailure[] failures = [];
            try
            {
                run(tick);
            }
            catch (Exception ex)
            {
                failures = [new StageFailure(Name, tick.Tick, "world", ex)];
            }

            owner._observer.StageCompleted(Name, tick, 1, 0, Stopwatch.GetElapsedTime(started));
            return failures;
        }
    }

    sealed class PerRegionStage(IRegionStage stage, int periodTicks) : ScheduledStage(stage.Name, periodTicks)
    {
        public IRegionStage Stage { get; } = stage;

        public override IReadOnlyList<StageFailure> Run(RegionTickScheduler owner, TickContext tick)
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                Stage.Collect(tick, owner._collector);
            }
            catch (Exception ex)
            {
                owner._collector.Drain();
                owner._observer.StageCompleted(Name, tick, 0, 0, Stopwatch.GetElapsedTime(started));
                return [new StageFailure(Name, tick.Tick, "collect", ex)];
            }

            var work = owner._collector.Drain();
            // One slot per region keeps the failure order deterministic under the parallel executor.
            var errors = new Exception?[work.Count];
            owner._executor.Run(work.Count, i =>
            {
                try
                {
                    Stage.RunRegion(tick, work[i].Region, work[i].ObjectIds);
                }
                catch (Exception ex)
                {
                    errors[i] = ex;
                }
            });

            var failures = new List<StageFailure>();
            int entities = 0;
            for (int i = 0; i < work.Count; i++)
            {
                entities += work[i].ObjectIds.Count;
                if (errors[i] is { } error)
                    failures.Add(new StageFailure(Name, tick.Tick, work[i].Region.ToString(), error));
            }

            owner._observer.StageCompleted(Name, tick, work.Count, entities, Stopwatch.GetElapsedTime(started));
            return failures;
        }
    }
}
