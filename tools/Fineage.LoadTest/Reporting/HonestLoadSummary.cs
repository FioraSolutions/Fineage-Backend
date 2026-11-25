using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Metrics;

namespace Fineage.LoadTest.Reporting;

/// <summary>Load harness metrics with interpolated histogram quantiles and steady-state windows.</summary>
public sealed class HonestLoadSummary
{
    public List<string> DataErrors { get; init; } = [];
    public HonestWindow Warmup { get; init; } = new();
    public HonestWindow SteadyState { get; init; } = new();
    /// <summary>Tick histogram over the whole run (final cumulative scrape), ramp included.</summary>
    public TickWindowStats? TickOverall { get; init; }
    /// <summary>Tick histogram delta from (first scrape with every bot in-world) + settle seconds to the last scrape.</summary>
    public TickWindowStats? TickSteady { get; init; }
}

/// <summary>Tick quantiles for one window. <see cref="MaxMs"/> is exact when <see cref="MaxExact"/>, else the highest non-empty bucket ceiling.</summary>
public sealed record TickWindowStats
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string Rule { get; init; } = "";
    public long Ticks { get; init; }
    public double MeanMs { get; init; }
    public double P50Ms { get; init; }
    public double P99Ms { get; init; }
    public double P999Ms { get; init; }
    public double MaxMs { get; init; }
    public bool MaxExact { get; init; }
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
    public double FanoutQueueAgeP99Ms { get; init; }
    public double FanoutQueueAgeP999Ms { get; init; }
    public long FanoutDroppedTotal { get; init; }
    public long OutboundDroppedTotal { get; init; }
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

        var (tickOverall, tickSteady) = BuildTickWindows(samples, rawScrapes, errors);

        return new HonestLoadSummary
        {
            DataErrors = errors,
            TickOverall = tickOverall,
            TickSteady = tickSteady,
            Warmup = FillClient(warmup, warmupAttack, warmupSkill, warmupLegacy),
            SteadyState = FillClient(steady, steadyAttack, steadySkill, steadyLegacy)
        };
    }

    /// <summary>Seconds after every bot is in-world before the steady tick window opens.</summary>
    public const int SteadySettleSeconds = 30;

    static (TickWindowStats? Overall, TickWindowStats? Steady) BuildTickWindows(
        IReadOnlyList<PrometheusSample> samples,
        IReadOnlyList<string> rawScrapes,
        List<string> errors)
    {
        if (samples.Count == 0 || rawScrapes.Count != samples.Count)
        {
            errors.Add("tick_windows_unavailable: scrape samples and raw scrapes differ");
            return (null, null);
        }

        int last = samples.Count - 1;
        var all = PrometheusParser.ParseSingletonHistogram(rawScrapes[last], "fineage_tick_ms");
        TickWindowStats? overall = all == null ? null : new TickWindowStats
        {
            From = samples[0].ScrapedAt,
            To = samples[last].ScrapedAt,
            Rule = "whole run, ramp included",
            Ticks = all.Count,
            MeanMs = all.Mean,
            P50Ms = all.P50,
            P99Ms = all.P99,
            P999Ms = all.P999,
            MaxMs = samples[last].TickTrueMaxMs > 0 ? samples[last].TickTrueMaxMs : all.Max,
            MaxExact = samples[last].TickTrueMaxMs > 0
        };
        if (all == null)
            errors.Add("tick_overall_histogram_unavailable");

        int peakOnline = samples.Max(s => s.ZoneOnline);
        int inWorld = -1;
        for (int i = 0; i < samples.Count; i++)
        {
            if (peakOnline > 0 && samples[i].ZoneOnline >= peakOnline)
            {
                inWorld = i;
                break;
            }
        }

        if (inWorld < 0)
        {
            errors.Add("tick_steady_unavailable: fineage_zone_online never reported");
            return (overall, null);
        }

        DateTimeOffset steadyFrom = samples[inWorld].ScrapedAt.AddSeconds(SteadySettleSeconds);
        int first = -1;
        for (int i = inWorld; i < samples.Count; i++)
        {
            if (samples[i].ScrapedAt >= steadyFrom)
            {
                first = i;
                break;
            }
        }

        if (first < 0 || first >= last)
        {
            errors.Add("tick_steady_unavailable: run ended before every bot was in-world + settle");
            return (overall, null);
        }

        var delta = PrometheusParser.HistogramDelta(rawScrapes[first], rawScrapes[last], "fineage_tick_ms");
        if (delta == null)
        {
            errors.Add("tick_steady_histogram_delta_unavailable");
            return (overall, null);
        }

        // The tick max gauge is a lifetime max: when it rose inside the window its final value is the window max.
        bool exact = samples[last].TickTrueMaxMs > samples[first].TickTrueMaxMs;
        var steady = new TickWindowStats
        {
            From = samples[first].ScrapedAt,
            To = samples[last].ScrapedAt,
            Rule = $"from first scrape with zone online = {peakOnline} ({samples[inWorld].ScrapedAt:HH:mm:ss}Z) + {SteadySettleSeconds} s",
            Ticks = delta.Count,
            MeanMs = delta.Mean,
            P50Ms = delta.P50,
            P99Ms = delta.P99,
            P999Ms = delta.P999,
            MaxMs = exact ? samples[last].TickTrueMaxMs : delta.Max,
            MaxExact = exact
        };
        return (overall, steady);
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

        double trueMax = windowSamples.Max(s => s.TickTrueMaxMs);
        if (trueMax <= 0 && windowSamples.Any(s => s.TickMax > 0))
            errors.Add("fineage_tick_max_ms absent while the tick histogram has a bucket ceiling; that ceiling is not the tick max");

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
                var fanoutAge = PrometheusParser.HistogramDelta(rawScrapes[first], rawScrapes[last], "fineage_fanout_queue_oldest_age_ms");
                long fanoutDropped = PrometheusParser.CounterDeltaSum(rawScrapes[first], rawScrapes[last], "fineage_fanout_dropped_total");
                long outboundDropped = PrometheusParser.CounterDeltaSum(rawScrapes[first], rawScrapes[last], "fineage_outbound_dropped_total");
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
                        TickMaxEstimateMs = trueMax,
                        CommandsStageP99EstimateMs = commands?.P99 ?? double.NaN,
                        CommandToMailboxesP99Ms = mail?.P99 ?? double.NaN,
                        FanoutDrainP99Ms = drain?.P99 ?? double.NaN,
                        FanoutBatchProcessP99Ms = batch?.P99 ?? double.NaN,
                        FanoutQueueAgeP99Ms = fanoutAge?.P99 ?? double.NaN,
                        FanoutQueueAgeP999Ms = fanoutAge?.P999 ?? double.NaN,
                        FanoutDroppedTotal = fanoutDropped,
                        OutboundDroppedTotal = outboundDropped,
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
            TickMaxEstimateMs = trueMax,
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