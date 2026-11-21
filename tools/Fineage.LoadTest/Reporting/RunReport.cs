using Fineage.LoadTest.Bot;
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
    public int BotsDisconnected { get; init; }
    public int BotsStuck { get; init; }
    public double ClientRttP50Ms { get; init; }
    public double ClientRttP95Ms { get; init; }
    public double ClientRttP99Ms { get; init; }
    public double ClientRttMaxMs { get; init; }
    public PrometheusAggregate? Server { get; init; }
    public ProcessSample? ZoneProcess { get; init; }
    public List<LogIssue> LogIssues { get; init; } = [];
    public bool Passed { get; init; }
    public List<string> FailReasons { get; init; } = [];
    public List<string> Bugs { get; init; } = [];
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
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

    public static PrometheusAggregate FromSamples(IReadOnlyList<PrometheusSample> samples)
    {
        if (samples.Count == 0)
            return new PrometheusAggregate();
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
            ZoneOnlineMax = samples.Max(s => s.ZoneOnline)
        };
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
        string jsonPath = Path.Combine(directory, "report.json");
        string mdPath = Path.Combine(directory, "report.md");
        File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(report,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(mdPath, ToMarkdown(report));
    }

    static string ToMarkdown(RunReport r)
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
        sb.AppendLine($"| p50 | p95 | p99 | max |");
        sb.AppendLine($"| ---: | ---: | ---: | ---: |");
        sb.AppendLine($"| {r.ClientRttP50Ms:0.0} | {r.ClientRttP95Ms:0.0} | {r.ClientRttP99Ms:0.0} | {r.ClientRttMaxMs:0.0} |");

        if (r.Server != null)
        {
            sb.AppendLine();
            sb.AppendLine("## Server metrics (zone)");
            sb.AppendLine($"| tick p50 | p95 | p99 | max | overruns | skipped | ingress max | in pps | out pps |");
            sb.AppendLine($"| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            sb.AppendLine(
                $"| {r.Server.TickP50Ms:0.0} | {r.Server.TickP95Ms:0.0} | {r.Server.TickP99Ms:0.0} | {r.Server.TickMaxMs:0.0} | {r.Server.TickOverruns} | {r.Server.TicksSkipped} | {r.Server.CommandIngressQueuedMax} | {r.Server.InPpsAvg:0.0} | {r.Server.OutPpsAvg:0.0} |");
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