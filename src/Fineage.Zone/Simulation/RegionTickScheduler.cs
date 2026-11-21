using System.Collections.Concurrent;
using System.Diagnostics;

namespace Fineage.Zone.Simulation;

/// <summary>A region body running right now: what a step watchdog names when a step does not finish.</summary>
public sealed record RegionBodyTrace(string Stage, RegionId Region, int ManagedThreadId, long StartedTimestamp)
{
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(StartedTimestamp);

    public override string ToString() =>
        $"{Stage}@{Region} on thread {ManagedThreadId} for {Elapsed.TotalMilliseconds:F0} ms";
}

/// <summary>
/// Fixed-step simulation clock. Each <see cref="Step"/> moves <see cref="Tick"/> to the next tick (or, when the caller
/// ran late, to the tick the wall clock is in, reporting the skipped ones) and runs every due stage in registration
/// order, with a barrier between stages. A stage with period <c>p</c> is due when the step crosses a multiple of
/// <c>p</c>, so skipped ticks coalesce into one run instead of losing it. A world stage runs once; a region stage
/// collects its entities into regions and runs each region in its <see cref="IRegionScope"/>, through the parallel
/// executor when the stage allows it and one after another in ascending <see cref="RegionId"/> order otherwise; the
/// scopes are committed in ascending order after the barrier. A throwing stage or region is reported to the
/// <see cref="ISimulationObserver"/> and the rest of the step still runs. Not thread-safe: one caller drives the clock.
/// </summary>
public sealed class RegionTickScheduler
{
    readonly List<ScheduledStage> _stages = [];
    readonly HashSet<string> _names = new(StringComparer.Ordinal);
    readonly RegionWorkCollector _collector = new();
    readonly IRegionExecutor _executor;
    readonly ISimulationObserver _observer;
    readonly IRegionScopes _scopes;
    readonly ConcurrentDictionary<long, RegionBodyTrace> _inFlight = new();
    long _inFlightSeq;

    [ThreadStatic] static RegionId? t_body;

    /// <summary>The region whose body runs on this thread, or null outside region bodies.</summary>
    public static RegionId? CurrentBody => t_body;

    /// <summary>
    /// The region bodies running right now, in (stage, region) order. Empty between region stages and while world
    /// stages run, so a stuck step with an empty list points at a world stage, the collect step or the barrier.
    /// </summary>
    public IReadOnlyList<RegionBodyTrace> InFlightRegionBodies =>
        _inFlight.Values.OrderBy(t => t.Stage, StringComparer.Ordinal).ThenBy(t => t.Region).ToList();

    /// <summary>Starts at the tick the clock's wall time is in; the first <see cref="Step"/> runs the tick after it.</summary>
    /// <param name="executor">Runs the regions of stages that allow it (<see cref="IRegionStage.Parallel"/>).</param>
    /// <param name="scopes">Opens the scope each region body runs in; <see cref="DirectRegionScopes"/> when omitted.</param>
    public RegionTickScheduler(
        SimulationClock clock,
        IRegionExecutor executor,
        ISimulationObserver observer,
        IRegionScopes? scopes = null)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _scopes = scopes ?? DirectRegionScopes.Instance;
        Tick = clock.TickAt(clock.WallUnixMs());
    }

    /// <summary>The executor of stages that allow parallel regions.</summary>
    public IRegionExecutor Executor => _executor;

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

        using (Clock.Pin(tick))
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
        using (Clock.Pin(tick))
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
            var scopes = new IRegionScope[work.Count];
            try
            {
                for (int i = 0; i < work.Count; i++)
                    scopes[i] = owner._scopes.Open(Name, tick, work[i].Region);
            }
            catch (Exception ex)
            {
                owner._observer.StageCompleted(Name, tick, 0, 0, Stopwatch.GetElapsedTime(started));
                return [new StageFailure(Name, tick.Tick, "scopes", ex)];
            }

            // One slot per region keeps the failure order deterministic under the parallel executor.
            var errors = new Exception?[work.Count];
            var executor = Stage.Parallel ? owner._executor : SequentialRegionExecutor.Instance;
            executor.Run(work.Count, i =>
            {
                var outer = t_body;
                t_body = work[i].Region;
                long traceId = Interlocked.Increment(ref owner._inFlightSeq);
                owner._inFlight[traceId] = new RegionBodyTrace(Name, work[i].Region,
                    Environment.CurrentManagedThreadId, Stopwatch.GetTimestamp());
                try
                {
                    // The pin is per thread: a worker reads the step's time like the scheduler thread does.
                    using (owner.Clock.Pin(tick))
                    using (scopes[i].Enter())
                        Stage.RunRegion(tick, work[i].Region, work[i].ObjectIds);
                }
                catch (Exception ex)
                {
                    errors[i] = ex;
                }
                finally
                {
                    owner._inFlight.TryRemove(traceId, out _);
                    t_body = outer;
                }
            });

            var failures = new List<StageFailure>();
            int entities = 0;
            for (int i = 0; i < work.Count; i++)
            {
                entities += work[i].ObjectIds.Count;
                if (errors[i] is { } error)
                    failures.Add(new StageFailure(Name, tick.Tick, work[i].Region.ToString(), error));
                try
                {
                    scopes[i].Commit();
                }
                catch (Exception ex)
                {
                    failures.Add(new StageFailure(Name, tick.Tick, $"{work[i].Region} commit", ex));
                }
            }

            owner._observer.StageCompleted(Name, tick, work.Count, entities, Stopwatch.GetElapsedTime(started));
            return failures;
        }
    }
}
