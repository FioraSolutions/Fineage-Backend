using Fineage.LoadTest.Metrics;

namespace Fineage.LoadTest.Reporting;

public sealed class TimeSeriesWindow
{
    public DateTimeOffset WindowStart { get; init; }
    public DateTimeOffset WindowEnd { get; init; }
    public double TickP99Ms { get; init; }
    public double CommandsStageP99Ms { get; init; }
    public double CommandToMailboxesP99Ms { get; init; }
    public double SubmitToMailboxP99Ms { get; init; }
    public double FanoutEnqueueToMailboxP99Ms { get; init; }
    public double FanoutQueueAgeP99Ms { get; init; }
    public double FanoutQueueDepthMax { get; init; }
    public double CommandQueueDepthMax { get; init; }
    public double CommandIngressQueuedMax { get; init; }
}

public static class TimeSeriesSummaryBuilder
{
    public const int WindowSeconds = 10;

    public static List<TimeSeriesWindow> Build(IReadOnlyList<PrometheusSample> samples)
    {
        if (samples.Count == 0)
            return [];

        var ordered = samples.OrderBy(s => s.ScrapedAt).ToList();
        DateTimeOffset start = ordered[0].ScrapedAt;
        DateTimeOffset end = ordered[^1].ScrapedAt;
        var windows = new List<TimeSeriesWindow>();

        for (var wStart = start; wStart < end; wStart = wStart.AddSeconds(WindowSeconds))
        {
            var wEnd = wStart.AddSeconds(WindowSeconds);
            var inWindow = ordered.Where(s => s.ScrapedAt >= wStart && s.ScrapedAt < wEnd).ToList();
            if (inWindow.Count == 0)
                continue;

            windows.Add(new TimeSeriesWindow
            {
                WindowStart = wStart,
                WindowEnd = wEnd,
                TickP99Ms = inWindow.Max(s => s.TickP99),
                CommandsStageP99Ms = inWindow.Max(s => s.TickStageP99Ms.GetValueOrDefault("commands")),
                CommandToMailboxesP99Ms = inWindow.Max(s => s.CommandToMailboxesP99Ms),
                SubmitToMailboxP99Ms = inWindow.Max(s => s.SubmitToMailboxP99Ms),
                FanoutEnqueueToMailboxP99Ms = inWindow.Max(s => s.FanoutEnqueueToMailboxP99Ms),
                FanoutQueueAgeP99Ms = inWindow.Max(s => s.FanoutQueueOldestAgeMs),
                FanoutQueueDepthMax = inWindow.Max(s => s.FanoutQueueDepth),
                CommandQueueDepthMax = inWindow.Max(s => s.CommandQueueDepthMax),
                CommandIngressQueuedMax = inWindow.Max(s => s.CommandIngressQueued)
            });
        }

        return windows;
    }

    public static string BacklogTrend(IReadOnlyList<TimeSeriesWindow> windows) =>
        Trend(windows, w => w.FanoutQueueDepthMax, "fanout_queue_depth metric absent");

    /// <summary>Early-third vs late-third max. Used by the queue-depth soak gate (#990).</summary>
    public static string Trend(
        IReadOnlyList<TimeSeriesWindow> windows,
        Func<TimeSeriesWindow, double> selector,
        string absentText)
    {
        if (windows.Count < 2)
            return "insufficient windows";
        double first = windows.Take(windows.Count / 3).DefaultIfEmpty().Max(w => w == null ? 0 : selector(w));
        double last = windows.TakeLast(windows.Count / 3).DefaultIfEmpty().Max(w => w == null ? 0 : selector(w));
        if (last <= 0 && first <= 0)
            return absentText;
        if (last > first * 1.25 + 1)
            return "growing";
        if (last < first * 0.75)
            return "shrinking";
        return "flat";
    }
}