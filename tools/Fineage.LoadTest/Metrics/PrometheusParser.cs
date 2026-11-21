using System.Globalization;
using System.Text.RegularExpressions;
using Fineage.Protocol.Telemetry;

namespace Fineage.LoadTest.Metrics;

public sealed class PrometheusSample
{
    public double TickP50 { get; init; }
    public double TickP95 { get; init; }
    public double TickP99 { get; init; }
    public double TickMax { get; init; }
    public double StepFanoutDrainP99Ms { get; init; }
    public long TickOverruns { get; init; }
    public long TicksSkipped { get; init; }
    public long CommandIngressQueued { get; init; }
    public int ZoneOnline { get; init; }
    public double InPps { get; init; }
    public double OutPps { get; init; }
    public long PacketsRejected { get; init; }
    public long PacketsErrored { get; init; }
    public Dictionary<string, double> TickStageP99Ms { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> CommandApplyP99Ms { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> MovePhaseP99Ms { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> CommandReleaseP99Ms { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> CommandsDeferred { get; init; } = new(StringComparer.Ordinal);
    public long OutboundDropped { get; init; }
    public long BytesOut { get; init; }
    public long BytesIn { get; init; }
    public long LedgerAdjustments { get; init; }
    public long LedgerFlushFailures { get; init; }
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
            TickP50 = Quantile(buckets, tickCount, 0.50),
            TickP95 = Quantile(buckets, tickCount, 0.95),
            TickP99 = Quantile(buckets, tickCount, 0.99),
            TickMax = EstimateMax(buckets, tickSum, tickCount),
            StepFanoutDrainP99Ms = ParseSimpleHistogramP99(text, "fineage_tick_step_fanout_drain_ms"),
            TickOverruns = ParseLong(text, "fineage_tick_overruns_total"),
            TicksSkipped = ParseLong(text, "fineage_tick_skipped_total"),
            CommandIngressQueued = ParseLong(text, "fineage_command_ingress_queued"),
            ZoneOnline = (int)ParseLong(text, "fineage_zone_online"),
            InPps = ParseDouble(text, @"fineage_packets_per_second\{direction=""in""\}"),
            OutPps = ParseDouble(text, @"fineage_packets_per_second\{direction=""out""\}"),
            PacketsRejected = ParseCounterSum(text, "fineage_packets_rejected_total"),
            PacketsErrored = ParseCounterSum(text, "fineage_packets_errored_total"),
            TickStageP99Ms = ParseStageP99(text),
            CommandApplyP99Ms = ParseLabeledP99(text, "fineage_command_apply_ms", "command"),
            MovePhaseP99Ms = ParseLabeledP99(text, "fineage_command_move_phase_ms", "phase"),
            CommandReleaseP99Ms = ParseLabeledP99(text, "fineage_command_release_phase_ms", "phase"),
            CommandsDeferred = ParseLabeledCounters(text, "fineage_command_deferred_total", "reason"),
            OutboundDropped = ParseCounterSum(text, "fineage_outbound_dropped_total"),
            BytesOut = ParseCounterSum(text, "fineage_bytes_out_total"),
            BytesIn = ParseCounterSum(text, "fineage_bytes_in_total"),
            LedgerAdjustments = ParseCounterSum(text, "fineage_ledger_adjustments_total"),
            LedgerFlushFailures = ParseLong(text, "fineage_ledger_flush_failures_total")
        };
    }

    static Dictionary<string, double> ParseLabeledP99(string text, string metric, string labelKey) =>
        ParseLabeledP99(text, metric, labelKey, $"{metric}_bucket\\{{{labelKey}=\"([^\"]+)\",le=\"([^\"]+)\"\\}}\\s+(\\d+)");

    static Dictionary<string, double> ParseLabeledP99(string text, string metric, string labelKey, string bucketPattern)
    {
        var stages = new Dictionary<string, List<(SortedDictionary<double, long> Buckets, long Count)>>
            (StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(text, bucketPattern, RegexOptions.Multiline))
        {
            string stage = match.Groups[1].Value;
            double le = match.Groups[2].Value == "+Inf"
                ? double.PositiveInfinity
                : double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            long cum = long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            if (!stages.TryGetValue(stage, out var list))
            {
                list = [];
                stages[stage] = list;
            }

            if (list.Count == 0)
                list.Add((new SortedDictionary<double, long>(), 0));
            list[0].Buckets[le] = cum;
        }

        foreach (Match match in Regex.Matches(text, $@"{Regex.Escape(metric)}_count\\{{{labelKey}=""([^""]+)""\}}\\s+(\\d+)",
                     RegexOptions.Multiline))
        {
            string stage = match.Groups[1].Value;
            long count = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (!stages.TryGetValue(stage, out var list))
            {
                list = [(new SortedDictionary<double, long>(), count)];
                stages[stage] = list;
            }
            else
                list[0] = (list[0].Buckets, count);
        }

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (stage, list) in stages)
        {
            if (list.Count == 0)
                continue;
            var (buckets, count) = list[0];
            result[stage] = Quantile(buckets, count, 0.99);
        }

        return result;
    }

    static Dictionary<string, double> ParseStageP99(string text) =>
        ParseLabeledP99(text, "fineage_tick_stage_ms", "stage",
            @"fineage_tick_stage_ms_bucket\{stage=""([^""]+)"",le=""([^""]+)""\}\s+(\d+)");

    static double ParseSimpleHistogramP99(string text, string metric)
    {
        var buckets = new SortedDictionary<double, long>();
        long count = ParseLong(text, $"{metric}_count");
        foreach (Match match in Regex.Matches(text,
                     $@"{Regex.Escape(metric)}_bucket{{le=""([^""]+)""}}\s+(\d+)", RegexOptions.Multiline))
        {
            double le = match.Groups[1].Value == "+Inf"
                ? double.PositiveInfinity
                : double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            buckets[le] = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        return Quantile(buckets, count, 0.99);
    }

    static Dictionary<string, long> ParseLabeledCounters(string text, string metric, string labelKey)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(text,
                     $"^{Regex.Escape(metric)}\\{{{labelKey}=\"([^\"]+)\"\\}}\\s+(\\d+)", RegexOptions.Multiline))
            result[match.Groups[1].Value] = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
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

    static long ParseCounterSum(string text, string metric)
    {
        long sum = 0;
        foreach (Match m in Regex.Matches(text, $"^{Regex.Escape(metric)}.*\\s+(\\d+)", RegexOptions.Multiline))
            sum += long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return sum;
    }

    static double ParseDouble(string text, string pattern)
    {
        var m = Regex.Match(text, $"^{pattern}\\s+([0-9.]+)", RegexOptions.Multiline);
        return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }
}