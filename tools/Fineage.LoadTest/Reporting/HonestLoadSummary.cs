using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Metrics;

namespace Fineage.LoadTest.Reporting;

/// <summary>Load harness metrics with interpolated histogram quantiles and steady-state windows.</summary>
public sealed class HonestLoadSummary
{
    public HonestWindow Warmup { get; init; } = new();
    public HonestWindow SteadyState { get; init; } = new();
}

public sealed class HonestWindow
{
    public double TickP95Ms { get; init; }
    public double TickP99Ms { get; init; }
    public double TickMaxMs { get; init; }
    public double CommandsStageP99Ms { get; init; }
    public double CommandToMailboxesP99Ms { get; init; }
    public double FanoutDrainP99Ms { get; init; }
    public double FanoutBatchProcessP99Ms { get; init; }
    public double ClientCombatFanoutP99Ms { get; init; }
    public double ClientCombatFanoutMaxMs { get; init; }
}

public static class HonestLoadSummaryBuilder
{
    public static HonestLoadSummary Build(
        IReadOnlyList<PrometheusSample> samples,
        IReadOnlyList<string> rawScrapes,
        DateTimeOffset runStarted,
        int rampUpSeconds,
        IEnumerable<BotObservationLog.EntityTracker.LatencySample> clientSamples)
    {
        var steadyAfter = runStarted.AddSeconds(rampUpSeconds);
        var warmupSamples = samples.Where(s => s.ScrapedAt < steadyAfter).ToList();
        var steadySamples = samples.Where(s => s.ScrapedAt >= steadyAfter).ToList();

        var warmupClient = clientSamples.Where(s => s.ObservedAt < steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();
        var steadyClient = clientSamples.Where(s => s.ObservedAt >= steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();

        var warmup = BuildServerWindow(warmupSamples, rawScrapes, samples, isSteady: false);
        var steady = BuildServerWindow(steadySamples, rawScrapes, samples, isSteady: true);
        return new HonestLoadSummary
        {
            Warmup = new HonestWindow
            {
                TickP95Ms = warmup.TickP95Ms,
                TickP99Ms = warmup.TickP99Ms,
                TickMaxMs = warmup.TickMaxMs,
                CommandsStageP99Ms = warmup.CommandsStageP99Ms,
                CommandToMailboxesP99Ms = warmup.CommandToMailboxesP99Ms,
                FanoutDrainP99Ms = warmup.FanoutDrainP99Ms,
                FanoutBatchProcessP99Ms = warmup.FanoutBatchProcessP99Ms,
                ClientCombatFanoutP99Ms = Percentile(warmupClient, 0.99),
                ClientCombatFanoutMaxMs = warmupClient.Count > 0 ? warmupClient[^1] : 0
            },
            SteadyState = new HonestWindow
            {
                TickP95Ms = steady.TickP95Ms,
                TickP99Ms = steady.TickP99Ms,
                TickMaxMs = steady.TickMaxMs,
                CommandsStageP99Ms = steady.CommandsStageP99Ms,
                CommandToMailboxesP99Ms = steady.CommandToMailboxesP99Ms,
                FanoutDrainP99Ms = steady.FanoutDrainP99Ms,
                FanoutBatchProcessP99Ms = steady.FanoutBatchProcessP99Ms,
                ClientCombatFanoutP99Ms = Percentile(steadyClient, 0.99),
                ClientCombatFanoutMaxMs = steadyClient.Count > 0 ? steadyClient[^1] : 0
            }
        };
    }

    static HonestWindow BuildServerWindow(
        IReadOnlyList<PrometheusSample> windowSamples,
        IReadOnlyList<string> rawScrapes,
        IReadOnlyList<PrometheusSample> allSamples,
        bool isSteady)
    {
        if (windowSamples.Count == 0)
            return new HonestWindow();

        if (isSteady && windowSamples.Count >= 2 && rawScrapes.Count == allSamples.Count)
        {
            int first = SampleIndex(allSamples, windowSamples[0]);
            int last = SampleIndex(allSamples, windowSamples[^1]);
            if (first >= 0 && last > first && last < rawScrapes.Count)
            {
                var tick = PrometheusParser.HistogramDelta(rawScrapes[first], rawScrapes[last], "fineage_tick_ms");
                var mail = PrometheusParser.HistogramDelta(rawScrapes[first], rawScrapes[last], "fineage_command_to_mailboxes_ms");
                var drain = PrometheusParser.HistogramDelta(rawScrapes[first], rawScrapes[last], "fineage_tick_step_fanout_drain_ms");
                var batch = PrometheusParser.HistogramDelta(rawScrapes[first], rawScrapes[last], "fineage_fanout_batch_process_ms");
                var commands = PrometheusParser.ParseLabeledHistogram(
                    rawScrapes[last], "fineage_tick_stage_ms", "stage", "commands");
                if (tick != null)
                {
                    return new HonestWindow
                    {
                        TickP95Ms = tick.P95,
                        TickP99Ms = tick.P99,
                        TickMaxMs = tick.Max,
                        CommandsStageP99Ms = commands?.P99 ?? 0,
                        CommandToMailboxesP99Ms = mail?.P99 ?? 0,
                        FanoutDrainP99Ms = drain?.P99 ?? 0,
                        FanoutBatchProcessP99Ms = batch?.P99 ?? 0
                    };
                }
            }
        }

        int lastIdx = SampleIndex(allSamples, windowSamples[^1]);
        var commandsPeak = lastIdx >= 0 && lastIdx < rawScrapes.Count
            ? PrometheusParser.ParseLabeledHistogram(rawScrapes[lastIdx], "fineage_tick_stage_ms", "stage", "commands")
            : null;

        return new HonestWindow
        {
            TickP95Ms = Percentile(windowSamples.Select(s => s.TickP95).OrderBy(v => v).ToList(), 0.95),
            TickP99Ms = Percentile(windowSamples.Select(s => s.TickP99).OrderBy(v => v).ToList(), 0.99),
            TickMaxMs = windowSamples.Max(s => s.TickMax),
            CommandsStageP99Ms = commandsPeak?.P99 ?? windowSamples.Max(s => s.TickStageP99Ms.GetValueOrDefault("commands")),
            CommandToMailboxesP99Ms = windowSamples.Max(s => s.CommandToMailboxesP99Ms),
            FanoutDrainP99Ms = windowSamples.Max(s => s.StepFanoutDrainP99Ms)
        };
    }

    static int SampleIndex(IReadOnlyList<PrometheusSample> all, PrometheusSample target)
    {
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].ScrapedAt == target.ScrapedAt)
                return i;
        }

        return -1;
    }

    static double Percentile(List<double> values, double q)
    {
        if (values.Count == 0)
            return 0;
        int idx = (int)Math.Ceiling(q * values.Count) - 1;
        return values[Math.Clamp(idx, 0, values.Count - 1)];
    }
}