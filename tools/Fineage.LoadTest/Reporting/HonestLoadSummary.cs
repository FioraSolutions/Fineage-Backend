using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Metrics;

namespace Fineage.LoadTest.Reporting;

/// <summary>Load harness metrics with interpolated histogram quantiles and steady-state windows.</summary>
public sealed class HonestLoadSummary
{
    public List<string> DataErrors { get; init; } = [];
    public HonestWindow Warmup { get; init; } = new();
    public HonestWindow SteadyState { get; init; } = new();
}

public sealed record HonestWindow
{
    public double TickP95Ms { get; init; }
    /// <summary>Histogram-interpolated tick P99 (not a bucket snap).</summary>
    public double TickP99EstimateMs { get; init; }
    /// <summary>Histogram bucket max estimate (may exceed observed single samples).</summary>
    public double TickMaxEstimateMs { get; init; }
    public double CommandsStageP99EstimateMs { get; init; }
    public double CommandToMailboxesP99Ms { get; init; }
    public double FanoutDrainP99Ms { get; init; }
    public double FanoutBatchProcessP99Ms { get; init; }
    public double ClientAttackFanoutP95EstimateMs { get; init; }
    public double ClientAttackFanoutP99EstimateMs { get; init; }
    public double ClientSkillFanoutP95EstimateMs { get; init; }
    public double ClientSkillFanoutP99EstimateMs { get; init; }
    public long FanoutDroppedTotal { get; init; }
    public long SocketWriteCountSteady { get; init; }
    public double SocketWriteFramesMeanSteady { get; init; }
    public double ClientCombatFanoutP99EstimateMs { get; init; }
    public double ClientCombatFanoutMaxMs { get; init; }
}

public static class HonestLoadSummaryBuilder
{
    public static HonestLoadSummary Build(
        IReadOnlyList<PrometheusSample> samples,
        IReadOnlyList<string> rawScrapes,
        DateTimeOffset runStarted,
        DateTimeOffset steadyAfter,
        IEnumerable<BotObservationLog.EntityTracker.LatencySample> attackSamples,
        IEnumerable<BotObservationLog.EntityTracker.LatencySample> skillSamples,
        IEnumerable<BotObservationLog.EntityTracker.LatencySample> legacyClientSamples)
    {
        var warmupSamples = samples.Where(s => s.ScrapedAt < steadyAfter).ToList();
        var steadySamples = samples.Where(s => s.ScrapedAt >= steadyAfter).ToList();

        var errors = new List<string>();
        var warmup = BuildServerWindow(warmupSamples, rawScrapes, samples, isSteady: false, errors);
        var steady = BuildServerWindow(steadySamples, rawScrapes, samples, isSteady: true, errors);

        var warmupAttack = attackSamples.Where(s => s.ObservedAt < steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();
        var steadyAttack = attackSamples.Where(s => s.ObservedAt >= steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();
        var warmupSkill = skillSamples.Where(s => s.ObservedAt < steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();
        var steadySkill = skillSamples.Where(s => s.ObservedAt >= steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();
        var warmupLegacy = legacyClientSamples.Where(s => s.ObservedAt < steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();
        var steadyLegacy = legacyClientSamples.Where(s => s.ObservedAt >= steadyAfter).Select(s => s.Milliseconds).OrderBy(v => v).ToList();

        return new HonestLoadSummary
        {
            DataErrors = errors,
            Warmup = FillClient(warmup, warmupAttack, warmupSkill, warmupLegacy),
            SteadyState = FillClient(steady, steadyAttack, steadySkill, steadyLegacy)
        };
    }

    static HonestWindow FillClient(
        HonestWindow server,
        List<double> attack,
        List<double> skill,
        List<double> legacy)
    {
        double attackP99 = Percentile(attack, 0.99);
        double skillP99 = Percentile(skill, 0.99);
        double combatP99 = Math.Max(attackP99, skillP99);
        if (combatP99 == 0)
            combatP99 = Percentile(legacy, 0.99);
        return server with
        {
            ClientAttackFanoutP95EstimateMs = Percentile(attack, 0.95),
            ClientAttackFanoutP99EstimateMs = attackP99,
            ClientSkillFanoutP95EstimateMs = Percentile(skill, 0.95),
            ClientSkillFanoutP99EstimateMs = skillP99,
            ClientCombatFanoutP99EstimateMs = combatP99,
            ClientCombatFanoutMaxMs = legacy.Count > 0 ? legacy[^1] : attack.Count > 0 ? attack[^1] : skill.Count > 0 ? skill[^1] : 0
        };
    }

    static HonestWindow BuildServerWindow(
        IReadOnlyList<PrometheusSample> windowSamples,
        IReadOnlyList<string> rawScrapes,
        IReadOnlyList<PrometheusSample> allSamples,
        bool isSteady,
        List<string> errors)
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
                var commands = PrometheusParser.LabeledHistogramDelta(
                    rawScrapes[first], rawScrapes[last], "fineage_tick_stage_ms", "stage", "commands");
                if (tick == null)
                    errors.Add("steady_tick_histogram_delta_unavailable");
                if (isSteady && commands == null)
                    errors.Add("steady_commands_stage_histogram_delta_unavailable");
                if (mail == null)
                    errors.Add("steady_command_to_mailboxes_histogram_delta_unavailable");

                var socketWrites = PrometheusParser.HistogramDelta(rawScrapes[first], rawScrapes[last], "fineage_socket_write_ms");
                var framesPerWrite = PrometheusParser.HistogramDelta(rawScrapes[first], rawScrapes[last], "fineage_socket_write_frames");
                long fanoutDropped = PrometheusParser.CounterDeltaSum(rawScrapes[first], rawScrapes[last], "fineage_fanout_dropped_total");
                if (isSteady && socketWrites == null)
                    errors.Add("steady_socket_write_histogram_delta_unavailable");
                if (isSteady && framesPerWrite == null)
                    errors.Add("steady_socket_write_frames_histogram_delta_unavailable");

                if (tick != null)
                {
                    return new HonestWindow
                    {
                        TickP95Ms = tick.P95,
                        TickP99EstimateMs = tick.P99,
                        TickMaxEstimateMs = tick.Max,
                        CommandsStageP99EstimateMs = commands?.P99 ?? double.NaN,
                        CommandToMailboxesP99Ms = mail?.P99 ?? double.NaN,
                        FanoutDrainP99Ms = drain?.P99 ?? double.NaN,
                        FanoutBatchProcessP99Ms = batch?.P99 ?? double.NaN,
                        FanoutDroppedTotal = fanoutDropped,
                        SocketWriteCountSteady = socketWrites?.Count ?? 0,
                        SocketWriteFramesMeanSteady = framesPerWrite?.Mean ?? 0
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
            TickP99EstimateMs = Percentile(windowSamples.Select(s => s.TickP99).OrderBy(v => v).ToList(), 0.99),
            TickMaxEstimateMs = windowSamples.Max(s => s.TickMax),
            CommandsStageP99EstimateMs = commandsPeak?.P99 ?? windowSamples.Max(s => s.TickStageP99Ms.GetValueOrDefault("commands")),
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