using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.Simulation;

/// <summary>A stage body that threw. <paramref name="Scope"/> is the region id, <c>world</c>, or <c>collect</c>.</summary>
public sealed record StageFailure(string Stage, long Tick, string Scope, Exception Error)
{
    public override string ToString() =>
        $"stage={Stage} scope={Scope} tick={Tick} error={Error.GetType().Name}: {Error.Message}";
}

/// <summary>Timing and fault sink for <see cref="RegionTickScheduler"/>.</summary>
public interface ISimulationObserver
{
    void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed);

    void StageFailed(StageFailure failure);

    /// <summary>The step to <paramref name="tick"/> jumped over <paramref name="skipped"/> ticks because it ran late.</summary>
    void TicksSkipped(TickContext tick, long skipped);

    /// <summary>Owner changes made at the end of the step to <paramref name="tick"/>, in ascending object id order.</summary>
    void EntitiesHandedOff(TickContext tick, IReadOnlyList<RegionTransfer> transfers);

    void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget);
}

/// <summary>
/// Production observer: per-stage histograms, a fault counter with the full exception in the log, an overrun warning
/// naming the slowest stages whenever a step takes longer than its fixed step, a skipped-tick counter and warning, and
/// a warning when the wall clock falls a step or more behind simulation time (and when it catches up).
/// </summary>
sealed class ZoneSimulationObserver : ISimulationObserver
{
    const int SlowestStagesLogged = 3;
    readonly List<(string Stage, double Ms)> _stepStages = [];
    bool _wallBehind;
    Func<(int TotalNpcs, int ThinkingNpcs, int PlayerRegions)>? _npcCounts;

    internal void BindNpcDiagnostics(Func<(int TotalNpcs, int ThinkingNpcs, int PlayerRegions)> provider) =>
        _npcCounts = provider ?? throw new ArgumentNullException(nameof(provider));

    public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed)
    {
        var tag = new KeyValuePair<string, object?>("stage", stage);
        double ms = elapsed.TotalMilliseconds;
        FineageTelemetry.StageMilliseconds.Record(ms, tag);
        FineageTelemetry.StageRegions.Record(regions, tag);
        _stepStages.Add((stage, ms));
    }

    public void StageFailed(StageFailure failure)
    {
        FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", failure.Stage));
        ZoneLog.Warn($"simulation stage failed: {failure}\n{failure.Error}");
    }

    public void TicksSkipped(TickContext tick, long skipped)
    {
        FineageTelemetry.TicksSkipped.Add(skipped);
        ZoneLog.Warn(
            $"simulation skipped {skipped} tick(s) before tick {tick.Tick}: the step ran {tick.WallUnixMs - tick.NowUnixMs} ms into its interval; their stages run once now");
    }

    public void EntitiesHandedOff(TickContext tick, IReadOnlyList<RegionTransfer> transfers) =>
        FineageTelemetry.RegionHandoffs.Add(transfers.Count);

    public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget)
    {
        if (elapsed > budget)
        {
            FineageTelemetry.TickOverruns.Add(1);
            string slowest = string.Join(", ", _stepStages
                .OrderByDescending(s => s.Ms)
                .Take(SlowestStagesLogged)
                .Select(s => $"{s.Stage}={s.Ms:F1}ms"));
            string counts = "";
            if (_npcCounts != null)
            {
                var (total, thinking, regions) = _npcCounts();
                counts = $"; npcs total={total} thinking={thinking} playerRegions={regions}";
            }

            ZoneLog.Warn(
                $"simulation tick {tick.Tick} took {elapsed.TotalMilliseconds:F1} ms, over its {budget.TotalMilliseconds:F0} ms step; slowest: {slowest}{counts}");
        }

        bool wallBehind = tick.NowUnixMs - tick.WallUnixMs >= budget.TotalMilliseconds;
        if (wallBehind != _wallBehind)
        {
            _wallBehind = wallBehind;
            ZoneLog.Warn(wallBehind
                ? $"wall clock is {tick.NowUnixMs - tick.WallUnixMs} ms behind simulation tick {tick.Tick}; the simulation keeps its fixed step until the wall clock catches up"
                : $"wall clock caught up with simulation tick {tick.Tick}");
        }

        _stepStages.Clear();
    }
}
