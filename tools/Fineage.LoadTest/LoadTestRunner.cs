using Fineage.LoadTest.Bot;
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
        var bots = new List<FineageBotClient>();
        var stats = new List<BotStats>();

        var metricsTask = Task.Run(async () =>
        {
            var sampler = new ServerMetricsSampler();
            return await sampler.SampleZoneDuringAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(scenario.DurationSeconds + scenario.RampUpSeconds), ct);
        }, ct);

        for (int i = 0; i < scenario.BotCount; i++)
        {
            var botStats = new BotStats { BotId = i, Account = $"{scenario.AccountPrefix}{i:D5}" };
            stats.Add(botStats);
            var bot = new FineageBotClient(scenario, i, botStats);
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

        var behaviorEnd = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(scenario.DurationSeconds);
        while (DateTimeOffset.UtcNow < behaviorEnd && !ct.IsCancellationRequested)
        {
            await Task.WhenAll(bots.Where(b => !b.Stats.Disconnected).Select(b => b.RunBehaviorTickAsync(ct)));
            await Task.Delay(250, ct);
        }

        List<PrometheusSample> promSamples = await metricsTask;
        foreach (var bot in bots)
            await bot.DisposeAsync();

        var logs = new List<string>();
        if (loginLog != null) logs.Add(loginLog);
        if (zoneLog != null) logs.Add(zoneLog);
        var logIssues = LogScanner.ScanFiles(logs);

        int inWorld = stats.Count(s => s.InWorld && !s.Disconnected);
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

        return new RunReport
        {
            ScenarioName = scenario.Name,
            Target = target,
            RegionWorkers = regionWorkers,
            SpawnLayout = scenario.SpawnLayout,
            BotCount = scenario.BotCount,
            BotsInWorld = inWorld,
            BotsDisconnected = disconnected,
            BotsStuck = stuck,
            ClientRttP50Ms = p50,
            ClientRttP95Ms = p95,
            ClientRttP99Ms = p99,
            ClientRttMaxMs = Math.Max(max, stats.Count > 0 ? stats.Max(s => s.RttMaxMs) : 0),
            Server = PrometheusAggregate.FromSamples(promSamples),
            ZoneProcess = proc,
            LogIssues = logIssues,
            Passed = failReasons.Count == 0,
            FailReasons = failReasons,
            Bugs = bugs,
            StartedAt = started,
            FinishedAt = DateTimeOffset.UtcNow
        };
    }
}