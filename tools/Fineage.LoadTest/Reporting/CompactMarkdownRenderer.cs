using System.Text;

namespace Fineage.LoadTest.Reporting;

public static class CompactMarkdownRenderer
{
    public const int TargetMaxBytes = 30 * 1024;

    public static string Render(RunReport r)
    {
        var sb = new StringBuilder(16_000);
        sb.AppendLine($"# Load test report: `{r.ScenarioName}`");
        sb.AppendLine();
        sb.AppendLine($"**Overall:** {(r.Passed && r.GateVerdicts.All(g => g.Passed) ? "PASS" : "FAIL")}  ");
        sb.AppendLine($"**Window:** {r.StartedAt:u} → {r.FinishedAt:u} ({(r.FinishedAt - r.StartedAt).TotalSeconds:0}s)");
        sb.AppendLine();

        AppendHeader(sb, r);
        AppendLatencyTable(sb, r);
        AppendTimeSeries(sb, r);
        AppendRuntime(sb, r);
        AppendLogs(sb, r);
        AppendBots(sb, r);
        AppendGates(sb, r);
        AppendMissingMetrics(sb, r);

        if (r.FailReasons.Count > 0)
        {
            sb.AppendLine("## Fail reasons (runner)");
            foreach (string reason in r.FailReasons)
                sb.AppendLine($"- {reason}");
            sb.AppendLine();
        }

        if (r.Bugs.Count > 0)
        {
            sb.AppendLine("## Bugs (prioritized)");
            int n = 1;
            foreach (string bug in r.Bugs.Take(8))
                sb.AppendLine($"{n++}. {bug}");
            sb.AppendLine();
        }

        string text = sb.ToString();
        if (Encoding.UTF8.GetByteCount(text) > TargetMaxBytes)
            text = TruncateUtf8(text, TargetMaxBytes) + "\n\n<!-- truncated to ~30 KiB for LLM paste -->\n";
        return text;
    }

    static void AppendHeader(StringBuilder sb, RunReport r)
    {
        sb.AppendLine("## Run header");
        var e = r.Environment;
        if (e == null)
        {
            sb.AppendLine("- **Environment:** MISSING — RunEnvironmentCollector did not run");
        }
        else
        {
            sb.AppendLine($"| Field | Value |");
            sb.AppendLine($"| --- | --- |");
            sb.AppendLine($"| git commit | `{e.GitCommit ?? "MISSING"}`{(e.GitDirty ? " (dirty)" : "")} |");
            sb.AppendLine($"| branch | `{e.GitBranch ?? "MISSING"}` |");
            sb.AppendLine($"| build | **{e.BuildConfiguration}** |");
            sb.AppendLine($"| scenario | **{r.ScenarioName}** |");
            sb.AppendLine($"| bots | **{r.BotCount}** (in world {r.BotsInWorld}, peak online {r.PeakZoneOnline}) |");
            sb.AppendLine($"| region workers | **{r.RegionWorkers}** |");
            sb.AppendLine($"| spawn layout | **{r.SpawnLayout}** |");
            sb.AppendLine($"| ramp + duration | **{r.RampUpSeconds}**s + **{r.DurationSeconds}**s |");
            sb.AppendLine($"| machine | {e.ProcessorCount} cores, {e.TotalRamMb} MiB RAM, {e.OsDescription} |");
            sb.AppendLine($"| .NET | {e.DotNetVersion} |");
            sb.AppendLine($"| GC | **{e.GcMode}** |");
            sb.AppendLine($"| DOTNET_GCDynamicAdaptationMode | `{e.DotnetGcDynamicAdaptation ?? "(unset)"}` |");
            sb.AppendLine($"| DOTNET_TieredPGO | `{e.DotnetTieredPgo ?? "(unset)"}` |");
        }

        sb.AppendLine();
    }

    static void AppendLatencyTable(StringBuilder sb, RunReport r)
    {
        sb.AppendLine("## Latency (peak scrape + zone slow-command log)");
        if (r.LatencyTable.Count == 0)
        {
            sb.AppendLine("MISSING — no peak Prometheus scrape or zone log samples.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| metric | p50 | p95 | p99 | max | count | source |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var row in r.LatencyTable.Take(48))
        {
            sb.AppendLine(
                $"| `{row.Name}` | {row.P50Ms:0.0} | {row.P95Ms:0.0} | {row.P99Ms:0.0} | {row.MaxMs:0.0} | {row.Count} | {row.Source} |");
        }

        if (r.Server != null)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"Aggregate tick (scrapes): p50 **{r.Server.TickP50Ms:0.0}** p95 **{r.Server.TickP95Ms:0.0}** p99 **{r.Server.TickP99Ms:0.0}** max **{r.Server.TickMaxMs:0.0}**; ingress max **{r.Server.CommandIngressQueuedMax}**; outbound drops **{r.Server.OutboundDropped}**.");
        }

        sb.AppendLine();
    }

    static void AppendTimeSeries(StringBuilder sb, RunReport r)
    {
        sb.AppendLine($"## Time series ({TimeSeriesSummaryBuilder.WindowSeconds}s windows)");
        sb.AppendLine($"Fanout queue depth trend: **{r.TimeSeriesBacklogTrend ?? "MISSING"}**");
        if (r.TimeSeriesWindows.Count == 0)
        {
            sb.AppendLine("MISSING — fewer than one Prometheus sample window.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| start (UTC) | tick p99 | commands p99 | cmd→mailboxes p99 | fanout depth max |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        foreach (var w in r.TimeSeriesWindows.Take(24))
        {
            sb.AppendLine(
                $"| {w.WindowStart:HH:mm:ss} | {w.TickP99Ms:0.0} | {w.CommandsStageP99Ms:0.0} | {w.CommandToMailboxesP99Ms:0.0} | {w.FanoutQueueDepthMax:0} |");
        }

        sb.AppendLine();
    }

    static void AppendRuntime(StringBuilder sb, RunReport r)
    {
        sb.AppendLine("## Runtime (System.Runtime)");
        var rt = r.Runtime;
        if (rt == null)
        {
            sb.AppendLine("MISSING — runtime sampler did not run.");
            sb.AppendLine();
            return;
        }

        if (rt.UnavailableReason != null)
            sb.AppendLine($"- **Note:** {rt.Source}: {rt.UnavailableReason}");
        else
            sb.AppendLine($"- Source: **{rt.Source}**");
        sb.AppendLine("| metric | min | avg | max |");
        sb.AppendLine("| --- | ---: | ---: | ---: |");
        sb.AppendLine($"| CPU % | {rt.CpuPercentMin:0.0} | {rt.CpuPercentAvg:0.0} | {rt.CpuPercentMax:0.0} |");
        sb.AppendLine($"| alloc rate (MiB/s) | — | {rt.AllocRateMbPerSecAvg:0.1} | — |");
        sb.AppendLine($"| GC heap (MiB) | — | — | {rt.GcHeapMbMax:0.0} |");
        sb.AppendLine($"| time in GC % | — | — | {rt.TimeInGcPercentMax:0.0} |");
        sb.AppendLine($"| gen0/1/2 Δ | — | — | {rt.Gen0CountDelta}/{rt.Gen1CountDelta}/{rt.Gen2CountDelta} |");
        sb.AppendLine($"| threadpool queue | — | — | {rt.ThreadPoolQueueMax:0} |");
        sb.AppendLine($"| threadpool threads | — | — | {rt.ThreadPoolThreadsMax:0} |");
        sb.AppendLine($"| lock contention | — | — | {rt.LockContentionMax:0} |");
        if (r.ZoneProcess != null)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"Zone RSS **{r.ZoneProcess.RssBytes / (1024 * 1024)}** MiB, VmHWM **{r.ZoneProcess.VmHwmKb / 1024}** MiB (proc snapshot).");
        }

        sb.AppendLine();
    }

    static void AppendLogs(StringBuilder sb, RunReport r)
    {
        sb.AppendLine("## Logs (errors / warnings)");
        var logs = r.StructuredLogs;
        if (logs == null)
        {
            sb.AppendLine("MISSING — structured log scan did not run.");
            sb.AppendLine();
            return;
        }

        if (logs.MissingLogPaths != null)
            sb.AppendLine($"**Missing log files:** {logs.MissingLogPaths}");

        if (logs.Counts.Count == 0)
            sb.AppendLine("No Error/Warning lines matched the level detector.");
        else
        {
            sb.AppendLine("| level | count | template |");
            sb.AppendLine("| --- | ---: | --- |");
            foreach (var c in logs.Counts.Take(20))
                sb.AppendLine($"| {c.Level} | {c.Count} | `{c.Template.Replace('|', '/')}` |");
        }

        if (logs.FirstErrors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### First distinct errors");
            foreach (var err in logs.FirstErrors.Take(10))
                sb.AppendLine($"- `{err.Template}` → `{err.FirstLine.Replace('|', '/')}`");
        }

        if (r.LogIssues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Legacy log signals");
            foreach (var issue in r.LogIssues.Take(8))
                sb.AppendLine($"- **{issue.Key}**: {issue.Count} (`{issue.SampleLine?.Replace('|', '/')}`)");
        }

        sb.AppendLine();
    }

    static void AppendBots(StringBuilder sb, RunReport r)
    {
        sb.AppendLine("## Bot side");
        sb.AppendLine($"| in world | disconnects | stuck | client RTT p50/p99 | oracle cmd→fanout p50/p99 |");
        sb.AppendLine($"| ---: | ---: | ---: | --- | --- |");
        double o50 = r.Consistency?.BroadcastLatencyP50Ms ?? 0;
        double o99 = r.Consistency?.BroadcastLatencyP99Ms ?? 0;
        sb.AppendLine(
            $"| {r.BotsInWorld} | {r.BotsDisconnected} | {r.BotsStuck} | {r.ClientRttP50Ms:0.0} / {r.ClientRttP99Ms:0.0} ms | {o50:0.0} / {o99:0.0} ms |");
        if (r.Consistency != null)
        {
            sb.AppendLine(
                $"- Consistency oracle: **{r.Consistency.PairwiseObserversCompared}** pairwise checks, **{r.Consistency.Issues.Count}** issues ({(r.Consistency.Passed ? "PASS" : "FAIL")}).");
        }

        sb.AppendLine();
    }

    static void AppendGates(StringBuilder sb, RunReport r)
    {
        sb.AppendLine("## Verdict vs #867 gates");
        if (r.GateVerdicts.Count == 0)
        {
            sb.AppendLine("MISSING — gate evaluation did not run.");
            sb.AppendLine();
            return;
        }

        foreach (var g in r.GateVerdicts)
        {
            string mark = g.Passed ? "PASS" : "FAIL";
            string obs = g.Unit == "ms" ? $"{g.Observed:0.0} ms" : $"{g.Observed:0}";
            string lim = g.Limit.HasValue ? $" (limit {g.Limit:0}{g.Unit switch { "ms" => " ms", _ => "" }})" : "";
            sb.AppendLine($"- **{mark}** — {g.Gate}: observed **{obs}**{lim}");
            if (g.Note != null)
                sb.AppendLine($"  - {g.Note}");
        }

        sb.AppendLine();
    }

    static void AppendMissingMetrics(StringBuilder sb, RunReport r)
    {
        sb.AppendLine("## Missing expected Prometheus metrics");
        if (r.PeakMetricsText == null)
        {
            sb.AppendLine("MISSING — peak metrics text was not captured.");
            sb.AppendLine();
            return;
        }

        if (r.MissingExpectedMetrics.Count == 0)
        {
            sb.AppendLine("All catalogued #862 metrics present on peak scrape.");
        }
        else
        {
            sb.AppendLine($"Absent on peak scrape ({r.MissingExpectedMetrics.Count}):");
            foreach (string name in r.MissingExpectedMetrics.Take(40))
                sb.AppendLine($"- `{name}`");
            if (r.MissingExpectedMetrics.Count > 40)
                sb.AppendLine($"- … and {r.MissingExpectedMetrics.Count - 40} more");
        }

        sb.AppendLine();
    }

    static string TruncateUtf8(string text, int maxBytes)
    {
        var enc = Encoding.UTF8;
        if (enc.GetByteCount(text) <= maxBytes)
            return text;
        int lo = 0;
        int hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (enc.GetByteCount(text[..mid]) <= maxBytes)
                lo = mid;
            else
                hi = mid - 1;
        }

        return text[..lo];
    }
}