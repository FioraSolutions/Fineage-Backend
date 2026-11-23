using System.Globalization;
using System.Text.RegularExpressions;
using Fineage.Protocol.Telemetry;

namespace Fineage.LoadTest.Metrics;

public sealed record HistogramStats(
    double Sum,
    long Count,
    double Mean,
    double P50,
    double P95,
    double P99,
    double P999,
    double Max);

public sealed class PrometheusSample
{
    public DateTimeOffset ScrapedAt { get; init; } = DateTimeOffset.UtcNow;
    public double TickP50 { get; init; }
    public double TickP95 { get; init; }
    public double TickP99 { get; init; }
    public double TickMax { get; init; }
    public double StepFanoutDrainP99Ms { get; init; }
    public double CommandToWireP99Ms { get; init; }
    public double CommandToMailboxesP99Ms { get; init; }
    public long TickOverruns { get; init; }
    public long TicksSkipped { get; init; }
    public long CommandIngressQueued { get; init; }
    public int ZoneOnline { get; init; }
    public double InPps { get; init; }
    public double OutPps { get; init; }
    public long PacketsErrored { get; init; }
    public long PacketsRejected { get; init; }
    public Dictionary<string, double> TickStageP99Ms { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> EnterP99Ms { get; init; } = new(StringComparer.Ordinal);
    public long OutboundDropped { get; init; }
    public long BytesOut { get; init; }
    public long BytesIn { get; init; }
    public long LedgerAdjustments { get; init; }
    public long LedgerFlushFailures { get; init; }
    public double FanoutQueueDepth { get; init; }
    public double FanoutQueueOldestAgeMs { get; init; }
    public double FanoutQueueOldestAgeP999Ms { get; init; }
    public long ReliableOverflowTotal { get; init; }
    public long FanoutDropped { get; init; }
    public double TickTrueMaxMs { get; init; }
    public double SubmitToMailboxP99Ms { get; init; }
    public double SubmitToMailboxP999Ms { get; init; }
    public double FanoutEnqueueToMailboxP99Ms { get; init; }
    public double CommandQueueDepthMax { get; init; }
    public double CommandQueueAgeP99Ms { get; init; }
    public double CommandQueueAgeP999Ms { get; init; }
    public double CommandQueueAgeMaxMs { get; init; }
    public double WatchdogStallMaxMs { get; init; }
    public long WatchdogStalls { get; init; }
    public long CommandCoalesced { get; init; }
    public long CommandRejected { get; init; }
}

public static class PrometheusParser
{
    static readonly double[] Bounds = MetricsText.DurationBoundsMs;

    public static PrometheusSample Parse(string text, DateTimeOffset scrapedAt = default)
    {
        if (scrapedAt == default)
            scrapedAt = DateTimeOffset.UtcNow;
        var buckets = new SortedDictionary<double, long>();
        long tickCount = ParseLong(text, "fineage_tick_ms_count");
        double tickSum = ParseDouble(text, "fineage_tick_ms_sum");
        foreach (Match match in Regex.Matches(text, @"fineage_tick_ms_bucket\{le=""([^""]+)""\}\s+(\d+)", RegexOptions.Multiline))
        {
            double le = match.Groups[1].Value == "+Inf"
                ? double.PositiveInfinity
                : double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            buckets[le] = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        return new PrometheusSample
        {
            ScrapedAt = scrapedAt,
            TickP50 = Quantile(buckets, tickCount, 0.50),
            TickP95 = Quantile(buckets, tickCount, 0.95),
            TickP99 = Quantile(buckets, tickCount, 0.99),
            TickMax = MaxFromHistogram(buckets, tickCount, tickSum),
            TickTrueMaxMs = ParseDouble(text, "fineage_tick_max_ms"),
            StepFanoutDrainP99Ms = ParseSingletonHistogram(text, "fineage_tick_step_fanout_drain_ms")?.P99 ?? 0,
            CommandToMailboxesP99Ms = Phase(text, "submit_to_mailbox")?.P99 ?? 0,
            SubmitToMailboxP99Ms = Phase(text, "submit_to_mailbox")?.P99 ?? 0,
            SubmitToMailboxP999Ms = Phase(text, "submit_to_mailbox")?.P999 ?? 0,
            FanoutEnqueueToMailboxP99Ms = Phase(text, "fanout_enqueue_to_mailbox")?.P99
                ?? ParseSingletonHistogram(text, "fineage_command_to_mailboxes_ms")?.P99 ?? 0,
            CommandToWireP99Ms = ParseSingletonHistogram(text, "fineage_command_to_wire_ms")?.P99 ?? 0,
            TickOverruns = ParseLong(text, "fineage_tick_overruns_total"),
            TicksSkipped = ParseLong(text, "fineage_tick_skipped_total"),
            CommandIngressQueued = ParseLong(text, "fineage_command_ingress_queued"),
            ZoneOnline = (int)ParseLong(text, "fineage_zone_online"),
            InPps = ParseDouble(text, @"fineage_packets_per_second\{direction=""in""\}"),
            OutPps = ParseDouble(text, @"fineage_packets_per_second\{direction=""out""\}"),
            PacketsRejected = ParseCounterSum(text, "fineage_packets_rejected_total"),
            PacketsErrored = ParseCounterSum(text, "fineage_packets_errored_total"),
            TickStageP99Ms = ParseStageQuantiles(text, 0.99),
            EnterP99Ms = ParseUnlabeledQuantiles(text, "fineage_enter_", 0.99),
            OutboundDropped = ParseCounterSum(text, "fineage_outbound_dropped_total"),
            FanoutDropped = ParseCounterSum(text, "fineage_fanout_dropped_total"),
            BytesOut = ParseCounterSum(text, "fineage_bytes_out_total"),
            BytesIn = ParseCounterSum(text, "fineage_bytes_in_total"),
            LedgerAdjustments = ParseCounterSum(text, "fineage_ledger_adjustments_total"),
            LedgerFlushFailures = ParseLong(text, "fineage_ledger_flush_failures_total"),
            FanoutQueueDepth = ParseSingletonHistogram(text, "fineage_fanout_queue_depth")?.Max ?? 0,
            FanoutQueueOldestAgeMs = ParseSingletonHistogram(text, "fineage_fanout_queue_oldest_age_ms")?.P99 ?? 0,
            FanoutQueueOldestAgeP999Ms = ParseSingletonHistogram(text, "fineage_fanout_queue_oldest_age_ms")?.P999 ?? 0,
            ReliableOverflowTotal = ParseCounterSum(text, "fineage_reliable_overflow_total"),
            CommandQueueDepthMax = ParseSingletonHistogram(text, "fineage_command_queue_depth")?.Max ?? 0,
            CommandQueueAgeP99Ms = ParseSingletonHistogram(text, "fineage_command_queue_age_ms")?.P99 ?? 0,
            CommandQueueAgeP999Ms = ParseSingletonHistogram(text, "fineage_command_queue_age_ms")?.P999 ?? 0,
            CommandQueueAgeMaxMs = ParseDouble(text, "fineage_command_queue_age_max_ms"),
            WatchdogStallMaxMs = ParseDouble(text, "fineage_step_watchdog_stall_max_ms"),
            WatchdogStalls = ParseLong(text, "fineage_step_watchdog_stalls"),
            CommandCoalesced = ParseLong(text, "fineage_command_coalesced_total"),
            CommandRejected = ParseCounterSum(text, "fineage_command_rejected_total")
        };
    }

    static HistogramStats? Phase(string text, string phase) =>
        ParseLabeledHistogram(text, "fineage_command_timeline_ms", "phase", phase);

    static double ParseSingletonP99(string text, string metric)
    {
        var hs = ParseSingletonHistogram(text, metric);
        return hs?.P99 ?? 0;
    }

    /// <summary>Per-stage quantile from <c>fineage_tick_stage_ms</c> histograms in a single scrape.</summary>
    public static Dictionary<string, double> ParseStageQuantiles(string text, double q)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var bucketsByStage = new Dictionary<string, SortedDictionary<double, long>>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     text,
                     @"fineage_tick_stage_ms_bucket\{stage=""([^""]+)"",le=""([^""]+)""\}\s+(\d+)",
                     RegexOptions.Multiline))
        {
            string stage = match.Groups[1].Value;
            double le = match.Groups[2].Value == "+Inf"
                ? double.PositiveInfinity
                : double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            long cum = long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            if (!bucketsByStage.TryGetValue(stage, out var buckets))
                bucketsByStage[stage] = buckets = [];
            buckets[le] = cum;
        }

        foreach (Match match in Regex.Matches(
                     text,
                     @"fineage_tick_stage_ms_count\{stage=""([^""]+)""\}\s+(\d+)",
                     RegexOptions.Multiline))
            counts[match.Groups[1].Value] = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (stage, buckets) in bucketsByStage)
        {
            long count = counts.GetValueOrDefault(stage);
            if (count > 0)
                result[stage] = Quantile(buckets, count, q);
        }

        return result;
    }

    static double Quantile(SortedDictionary<double, long> cumulative, long count, double q)
    {
        if (count <= 0)
            return 0;
        ToBoundsAndCumulative(cumulative, out var bounds, out var cum);
        return HistogramMath.Quantile(bounds, cum, count, q);
    }

    static double MaxFromHistogram(SortedDictionary<double, long> cumulative, long count, double sum)
    {
        if (count <= 0)
            return 0;
        ToBoundsAndCumulative(cumulative, out var bounds, out var cum);
        double fromBuckets = HistogramMath.MaxEstimate(bounds, cum, count);
        double mean = sum / count;
        return Math.Max(fromBuckets, mean);
    }

    static void ToBoundsAndCumulative(SortedDictionary<double, long> cumulative, out List<double> bounds, out List<long> cum)
    {
        bounds = [];
        cum = [];
        foreach (var (le, c) in cumulative)
        {
            if (double.IsPositiveInfinity(le))
                break;
            bounds.Add(le);
            cum.Add(c);
        }
    }

    public static HistogramStats? HistogramDelta(string beforeText, string afterText, string metric) =>
        HistogramDeltaAggregateLabeled(beforeText, afterText, metric);

    public static HistogramStats? HistogramDeltaAggregateLabeled(string beforeText, string afterText, string metric)
    {
        var labelSets = new HashSet<string>(StringComparer.Ordinal);
        string countPattern = $@"^{Regex.Escape(metric)}_count(?:\{{([^}}]+)\}})?\s+(\d+)";
        foreach (Match match in Regex.Matches(afterText, countPattern, RegexOptions.Multiline))
            labelSets.Add(match.Groups[1].Success ? match.Groups[1].Value : "");

        if (labelSets.Count == 0)
            return null;

        var merged = new SortedDictionary<double, long>();
        long totalCount = 0;
        double totalSum = 0;
        foreach (string labels in labelSets)
        {
            var before = ParseHistogramBuckets(beforeText, metric, labels);
            var after = ParseHistogramBuckets(afterText, metric, labels);
            if (before is not { } b || after is not { } a)
                continue;
            if (!HistogramMath.TryDelta(b.Buckets, a.Buckets, out var delta))
                continue;
            long count = HistogramMath.DeltaCount(delta);
            if (count <= 0)
                continue;
            totalCount += count;
            totalSum += HistogramMath.DeltaSum(a.Sum, b.Sum);
            foreach (var (le, n) in delta)
                merged[le] = merged.GetValueOrDefault(le) + n;
        }

        if (totalCount <= 0)
            return null;
        return StatsFromBuckets(merged, totalSum, totalCount);
    }

    static (SortedDictionary<double, long> Buckets, double Sum, long Count)? ParseHistogramBuckets(string text, string metric, string labels = "")
    {
        var buckets = new SortedDictionary<double, long>();
        string labelPrefix = labels.Length == 0 ? "" : Regex.Escape(labels) + ",";
        string bucketPattern = $@"^{Regex.Escape(metric)}_bucket\{{{labelPrefix}le=""([^""]+)""\}}\s+(\d+)";
        foreach (Match match in Regex.Matches(text, bucketPattern, RegexOptions.Multiline))
        {
            double le = match.Groups[1].Value == "+Inf"
                ? double.PositiveInfinity
                : double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            buckets[le] = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        string countPattern = labels.Length == 0
            ? $@"^{Regex.Escape(metric)}_count\s+(\d+)"
            : $@"^{Regex.Escape(metric)}_count\{{{Regex.Escape(labels)}\}}\s+(\d+)";
        string sumPattern = labels.Length == 0
            ? $@"^{Regex.Escape(metric)}_sum\s+([0-9.eE+\-]+)"
            : $@"^{Regex.Escape(metric)}_sum\{{{Regex.Escape(labels)}\}}\s+([0-9.eE+\-]+)";
        var countMatch = Regex.Match(text, countPattern, RegexOptions.Multiline);
        var sumMatch = Regex.Match(text, sumPattern, RegexOptions.Multiline);
        if (!countMatch.Success || buckets.Count == 0)
            return null;
        return (Buckets: buckets,
            Sum: sumMatch.Success ? double.Parse(sumMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
            Count: long.Parse(countMatch.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    static HistogramStats StatsFromBuckets(SortedDictionary<double, long> buckets, double sum, long count)
    {
        ToBoundsAndCumulative(buckets, out var bounds, out var cum);
        double mean = count > 0 ? sum / count : 0;
        return new HistogramStats(
            sum,
            count,
            mean,
            HistogramMath.Quantile(bounds, cum, count, 0.50),
            HistogramMath.Quantile(bounds, cum, count, 0.95),
            HistogramMath.Quantile(bounds, cum, count, 0.99),
            HistogramMath.Quantile(bounds, cum, count, 0.999),
            HistogramMath.MaxEstimate(bounds, cum, count));
    }

    static long ParseLong(string text, string metric)
    {
        var m = Regex.Match(text, $"^{Regex.Escape(metric)}\\s+(\\d+)", RegexOptions.Multiline);
        return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    public static long ParseCounterSumStatic(string text, string metric) => ParseCounterSum(text, metric);

    public static long CounterDeltaSum(string beforeText, string afterText, string metric) =>
        Math.Max(0, ParseCounterSum(afterText, metric) - ParseCounterSum(beforeText, metric));

    static long ParseCounterSum(string text, string metric)
    {
        long sum = 0;
        foreach (Match m in Regex.Matches(text, $"^{Regex.Escape(metric)}.*\\s+(\\d+)", RegexOptions.Multiline))
            sum += long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return sum;
    }

    static Dictionary<string, double> ParseUnlabeledQuantiles(string text, string prefix, double q)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var bucketsByMetric = new Dictionary<string, SortedDictionary<double, long>>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     text,
                     @"fineage_enter_[a-z_]+_bucket\{le=""([^""]+)""\}\s+(\d+)",
                     RegexOptions.Multiline))
        {
            string line = match.Value;
            int nameStart = line.IndexOf("fineage_enter_", StringComparison.Ordinal);
            int nameEnd = line.IndexOf("_bucket", StringComparison.Ordinal);
            if (nameStart < 0 || nameEnd < 0)
                continue;
            string metric = line[nameStart..nameEnd];
            double le = match.Groups[1].Value == "+Inf"
                ? double.PositiveInfinity
                : double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            long cum = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (!bucketsByMetric.TryGetValue(metric, out var buckets))
                bucketsByMetric[metric] = buckets = [];
            buckets[le] = cum;
        }

        foreach (Match match in Regex.Matches(
                     text,
                     @"(fineage_enter_[a-z_]+)_count\s+(\d+)",
                     RegexOptions.Multiline))
            counts[match.Groups[1].Value] = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (metric, buckets) in bucketsByMetric)
        {
            long count = counts.GetValueOrDefault(metric);
            if (count > 0)
                result[metric] = Quantile(buckets, count, q);
        }

        return result;
    }

    static double ParseDouble(string text, string pattern)
    {
        var m = Regex.Match(text, $"^{pattern}\\s+([0-9.]+)", RegexOptions.Multiline);
        return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    public static HistogramStats? LabeledHistogramDelta(
        string beforeText,
        string afterText,
        string metric,
        string labelName,
        string labelValue)
    {
        var before = ParseLabeledHistogramBuckets(beforeText, metric, labelName, labelValue);
        var after = ParseLabeledHistogramBuckets(afterText, metric, labelName, labelValue);
        if (before is not { } b || after is not { } a)
            return null;
        if (!HistogramMath.TryDelta(b.Buckets, a.Buckets, out var delta))
            return null;
        long count = HistogramMath.DeltaCount(delta);
        if (count <= 0)
            return null;
        double sum = HistogramMath.DeltaSum(a.Sum, b.Sum);
        return StatsFromBuckets(delta, sum, count);
    }

    public static HistogramStats? ParseLabeledHistogram(string text, string metric, string labelName, string labelValue)
    {
        var parsed = ParseLabeledHistogramBuckets(text, metric, labelName, labelValue);
        if (parsed is not { } p)
            return null;
        return StatsFromBuckets(p.Buckets, p.Sum, p.Count);
    }

    static (SortedDictionary<double, long> Buckets, double Sum, long Count)? ParseLabeledHistogramBuckets(
        string text,
        string metric,
        string labelName,
        string labelValue)
    {
        var buckets = new SortedDictionary<double, long>();
        string bucketPattern =
            $@"^{Regex.Escape(metric)}_bucket\{{{Regex.Escape(labelName)}=""{Regex.Escape(labelValue)}"",le=""([^""]+)""\}}\s+(\d+)";
        foreach (Match match in Regex.Matches(text, bucketPattern, RegexOptions.Multiline))
        {
            double le = match.Groups[1].Value == "+Inf"
                ? double.PositiveInfinity
                : double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            buckets[le] = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        string sumPattern = $@"^{Regex.Escape(metric)}_sum\{{{Regex.Escape(labelName)}=""{Regex.Escape(labelValue)}""\}}\s+([0-9.eE+\-]+)";
        string countPattern = $@"^{Regex.Escape(metric)}_count\{{{Regex.Escape(labelName)}=""{Regex.Escape(labelValue)}""\}}\s+(\d+)";
        var sumMatch = Regex.Match(text, sumPattern, RegexOptions.Multiline);
        var countMatch = Regex.Match(text, countPattern, RegexOptions.Multiline);
        if (!countMatch.Success || buckets.Count == 0)
            return null;
        return (Buckets: buckets,
            Sum: sumMatch.Success ? double.Parse(sumMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
            Count: long.Parse(countMatch.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    public static HistogramStats? ParseSingletonHistogram(string text, string metric)
    {
        var buckets = new SortedDictionary<double, long>();
        string bucketPattern = $@"^{Regex.Escape(metric)}_bucket\{{le=""([^""]+)""\}}\s+(\d+)";
        foreach (Match match in Regex.Matches(text, bucketPattern, RegexOptions.Multiline))
        {
            double le = match.Groups[1].Value == "+Inf"
                ? double.PositiveInfinity
                : double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            buckets[le] = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        return FinishHistogram(text, buckets, $@"^{Regex.Escape(metric)}_sum\s+([0-9.eE+\-]+)", $@"^{Regex.Escape(metric)}_count\s+(\d+)");
    }

    static HistogramStats? FinishHistogram(string text, SortedDictionary<double, long> buckets, string sumPattern, string countPattern)
    {
        var sumMatch = Regex.Match(text, sumPattern, RegexOptions.Multiline);
        var countMatch = Regex.Match(text, countPattern, RegexOptions.Multiline);
        if (!countMatch.Success || buckets.Count == 0)
            return null;
        long count = long.Parse(countMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        double sum = sumMatch.Success ? double.Parse(sumMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return StatsFromBuckets(buckets, sum, count);
    }
}