using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Consistency;
using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Models;
using Fineage.LoadTest.Reporting;
using Fineage.LoadTest.Travel;

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
        CancellationToken ct,
        string? zoneDll = null,
        string? expectedZoneCommit = null,
        bool requireCleanProvenance = false)
    {
        if (scenario.Economy is { Enabled: true })
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("FINEAGE_LOADTEST_ECONOMY_HAMMER"), "1",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "economy hammer scenarios require FINEAGE_LOADTEST_ECONOMY_HAMMER=1 on the Zone process " +
                    "(dwarf manufacturers + customer materials provisioning).");
            }

            if (scenario.BotCount < scenario.Economy.ManufacturerModulo)
            {
                throw new InvalidOperationException(
                    $"economy hammer needs at least {scenario.Economy.ManufacturerModulo} bots for manufacture roles; got {scenario.BotCount}.");
            }
        }

        if (target == "mobius")
            return MobiusRunReport.ScenarioNotSupported(scenario, regionWorkers);

        var callerCt = ct;
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
        using var watchStop = new CancellationTokenSource();
        var observedFrom = DateTimeOffset.UtcNow;
        var hungSignal = new TaskCompletionSource<SimulationHangVerdict>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hangTask = SimulationHangGate.WatchTicksAsync(runCts, observedFrom, watchStop.Token, hungSignal);
        // Stall cancel is not a caller cancel. Connect and behavior use this token so a stuck tick still returns a report.
        ct = runCts.Token;
        SimulationHangVerdict hang = SimulationHangVerdict.Clear;
        Exception? runFault = null;

        var started = DateTimeOffset.UtcNow;
        var peers = new BotPeerDirectory();
        var bots = new List<FineageBotClient>();
        var stats = new List<BotStats>();

        RuntimeCountersSampler? runtimeSampler = zonePid is > 0 ? new RuntimeCountersSampler(zonePid.Value) : null;
        if (runtimeSampler != null)
            await runtimeSampler.StartAsync(ct);

        var gatekeepers = scenario.ResolvedGatekeeperTravelShare > 0
            ? GatekeeperDirectory.Load(GatekeeperDirectory.ResolveDataDir(scenario.DataDir))
            : null;
        for (int i = 0; i < scenario.BotCount; i++)
        {
            var botStats = new BotStats { BotId = i, Account = $"{scenario.AccountPrefix}{i:D5}" };
            stats.Add(botStats);
            bots.Add(new FineageBotClient(scenario, i, botStats, peers, gatekeepers));
        }

        int concurrency = ResolveEnterConcurrency(scenario);
        int delayMs = scenario.RampUpSeconds <= 0 || scenario.BotCount <= 1
            ? 0
            : (int)(scenario.RampUpSeconds * 1000.0 / scenario.BotCount);
        using var slots = new SemaphoreSlim(concurrency);
        var enters = new List<Task>();
        for (int i = 0; i < scenario.BotCount; i++)
        {
            if (i > 0 && delayMs > 0)
            {
                try
                {
                    await Task.Delay(delayMs, ct);
                }
                catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
                {
                    break;
                }
            }

            try
            {
                await slots.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
            {
                break;
            }

            enters.Add(EnterOneAsync(bots[i], slots, ct));
        }

        try
        {
            await Task.WhenAll(enters);
        }
        catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
        {
        }

        if (scenario.HeavyPlay is { IsHeavyPlay: true })
            await HeavyPlayBehavior.FormPartiesAsync(bots, scenario, ct);

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
        var behaviorTask = Task.WhenAll(behaviorLoops);
        await Task.WhenAny(behaviorTask, hungSignal.Task);
        if (!behaviorTask.IsCompleted)
            await Task.WhenAny(behaviorTask, Task.Delay(TimeSpan.FromSeconds(5)));
        if (behaviorTask.IsCompleted)
        {
            try
            {
                await behaviorTask;
            }
            catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                runFault = ex;
            }
        }
        else
        {
            runFault = new TimeoutException(
                "behavior loops stayed blocked for 5s after a simulation stall; the run stopped so a report could be written");
        }

        watchStop.Cancel();
        metricsCts.Cancel();
        List<PrometheusSample> promSamples;
        List<string> rawScrapes;
        try
        {
            (promSamples, rawScrapes) = await metricsTask;
        }
        catch (Exception ex)
        {
            promSamples = [];
            rawScrapes = [];
            runFault ??= ex;
        }

        SimulationHangVerdict tickHang;
        try
        {
            tickHang = await hangTask;
        }
        catch (Exception ex)
        {
            tickHang = SimulationHangVerdict.Clear;
            runFault ??= ex;
        }

        var tickSeries = SimulationHangGate.FromScrapes(promSamples, rawScrapes);
        hang = SimulationHangGate.Combine(
            SimulationHangGate.Combine(
                tickHang,
                SimulationHangGate.Evaluate(tickSeries, observedFrom, DateTimeOffset.UtcNow),
                SimulationHangGate.FromTickMax(tickSeries)),
            SimulationHangGate.FromLogFile(zoneLog));

        // Histograms are cumulative for the process, so the last scrape is the run aggregate.
        // A scrape chosen for a high queue gauge can predate those histograms and report them missing.
        string? peakMetrics = rawScrapes.Count > 0 ? rawScrapes[^1] : null;

        foreach (var bot in bots)
            await bot.DisposeAsync();

        var logs = new List<string>();
        if (loginLog != null) logs.Add(loginLog);
        if (zoneLog != null) logs.Add(zoneLog);
        // The log files are shared with earlier runs: only the entries this run produced may fail it.
        var logIssues = LogScanner.ScanFiles(logs, started, DateTimeOffset.UtcNow);

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
        {
            bugs.Add($"P2: {disconnected} bots disconnected during {scenario.Name} — check rate limits and handoff TTL.");
            foreach (var group in bots.Where(b => b.Stats.Disconnected)
                         .GroupBy(b => b.Stats.LastError ?? "(no error recorded)")
                         .OrderByDescending(g => g.Count())
                         .Take(5))
            {
                string error = group.Key.Length > 200 ? group.Key[..200] : group.Key;
                bugs.Add($"P2:   {group.Count()} disconnected bot(s) last error: {error} (bots {string.Join(",", group.Select(b => b.BotId).Take(8))})");
            }
        }
        if (promSamples.Count > 0 && promSamples.Max(s => s.TickP99) > 500)
            bugs.Add($"P1: tick p99 exceeded 500ms (max {promSamples.Max(s => s.TickP99):0}ms) under {scenario.BotCount} bots.");

        ProcessSample? proc = zonePid is > 0 ? new ProcessMetricsSampler().SamplePid(zonePid.Value) : null;

        ConsistencyReport consistency;
        bool oracleRan = false;
        if (hang.Hung)
        {
            // A stalled step used to leave the runner inside the per-frame oracle, so the report was never written.
            consistency = new ConsistencyReport();
            bugs.Add("P0: simulation hang; consistency oracle skipped so the report is written with the stall evidence.");
        }
        else
        {
            try
            {
                consistency = ConsistencyOracle.Analyze(bots, scenario, scenario.AoiCellSize, steadyStateStarted);
                oracleRan = true;
            }
            catch (Exception ex)
            {
                consistency = new ConsistencyReport();
                runFault ??= ex;
            }
        }

        var hardIssues = consistency.Issues.Where(i => ConsistencyOracle.IsHardFailure(i.Kind)).ToList();
        if (consistency.Issues.Count > 0)
        {
            var byKind = consistency.Issues.GroupBy(i => i.Kind).OrderByDescending(g => g.Count());
            foreach (var g in byKind.Take(5))
                bugs.Add($"P1 consistency {g.Key}: {g.Count()} ({g.First().Detail})");
        }

        if (hardIssues.Count > 0)
            failReasons.Add($"Consistency oracle (hard): {hardIssues.Count} issue(s).");

        ConsistencyTrace? consistencyTrace = null;
        if (oracleRan && scenario.ConsistencyTrace.Enabled && consistency.Issues.Count > 0)
        {
            try
            {
                consistencyTrace = ConsistencyTraceBuilder.Build(bots, consistency, scenario.ConsistencyTrace);
            }
            catch (Exception ex)
            {
                bugs.Add($"P2 consistency trace failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var travel = TravelSummary.From(stats.Where(s => s.Travel != null).Select(s => s.Travel!));
        if (travel.FailReason() is { } travelFail)
            failReasons.Add(travelFail);

        EconomyConsistencyReport? economyReport = null;
        if (scenario.Economy is { Enabled: true })
        {
            var logPathsEarly = new List<string>();
            if (loginLog != null) logPathsEarly.Add(loginLog);
            if (zoneLog != null) logPathsEarly.Add(zoneLog);
            economyReport = EconomyConsistencyOracle.Analyze(bots, scenario, promSamples, logPathsEarly);
            var economyHard = economyReport.Issues.Where(i => EconomyConsistencyOracle.IsHardFailure(i.Kind)).ToList();
            if (economyHard.Count > 0)
                failReasons.Add($"Economy oracle (hard): {economyHard.Count} issue(s).");
            foreach (var issue in economyHard.Take(10))
            {
                bugs.Add(issue.BotId is int bid
                    ? $"P0 economy {issue.Kind}: bot={bid} oid={issue.ObjectId} {issue.Detail}"
                    : $"P0 economy {issue.Kind}: {issue.Detail}");
            }
        }

        if (oracleRan && scenario.RequireSkillLatencySamples && consistency.SkillSampleCount == 0)
            failReasons.Add("skillSampleCount=0: no UseSkill→MagicSkillUse latency samples in steady window.");
        // Teardown, or an oracle that outlasts the watchdog, can cross the limit after the first read.
        // Read the zone log again so that stall fails the run.
        hang = SimulationHangGate.Combine(hang, SimulationHangGate.FromLogFile(zoneLog));
        if (hang.Hung && hang.Reason != null)
            failReasons.Add(hang.Reason);
        if (runFault != null)
            failReasons.Add($"Load test runner failed before finishing: {runFault.GetType().Name}: {runFault.Message}");

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
            GatekeeperTravel = travel,
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
            ConsistencyTrace = consistencyTrace,
            Economy = economyReport,
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
        ReportEnricher.Enrich(report, scenario, promSamples, rawScrapes, attackSamples, skillSamples, legacySamples,
            zoneLog, logPaths, runtimeStats, zonePid, zoneDll, expectedZoneCommit, requireCleanProvenance);

        report.SimulationStallSeconds = hang.StallSeconds;
        report.SimulationStallCount = hang.StallCount;
        report.FailReasons = failReasons;
        report.Passed = failReasons.Count == 0;

        return report;
    }

    static int ResolveEnterConcurrency(LoadScenario scenario)
    {
        string? text = Environment.GetEnvironmentVariable("FINEAGE_LOADTEST_ENTER_CONCURRENCY");
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!int.TryParse(text, out int fromEnv) || fromEnv < 1)
                throw new InvalidOperationException(
                    $"FINEAGE_LOADTEST_ENTER_CONCURRENCY='{text}' must be an integer >= 1.");
            return fromEnv;
        }

        if (scenario.EnterConcurrency < 1)
            throw new InvalidOperationException(
                $"scenario enterConcurrency={scenario.EnterConcurrency} must be an integer >= 1.");
        return scenario.EnterConcurrency;
    }

    static async Task EnterOneAsync(FineageBotClient bot, SemaphoreSlim slots, CancellationToken ct)
    {
        try
        {
            await bot.ConnectAndEnterAsync(ct);
        }
        catch (Exception ex)
        {
            bot.Stats.LastError = ex.Message;
            bot.Stats.Disconnected = true;
        }
        finally
        {
            slots.Release();
        }
    }
}