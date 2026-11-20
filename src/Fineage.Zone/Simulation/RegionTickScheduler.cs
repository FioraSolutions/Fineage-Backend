using System.Diagnostics;

namespace Fineage.Zone.Simulation;

/// <summary>
/// Fixed-step simulation clock. Each <see cref="Step"/> advances <see cref="Tick"/> by one and runs every due stage in
/// registration order, with a barrier between stages. A world stage runs once; a region stage collects its entities
/// into regions and runs each region through the <see cref="IRegionExecutor"/> in ascending <see cref="RegionId"/>
/// order. A throwing stage or region is reported to the <see cref="ISimulationObserver"/> and the rest of the step
/// still runs. Not thread-safe: one caller drives the clock.
/// </summary>
public sealed class RegionTickScheduler
{
    readonly List<ScheduledStage> _stages = [];
    readonly HashSet<string> _names = new(StringComparer.Ordinal);
    readonly RegionWorkCollector _collector;
    readonly IRegionExecutor _executor;
    readonly ISimulationObserver _observer;

    public RegionTickScheduler(
        int stepMilliseconds,
        IRegionPartition partition,
        IRegionExecutor executor,
        ISimulationObserver observer)
    {
        if (stepMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepMilliseconds), stepMilliseconds, "Simulation step must be positive.");
        StepMilliseconds = stepMilliseconds;
        _collector = new RegionWorkCollector(partition);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
    }

    public int StepMilliseconds { get; }

    /// <summary>Steps taken so far; the first <see cref="Step"/> runs tick 1.</summary>
    public long Tick { get; private set; }

    public IReadOnlyList<string> StageNames => _stages.Select(s => s.Name).ToList();

    /// <summary>A single-actor stage (global systems and ticks not yet split by region). Due when Tick % period == 0.</summary>
    public void AddWorldStage(string name, Action<TickContext> run, int periodTicks = 1)
    {
        ArgumentNullException.ThrowIfNull(run);
        Add(new WorldStage(name, periodTicks, run));
    }

    /// <summary>A per-region stage. Due when Tick % period == 0.</summary>
    public void AddRegionStage(IRegionStage stage, int periodTicks = 1)
    {
        ArgumentNullException.ThrowIfNull(stage);
        Add(new PerRegionStage(stage, periodTicks));
    }

    public void Step(long nowUnixMs)
    {
        long started = Stopwatch.GetTimestamp();
        var tick = new TickContext(++Tick, nowUnixMs);
        foreach (var stage in _stages)
        {
            if (tick.Tick % stage.PeriodTicks != 0)
                continue;
            Report(stage.Run(this, tick));
        }

        _observer.StepCompleted(tick, Stopwatch.GetElapsedTime(started), TimeSpan.FromMilliseconds(StepMilliseconds));
    }

    /// <summary>
    /// Runs one registered region stage now, at the current tick, without advancing the clock (test and admin hooks).
    /// Failures are reported like in <see cref="Step"/> and then rethrown so the caller sees them.
    /// </summary>
    public void RunNow(IRegionStage stage, long nowUnixMs)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var entry = _stages.OfType<PerRegionStage>().FirstOrDefault(s => ReferenceEquals(s.Stage, stage))
            ?? throw new InvalidOperationException($"Region stage '{stage.Name}' is not registered on this scheduler.");
        var failures = entry.Run(this, new TickContext(Tick, nowUnixMs));
        Report(failures);
        if (failures.Count > 0)
            throw new AggregateException(
                $"Region stage '{stage.Name}' failed in {failures.Count} scope(s): {string.Join("; ", failures)}",
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
