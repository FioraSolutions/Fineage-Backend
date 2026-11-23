using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Consistency;
using Fineage.LoadTest.Metrics;

namespace Fineage.LoadTest.Reporting;

public sealed class RunReport
{
    public required string ScenarioName { get; init; }
    public required string Target { get; init; }
    public int RegionWorkers { get; init; }
    public string SpawnLayout { get; init; } = "";
    public int BotCount { get; init; }
    public int BotsInWorld { get; init; }
    public int PeakZoneOnline { get; init; }
    public int BotsDisconnected { get; init; }
    public int BotsStuck { get; init; }
    public long RelogsAttempted { get; init; }
    public long RelogsSucceeded { get; init; }
    public double ClientRttP50Ms { get; init; }
    public double ClientRttP95Ms { get; init; }
    public double ClientRttP99Ms { get; init; }
    public double ClientRttMaxMs { get; init; }
    public PrometheusAggregate? Server { get; init; }
    public string? PeakMetricsText { get; init; }
    public ProcessSample? ZoneProcess { get; init; }
    public List<LogIssue> LogIssues { get; init; } = [];
    public bool Passed { get; set; }
    public List<string> FailReasons { get; set; } = [];
    public List<string> Bugs { get; init; } = [];
    public ConsistencyReport? Consistency { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset SteadyStateStartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }

    public RunEnvironmentInfo? Environment { get; set; }
    public int DurationSeconds { get; set; }
    public int RampUpSeconds { get; set; }
    public List<MetricLatencyRow> LatencyTable { get; set; } = [];
    public List<string> MissingExpectedMetrics { get; set; } = [];
    public List<TimeSeriesWindow> TimeSeriesWindows { get; set; } = [];
    public string? TimeSeriesBacklogTrend { get; set; }
    public RuntimeCounterStats? Runtime { get; set; }
    public StructuredLogSummary? StructuredLogs { get; set; }
    public List<GateVerdict> GateVerdicts { get; set; } = [];
    public double? CommandToMailboxesP99Ms { get; set; }
    public double? FanoutQueueOldestAgeP99Ms { get; set; }
    public long? ReliableOverflowTotal { get; set; }
    public HonestLoadSummary? Honest { get; set; }

    /// <summary>Longest tick freeze or step-watchdog stall seen in the run, in seconds.</summary>
    public double SimulationStallSeconds { get; set; }

    /// <summary>Tick-freeze episodes over the stall limit. A step-watchdog hit counts as one when the tick series did not.</summary>
    public int SimulationStallCount { get; set; }
}

public sealed class PrometheusAggregate
{
    public double TickP50Ms { get; init; }
    public double TickP95Ms { get; init; }
    public double TickP99Ms { get; init; }
    public double TickMaxMs { get; init; }
    public long TickOverruns { get; init; }
    public long TicksSkipped { get; init; }
    public long CommandIngressQueuedMax { get; init; }
    public double InPpsAvg { get; init; }
    public double OutPpsAvg { get; init; }
    public long PacketsRejected { get; init; }
    public long PacketsErrored { get; init; }
    public int ZoneOnlineMax { get; init; }
    public long OutboundDropped { get; init; }
    public Dictionary<string, double> TickStageP99Ms { get; init; } = new(StringComparer.Ordinal);
    public double BytesOutPerOnlinePlayer { get; init; }
    public long LedgerAdjustments { get; init; }

    /// <summary>Max sampled p99 of the <c>commands</c> simulation stage (server-side).</summary>
    public double CommandsStageP99Ms { get; init; }

    public double EnterPlanP99Ms { get; init; }
    public double EnterKnownEncodeP99Ms { get; init; }
    public double EnterSelfProjectionP99Ms { get; init; }
    public double EnterKnownSnapshotP99Ms { get; init; }
    public double EnterPeerFanoutP99Ms { get; init; }
    public double EnterTryP99Ms { get; init; }
    public double EnterTryAoiBindP99Ms { get; init; }
    public double CommandsStageP50Ms { get; init; }
    public double CommandsStageMeanMs { get; init; }
    public Dictionary<string, HistogramStats> EnterPhaseStats { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Peak p99 across stages for keys present in any scrape (load-test diagnostics).</summary>
    public IReadOnlyDictionary<string, double> StageP99MsPeak { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);

    public static PrometheusAggregate FromSamples(IReadOnlyList<PrometheusSample> samples, string? peakMetricsText = null)
    {
        if (samples.Count == 0)
            return new PrometheusAggregate();
        var stagePeak = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            foreach (var (stage, p99) in sample.TickStageP99Ms)
                stagePeak[stage] = Math.Max(p99, stagePeak.GetValueOrDefault(stage));
        }

        var enterStats = ParseEnterPhaseStats(peakMetricsText);
        HistogramStats? commands = peakMetricsText == null
            ? null
            : PrometheusParser.ParseLabeledHistogram(peakMetricsText, "fineage_tick_stage_ms", "stage", "commands");

        return new PrometheusAggregate
        {
            TickP50Ms = Percentile(samples.Select(s => s.TickP50), 0.5),
            TickP95Ms = Percentile(samples.Select(s => s.TickP95), 0.95),
            TickP99Ms = Percentile(samples.Select(s => s.TickP99), 0.99),
            TickMaxMs = samples.Max(s => s.TickMax),
            TickOverruns = samples.Max(s => s.TickOverruns),
            TicksSkipped = samples.Max(s => s.TicksSkipped),
            CommandIngressQueuedMax = samples.Max(s => s.CommandIngressQueued),
            InPpsAvg = samples.Average(s => s.InPps),
            OutPpsAvg = samples.Average(s => s.OutPps),
            PacketsRejected = samples.Max(s => s.PacketsRejected),
            PacketsErrored = samples.Max(s => s.PacketsErrored),
            ZoneOnlineMax = samples.Max(s => s.ZoneOnline),
            OutboundDropped = samples.Max(s => s.OutboundDropped),
            TickStageP99Ms = MergeStageP99(samples),
            LedgerAdjustments = samples.Max(s => s.LedgerAdjustments),
            BytesOutPerOnlinePlayer = PerPlayerBytes(samples),
            CommandsStageP99Ms = stagePeak.GetValueOrDefault("commands"),
            StageP99MsPeak = stagePeak,
            EnterPlanP99Ms = EnterPeak(samples, "fineage_enter_plan_ms"),
            EnterSelfProjectionP99Ms = EnterPeak(samples, "fineage_enter_self_projection_ms"),
            EnterKnownSnapshotP99Ms = EnterPeak(samples, "fineage_enter_known_snapshot_ms"),
            EnterKnownEncodeP99Ms = EnterPeak(samples, "fineage_enter_known_encode_ms"),
            EnterPeerFanoutP99Ms = EnterPeak(samples, "fineage_enter_peer_fanout_ms"),
            EnterTryP99Ms = EnterPeak(samples, "fineage_enter_try_ms"),
            EnterTryAoiBindP99Ms = EnterPeak(samples, "fineage_enter_try_aoi_bind_ms"),
            CommandsStageP50Ms = commands?.P50 ?? 0,
            CommandsStageMeanMs = commands?.Mean ?? 0,
            EnterPhaseStats = enterStats
        };
    }

    static Dictionary<string, HistogramStats> ParseEnterPhaseStats(string? peakMetricsText)
    {
        var map = new Dictionary<string, HistogramStats>(StringComparer.Ordinal);
        if (peakMetricsText == null)
            return map;
        string[] metrics =
        [
            "fineage_enter_try_ms",
            "fineage_enter_try_aoi_bind_ms",
            "fineage_enter_plan_ms",
            "fineage_enter_self_projection_ms",
            "fineage_enter_known_snapshot_ms",
            "fineage_enter_known_encode_ms",
            "fineage_enter_peer_fanout_ms"
        ];
        foreach (string metric in metrics)
        {
            if (PrometheusParser.ParseSingletonHistogram(peakMetricsText, metric) is { } stats)
                map[metric] = stats;
        }

        if (PrometheusParser.ParseLabeledHistogram(peakMetricsText, "fineage_tick_stage_ms", "stage", "commands") is { } commands)
            map["fineage_tick_stage_ms/commands"] = commands;
        return map;
    }

    static double EnterPeak(IReadOnlyList<PrometheusSample> samples, string metric)
    {
        double peak = 0;
        foreach (var sample in samples)
            peak = Math.Max(peak, sample.EnterP99Ms.GetValueOrDefault(metric));
        return peak;
    }

    static double PerPlayerBytes(IReadOnlyList<PrometheusSample> samples)
    {
        if (samples.Count == 0)
            return 0;
        long bytes = samples.Max(s => s.BytesOut);
        int online = Math.Max(1, samples.Max(s => s.ZoneOnline));
        return bytes / (double)online;
    }

    static Dictionary<string, double> MergeStageP99(IReadOnlyList<PrometheusSample> samples)
    {
        var merged = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            foreach (var (stage, p99) in sample.TickStageP99Ms)
                merged[stage] = Math.Max(merged.GetValueOrDefault(stage), p99);
        }

        return merged;
    }

    static double Percentile(IEnumerable<double> values, double q)
    {
        var sorted = values.Where(v => v > 0).OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            return 0;
        int idx = (int)Math.Ceiling(q * sorted.Count) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }
}

public static class ReportWriter
{
    public static void Write(RunReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        if (report.PeakMetricsText != null)
            File.WriteAllText(Path.Combine(directory, "metrics-peak.txt"), report.PeakMetricsText);

        string jsonPath = Path.Combine(directory, "report.json");
        string mdPath = Path.Combine(directory, "report.md");
        string json = System.Text.Json.JsonSerializer.Serialize(report,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        string md = CompactMarkdownRenderer.Render(report);
        File.WriteAllText(jsonPath, json);
        File.WriteAllText(mdPath, md);
        File.WriteAllText(Path.Combine(directory, "load-test-report.json"), json);
        File.WriteAllText(Path.Combine(directory, "load-test-report.md"), md);
    }

    static string ToMarkdownLegacy(RunReport r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Load test: {r.ScenarioName}");
        sb.AppendLine();
        sb.AppendLine($"- Target: **{r.Target}**");
        sb.AppendLine($"- Region workers: **{r.RegionWorkers}**");
        sb.AppendLine($"- Spawn layout: **{r.SpawnLayout}**");
        sb.AppendLine($"- Bots: **{r.BotCount}** (in world {r.BotsInWorld}, disconnected {r.BotsDisconnected}, stuck {r.BotsStuck})");
        sb.AppendLine($"- Result: **{(r.Passed ? "PASS" : "FAIL")}**");
        if (r.FailReasons.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Fail reasons");
            foreach (string reason in r.FailReasons)
                sb.AppendLine($"- {reason}");
        }

        sb.AppendLine();
        sb.AppendLine("## Client RTT (action timing)");
        sb.AppendLine("| p50 | p95 | p99 (command→fanout) | max |");
        sb.AppendLine("| ---: | ---: | ---: | ---: |");
        sb.AppendLine($"| {r.ClientRttP50Ms:0.0} | {r.ClientRttP95Ms:0.0} | {r.ClientRttP99Ms:0.0} | {r.ClientRttMaxMs:0.0} |");

        if (r.Server != null)
        {
            sb.AppendLine();
            sb.AppendLine("## Server metrics (zone)");
            sb.AppendLine($"| tick p50 | p95 | p99 | max | overruns | skipped | ingress max | in pps | out pps |");
            sb.AppendLine($"| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            sb.AppendLine(
                $"| {r.Server.TickP50Ms:0.0} | {r.Server.TickP95Ms:0.0} | {r.Server.TickP99Ms:0.0} | {r.Server.TickMaxMs:0.0} | {r.Server.TickOverruns} | {r.Server.TicksSkipped} | {r.Server.CommandIngressQueuedMax} | {r.Server.InPpsAvg:0.0} | {r.Server.OutPpsAvg:0.0} |");
            if (r.Server.OutboundDropped > 0)
                sb.AppendLine($"- Outbound queue drops: **{r.Server.OutboundDropped}**");
            sb.AppendLine();
            sb.AppendLine($"commands stage p99 (server): **{r.Server.CommandsStageP99Ms:0.0} ms**");
            if (r.Server.TickStageP99Ms.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### Tick stage p99 (ms)");
                foreach (var (stage, ms) in r.Server.TickStageP99Ms.OrderByDescending(kv => kv.Value).Take(12))
                    sb.AppendLine($"- `{stage}`: {ms:0.0}");
            }
        }

        if (r.ZoneProcess != null)
        {
            sb.AppendLine();
            sb.AppendLine("## Zone process");
            sb.AppendLine($"- RSS: {r.ZoneProcess.RssBytes / (1024 * 1024)} MiB");
            sb.AppendLine($"- VmHWM: {r.ZoneProcess.VmHwmKb / 1024} MiB");
        }

        if (r.LogIssues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Log signals");
            sb.AppendLine("| kind | count | sample |");
            sb.AppendLine("| --- | ---: | --- |");
            foreach (LogIssue issue in r.LogIssues)
                sb.AppendLine($"| {issue.Key} | {issue.Count} | `{issue.SampleLine?.Replace('|', '/')}` |");
        }

        if (r.Bugs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Bugs (prioritized)");
            int n = 1;
            foreach (string bug in r.Bugs)
                sb.AppendLine($"{n++}. {bug}");
        }

        if (r.Consistency != null)
        {
            sb.AppendLine();
            sb.AppendLine("## Consistency oracle");
            sb.AppendLine($"- Pairwise AOI checks: **{r.Consistency.PairwiseObserversCompared}**");
            sb.AppendLine($"- Issues: **{r.Consistency.Issues.Count}** ({(r.Consistency.Passed ? "PASS" : "FAIL")})");
            sb.AppendLine($"- Command→fanout latency ms: p50 **{r.Consistency.BroadcastLatencyP50Ms:0.0}**, p95 **{r.Consistency.BroadcastLatencyP95Ms:0.0}**, p99 **{r.Consistency.BroadcastLatencyP99Ms:0.0}**");
            if (r.Consistency.Issues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("| kind | observer | detail |");
                sb.AppendLine("| --- | ---: | --- |");
                foreach (var issue in r.Consistency.Issues.Take(40))
                    sb.AppendLine($"| {issue.Kind} | {issue.ObserverBotId} | {issue.Detail.Replace('|', '/')} |");
            }
        }

        return sb.ToString();
    }

    public static (double p50, double p95, double p99, double max) ClientRtt(IReadOnlyList<BotStats> bots)
    {
        var samples = bots.SelectMany(b =>
        {
            if (b.RttSamples <= 0)
                return [];
            return Enumerable.Repeat(b.RttAvgMs, (int)Math.Min(b.RttSamples, 100));
        }).OrderBy(v => v).ToList();
        if (samples.Count == 0)
            return (0, 0, 0, bots.Max(b => b.RttMaxMs));
        double At(double q)
        {
            int idx = (int)Math.Ceiling(q * samples.Count) - 1;
            return samples[Math.Clamp(idx, 0, samples.Count - 1)];
        }

        return (At(0.5), At(0.95), At(0.99), samples[^1]);
    }
}