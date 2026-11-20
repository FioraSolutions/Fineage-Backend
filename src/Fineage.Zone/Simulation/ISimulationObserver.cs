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

    void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget);
}

/// <summary>
/// Production observer: per-stage histograms, a fault counter with the full exception in the log, and an overrun
/// warning naming the slowest stages whenever a step takes longer than its fixed step.
/// </summary>
sealed class ZoneSimulationObserver : ISimulationObserver
{
    const int SlowestStagesLogged = 3;
    readonly List<(string Stage, double Ms)> _stepStages = [];

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

    public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget)
    {
        if (elapsed > budget)
        {
            FineageTelemetry.TickOverruns.Add(1);
            string slowest = string.Join(", ", _stepStages
                .OrderByDescending(s => s.Ms)
                .Take(SlowestStagesLogged)
                .Select(s => $"{s.Stage}={s.Ms:F1}ms"));
            ZoneLog.Warn(
                $"simulation tick {tick.Tick} took {elapsed.TotalMilliseconds:F1} ms, over its {budget.TotalMilliseconds:F0} ms step; slowest: {slowest}");
        }

        _stepStages.Clear();
    }
}
