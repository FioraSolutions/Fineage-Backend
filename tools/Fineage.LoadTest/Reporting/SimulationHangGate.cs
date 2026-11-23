using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Models;

namespace Fineage.LoadTest.Reporting;

/// <summary>
/// One observation of <c>fineage_tick_ms_count</c>. The load gate parses this from raw scrapes
/// so it does not depend on <c>PrometheusSample</c>.
/// </summary>
public readonly record struct TickObservation(DateTimeOffset At, long TickCount);

/// <summary>
/// Whether the zone simulation stalled, and for how long. <see cref="StallSeconds"/> is the
/// measured freeze (until the counter moved again, or until observation ended), not the threshold.
/// </summary>
public sealed record SimulationHangVerdict(bool Hung, double StallSeconds, int StallCount, string? Reason)
{
    public static SimulationHangVerdict Clear { get; } = new(false, 0, 0, null);
}

/// <summary>
/// Fails a load run when the zone step watchdog reports a stuck <c>StepSimulation</c>, or when
/// <c>fineage_tick_ms_count</c> does not advance for longer than <see cref="StallLimit"/> at any
/// point, including the tail from the last advance to the end of observation. The limit matches
/// the zone step watchdog (30s): a step the server already calls stuck fails the run.
/// </summary>
public static class SimulationHangGate
{
    public static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(30);

    const int MetricsPort = 9465;

    static readonly Regex TickCountPattern = new(
        @"^fineage_tick_ms_count(?:\{[^}]*\})?\s+(\d+)\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex WatchdogPattern = new(
        @"simulation step tick=\d+ has run (?<seconds>[0-9]+(?:\.[0-9]+)?)s without finishing",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static int ExitCode(bool reportWritten, bool passed) =>
        reportWritten && passed ? 0 : 2;

    public static List<TickObservation> FromScrapes(
        IReadOnlyList<PrometheusSample> samples,
        IReadOnlyList<string> rawScrapes)
    {
        var series = new List<TickObservation>();
        int n = Math.Min(samples.Count, rawScrapes.Count);
        for (int i = 0; i < n; i++)
        {
            if (ParseTickCount(rawScrapes[i]) is long count)
                series.Add(new TickObservation(samples[i].ScrapedAt, count));
        }

        return series;
    }

    public static long? ParseTickCount(string? metricsText)
    {
        if (string.IsNullOrEmpty(metricsText))
            return null;
        Match match = TickCountPattern.Match(metricsText);
        if (!match.Success)
            return null;
        return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Longest span in which the tick counter did not advance. The span from the last advance
    /// (or the only sample) through <paramref name="observedUntil"/> is included, so a freeze
    /// that is still going at the end of the behavior window counts.
    /// A series with no samples counts only when the whole observation exceeds the limit:
    /// metrics that appear late are not themselves a stall.
    /// </summary>
    public static SimulationHangVerdict Evaluate(
        IReadOnlyList<TickObservation> samples,
        DateTimeOffset observedFrom,
        DateTimeOffset observedUntil,
        TimeSpan? stallLimit = null)
    {
        TimeSpan limit = stallLimit ?? StallLimit;
        double limitSeconds = limit.TotalSeconds;
        double longest = 0;
        int episodes = 0;

        void Consider(double seconds)
        {
            if (seconds > longest)
                longest = seconds;
            if (seconds > limitSeconds)
                episodes++;
        }

        if (samples.Count == 0)
        {
            Consider((observedUntil - observedFrom).TotalSeconds);
        }
        else
        {
            DateTimeOffset freezeStart = samples[0].At;
            long count = samples[0].TickCount;
            bool sawRepeat = false;
            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i].TickCount == count)
                {
                    sawRepeat = true;
                    continue;
                }

                if (sawRepeat)
                    Consider((samples[i].At - freezeStart).TotalSeconds);
                freezeStart = samples[i].At;
                count = samples[i].TickCount;
                sawRepeat = false;
            }

            Consider((observedUntil - freezeStart).TotalSeconds);
        }

        if (longest < 0)
            longest = 0;

        bool hung = episodes > 0;
        string? reason = hung
            ? $"Simulation hang: tick counter stopped advancing for {longest.ToString("0.0", CultureInfo.InvariantCulture)}s (limit {limitSeconds.ToString("0", CultureInfo.InvariantCulture)}s)."
            : null;
        return new SimulationHangVerdict(hung, longest, episodes, reason);
    }

    public static SimulationHangVerdict FromLog(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return SimulationHangVerdict.Clear;

        double longest = 0;
        int hits = 0;
        string? longestLine = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;
            Match match = WatchdogPattern.Match(line);
            if (!match.Success)
                continue;
            hits++;
            double seconds = double.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture);
            if (seconds >= longest)
            {
                longest = seconds;
                longestLine = line;
            }
        }

        if (hits == 0)
            return SimulationHangVerdict.Clear;

        string snippet = longestLine ?? "simulation step watchdog";
        if (snippet.Length > 300)
            snippet = snippet[..300];
        string reason =
            $"Simulation hang: zone step watchdog reported a StepSimulation stall of {longest.ToString("0.0", CultureInfo.InvariantCulture)}s. {snippet}";
        return new SimulationHangVerdict(true, longest, hits, reason);
    }

    public static SimulationHangVerdict FromLogFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return SimulationHangVerdict.Clear;
        return FromLog(File.ReadAllText(path));
    }

    /// <summary>
    /// One verdict. The printed duration is the longer of the tick freeze and the watchdog's own
    /// "has run Ns" (the watchdog line is the server's measurement). A watchdog hit is a failure
    /// even when the tick series did not cross the limit. Stall count is the number of tick-freeze
    /// episodes over the limit, or one when only the watchdog fired.
    /// </summary>
    public static SimulationHangVerdict Combine(SimulationHangVerdict ticks, SimulationHangVerdict watchdog)
    {
        double seconds = Math.Max(ticks.StallSeconds, watchdog.StallSeconds);
        bool hung = ticks.Hung || watchdog.Hung;
        int count = ticks.StallCount;
        if (watchdog.Hung && count == 0)
            count = 1;
        if (!hung)
            return new SimulationHangVerdict(false, seconds, 0, null);

        var sb = new StringBuilder();
        sb.Append("Simulation hang: true stall duration ");
        sb.Append(seconds.ToString("0.0", CultureInfo.InvariantCulture));
        sb.Append("s (limit ");
        sb.Append(StallLimit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture));
        sb.Append("s).");
        if (ticks.Hung)
        {
            sb.Append(" Tick counter stopped advancing for ");
            sb.Append(ticks.StallSeconds.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append("s.");
        }

        if (watchdog.Hung && watchdog.Reason != null)
        {
            sb.Append(' ');
            sb.Append(watchdog.Reason);
        }

        return new SimulationHangVerdict(true, seconds, count, sb.ToString());
    }

    public static RunReport RunnerFault(
        LoadScenario scenario,
        string target,
        int regionWorkers,
        Exception ex,
        SimulationHangVerdict? hang = null)
    {
        var reasons = new List<string>();
        if (hang is { Hung: true, Reason: { } hangReason })
            reasons.Add(hangReason);
        reasons.Add($"Load test runner failed before finishing: {ex.GetType().Name}: {ex.Message}");
        reasons.Add("Load test runner wrote no report before the failure; this report is the failure record.");
        return new RunReport
        {
            ScenarioName = scenario.Name,
            Target = target,
            RegionWorkers = regionWorkers,
            SpawnLayout = scenario.SpawnLayout,
            BotCount = scenario.BotCount,
            Passed = false,
            FailReasons = reasons,
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            SimulationStallSeconds = hang?.StallSeconds ?? 0,
            SimulationStallCount = hang?.StallCount ?? 0
        };
    }

    /// <summary>
    /// Polls the zone metrics until <paramref name="stop"/> is cancelled. The first time the tick
    /// counter has been frozen for longer than the limit, cancels <paramref name="abortRun"/> so
    /// a behavior loop blocked on the stuck zone still returns and the runner can write a report.
    /// Observation continues until <paramref name="stop"/> so the recorded duration is the real
    /// freeze, not the threshold.
    /// </summary>
    public static async Task<SimulationHangVerdict> WatchTicksAsync(
        CancellationTokenSource abortRun,
        DateTimeOffset observedFrom,
        CancellationToken stop,
        TaskCompletionSource<SimulationHangVerdict> hung,
        TimeSpan? poll = null,
        TimeSpan? stallLimit = null,
        Func<CancellationToken, Task<long?>>? readTickCount = null)
    {
        TimeSpan interval = poll ?? TimeSpan.FromSeconds(2);
        TimeSpan limit = stallLimit ?? StallLimit;
        var samples = new List<TickObservation>();
        bool signaled = false;
        using var http = readTickCount == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(2) } : null;
        int port = MetricsPort;
        string? portText = Environment.GetEnvironmentVariable("FINEAGE_ZONE_METRICS_PORT");
        if (int.TryParse(portText, out int parsed) && parsed > 0)
            port = parsed;

        while (!stop.IsCancellationRequested)
        {
            long? count = null;
            try
            {
                if (readTickCount != null)
                    count = await readTickCount(stop).ConfigureAwait(false);
                else if (http != null)
                    count = ParseTickCount(await http.GetStringAsync($"http://127.0.0.1:{port}/metrics", stop).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                count = null;
            }

            if (count is long n)
                samples.Add(new TickObservation(DateTimeOffset.UtcNow, n));

            SimulationHangVerdict verdict = Evaluate(samples, observedFrom, DateTimeOffset.UtcNow, limit);
            if (verdict.Hung && !signaled)
            {
                signaled = true;
                try
                {
                    abortRun.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                hung.TrySetResult(verdict);
            }

            try
            {
                await Task.Delay(interval, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return Evaluate(samples, observedFrom, DateTimeOffset.UtcNow, limit);
    }
}
