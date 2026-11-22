using System.Globalization;
using System.Text.RegularExpressions;
using Fineage.Protocol.Telemetry;

namespace Fineage.LoadTest.Metrics;

public sealed record HistogramStats(double Sum, long Count, double Mean, double P50, double P99);

public sealed class PrometheusSample
{
    public DateTimeOffset ScrapedAt { get; init; } = DateTimeOffset.UtcNow;
    public double TickP50 { get; init; }
    public double TickP95 { get; init; }
    public double TickP99 { get; init; }
    public double TickMax { get; init; }
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
    public double CommandToMailboxesP99Ms { get; init; }
    public long ReliableOverflowTotal { get; init; }
}

public static class PrometheusParser
{
    static readonly double[] Bounds = MetricsText.DurationBoundsMs;

    public static PrometheusSample Parse(string text)
    {
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
            ScrapedAt = DateTimeOffset.UtcNow,
            TickP50 = Quantile(buckets, tickCount, 0.50),
            TickP95 = Quantile(buckets, tickCount, 0.95),
            TickP99 = Quantile(buckets, tickCount, 0.99),
            TickMax = EstimateMax(buckets, tickSum, tickCount),
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
            BytesOut = ParseCounterSum(text, "fineage_bytes_out_total"),
            BytesIn = ParseCounterSum(text, "fineage_bytes_in_total"),
            LedgerAdjustments = ParseCounterSum(text, "fineage_ledger_adjustments_total"),
            LedgerFlushFailures = ParseLong(text, "fineage_ledger_flush_failures_total"),
            FanoutQueueDepth = ParseDouble(text, "fineage_fanout_queue_depth"),
            FanoutQueueOldestAgeMs = ParseSingletonP99(text, "fineage_fanout_queue_oldest_age_ms"),
            CommandToMailboxesP99Ms = ParseSingletonP99(text, "fineage_command_to_mailboxes_ms"),
            ReliableOverflowTotal = ParseCounterSum(text, "fineage_reliable_overflow_total")
        };
    }

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
        long rank = (long)Math.Ceiling(q * count);
        foreach (var (le, cum) in cumulative)
        {
            if (cum >= rank)
                return double.IsPositiveInfinity(le) ? Bounds[^1] : le;
        }

        return 0;
    }

    static double EstimateMax(SortedDictionary<double, long> cumulative, double sum, long count) =>
        count > 0 ? Math.Max(sum / count * 4, Quantile(cumulative, count, 0.99)) : 0;

    static long ParseLong(string text, string metric)
    {
        var m = Regex.Match(text, $"^{Regex.Escape(metric)}\\s+(\\d+)", RegexOptions.Multiline);
        return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    public static long ParseCounterSumStatic(string text, string metric) => ParseCounterSum(text, metric);

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

    public static HistogramStats? ParseLabeledHistogram(string text, string metric, string labelName, string labelValue)
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
        return FinishHistogram(text, buckets, sumPattern, countPattern);
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
        return new HistogramStats(sum, count, count > 0 ? sum / count : 0, Quantile(buckets, count, 0.50), Quantile(buckets, count, 0.99));
    }
}