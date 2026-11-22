using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Consistency;
using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Models;
using Fineage.LoadTest.Reporting;

namespace Fineage.LoadTest;

public sealed class LoadTestRunner
{
    public async Task<RunReport> RunAsync(
        LoadScenario scenario,
        string target,
        int regionWorkers,
        string? loginLog,
        string? zoneLog,
        int? zonePid,
        CancellationToken ct)
    {
        if (target == "mobius")
            return MobiusRunReport.ScenarioNotSupported(scenario, regionWorkers);

        var started = DateTimeOffset.UtcNow;
        var peers = new BotPeerDirectory();
        var bots = new List<FineageBotClient>();
        var stats = new List<BotStats>();

        RuntimeCountersSampler? runtimeSampler = zonePid is > 0 ? new RuntimeCountersSampler(zonePid.Value) : null;
        if (runtimeSampler != null)
            await runtimeSampler.StartAsync(ct);

        for (int i = 0; i < scenario.BotCount; i++)
        {
            var botStats = new BotStats { BotId = i, Account = $"{scenario.AccountPrefix}{i:D5}" };
            stats.Add(botStats);
            var bot = new FineageBotClient(scenario, i, botStats, peers);
            bots.Add(bot);

            int delayMs = scenario.RampUpSeconds <= 0
                ? 0
                : (int)(scenario.RampUpSeconds * 1000.0 / scenario.BotCount);
            if (delayMs > 0)
                await Task.Delay(delayMs, ct);

            try
            {
                await bot.ConnectAndEnterAsync(ct);
            }
            catch (Exception ex)
            {
                botStats.LastError = ex.Message;
                botStats.Disconnected = true;
            }
        }

        DateTimeOffset behaviorStart = DateTimeOffset.UtcNow;
        DateTimeOffset steadyStateStarted = behaviorStart.AddSeconds(scenario.RampUpSeconds);
        DateTimeOffset behaviorEnd = behaviorStart + TimeSpan.FromSeconds(scenario.DurationSeconds);
        int scrapeSeconds = scenario.DurationSeconds + scenario.RampUpSeconds + 10;

        var metricsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var metricsTask = Task.Run(async () =>
        {
            var sampler = new ServerMetricsSampler();
            var samples = await sampler.SampleZoneDuringAsync(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(scrapeSeconds),
                metricsCts.Token);
            return (samples, sampler.RawScrapes);
        }, ct);

        var behaviorLoops = bots
            .Where(b => !b.Stats.Disconnected)
            .Select(b => b.RunBehaviorLoopAsync(bots, behaviorEnd, TimeSpan.FromMilliseconds(250), ct))
            .ToArray();
        await Task.WhenAll(behaviorLoops);

        metricsCts.Cancel();
        (List<PrometheusSample> promSamples, List<string> rawScrapes) = await metricsTask;

        string? peakMetrics = null;
        if (promSamples.Count > 0 && rawScrapes.Count == promSamples.Count)
        {
            int peakIdx = 0;
            long peakQ = 0;
            for (int i = 0; i < promSamples.Count; i++)
            {
                if (promSamples[i].CommandIngressQueued > peakQ)
                {
                    peakQ = promSamples[i].CommandIngressQueued;
                    peakIdx = i;
                }
            }

            peakMetrics = rawScrapes[peakIdx];
        }

        foreach (var bot in bots)
            await bot.DisposeAsync();

        var logs = new List<string>();
        if (loginLog != null) logs.Add(loginLog);
        if (zoneLog != null) logs.Add(zoneLog);
        var logIssues = LogScanner.ScanFiles(logs);

        int inWorld = stats.Count(s => s.InWorld && !s.Disconnected);
        int peakOnline = promSamples.Count > 0 ? promSamples.Max(s => s.ZoneOnline) : 0;
        int disconnected = stats.Count(s => s.Disconnected);
        var stuckCutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30);
        int stuck = stats.Count(s => s.InWorld && !s.Disconnected &&
            (s.LastActionAt == null || s.LastActionAt < stuckCutoff));

        var (p50, p95, p99, max) = ReportWriter.ClientRtt(stats);
        var failReasons = new List<string>();
        var bugs = new List<string>();

        if (logIssues.Any(i => i.Key is "exception" or "handler_error" && i.Count > 0))
            failReasons.Add("Server logs contain exceptions or handler errors.");
        if (disconnected > scenario.BotCount / 10)
            failReasons.Add($"High disconnect rate: {disconnected}/{scenario.BotCount}");
        if (stuck > scenario.BotCount / 5)
            failReasons.Add($"Many bots stuck: {stuck}/{scenario.BotCount}");

        if (disconnected > 0)
            bugs.Add($"P2: {disconnected} bots disconnected during {scenario.Name} — check rate limits and handoff TTL.");
        if (promSamples.Count > 0 && promSamples.Max(s => s.TickP99) > 500)
            bugs.Add($"P1: tick p99 exceeded 500ms (max {promSamples.Max(s => s.TickP99):0}ms) under {scenario.BotCount} bots.");

        ProcessSample? proc = zonePid is > 0 ? new ProcessMetricsSampler().SamplePid(zonePid.Value) : null;

        var consistency = ConsistencyOracle.Analyze(bots, scenario, scenario.AoiCellSize, steadyStateStarted);
        var hardIssues = consistency.Issues.Where(i => ConsistencyOracle.IsHardFailure(i.Kind)).ToList();
        if (consistency.Issues.Count > 0)
        {
            var byKind = consistency.Issues.GroupBy(i => i.Kind).OrderByDescending(g => g.Count());
            foreach (var g in byKind.Take(5))
                bugs.Add($"P1 consistency {g.Key}: {g.Count()} ({g.First().Detail})");
        }

        if (hardIssues.Count > 0)
            failReasons.Add($"Consistency oracle (hard): {hardIssues.Count} issue(s).");

        RuntimeCounterStats? runtimeStats = null;
        if (runtimeSampler != null)
        {
            runtimeStats = await runtimeSampler.StopAndSummarizeAsync();
            await runtimeSampler.DisposeAsync();
        }

        var report = new RunReport
        {
            ScenarioName = scenario.Name,
            Target = target,
            RegionWorkers = regionWorkers,
            SpawnLayout = scenario.SpawnLayout,
            BotCount = scenario.BotCount,
            BotsInWorld = inWorld,
            PeakZoneOnline = peakOnline,
            BotsDisconnected = disconnected,
            BotsStuck = stuck,
            RelogsAttempted = stats.Sum(s => s.RelogsAttempted),
            RelogsSucceeded = stats.Sum(s => s.RelogsSucceeded),
            ClientRttP50Ms = p50,
            ClientRttP95Ms = p95,
            ClientRttP99Ms = p99,
            ClientRttMaxMs = Math.Max(max, stats.Count > 0 ? stats.Max(s => s.RttMaxMs) : 0),
            Server = PrometheusAggregate.FromSamples(promSamples, peakMetrics),
            PeakMetricsText = peakMetrics,
            ZoneProcess = proc,
            LogIssues = logIssues,
            Passed = failReasons.Count == 0,
            FailReasons = failReasons,
            Bugs = bugs,
            Consistency = consistency,
            StartedAt = started,
            SteadyStateStartedAt = steadyStateStarted,
            FinishedAt = DateTimeOffset.UtcNow
        };

        var logPaths = new List<string>();
        if (loginLog != null) logPaths.Add(loginLog);
        if (zoneLog != null) logPaths.Add(zoneLog);
        var attackSamples = bots.SelectMany(b => b.ObservationLog.Tracker.AttackFanoutSamples);
        var skillSamples = bots.SelectMany(b => b.ObservationLog.Tracker.SkillFanoutSamples);
        var legacySamples = bots.SelectMany(b => b.ObservationLog.Tracker.CommandToFanoutSamples);
        ReportEnricher.Enrich(report, scenario, promSamples, rawScrapes, attackSamples, skillSamples, legacySamples, zoneLog, logPaths, runtimeStats);

        report.FailReasons = failReasons;
        report.Passed = failReasons.Count == 0;

        return report;
    }
}