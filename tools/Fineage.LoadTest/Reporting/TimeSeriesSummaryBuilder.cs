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

    /// <summary>
    /// One CountBounds step (see <c>MetricsText</c>) allowed on a lifetime histogram high-water
    /// before early-third vs late-third max is called growing. <c>fineage_fanout_queue_depth</c>
    /// (and the command-depth twin) expose a cumulative max, so a single mid-run sample of depth 4
    /// ratchets every later scrape from 2 to 4 and is not a backlog.
    /// </summary>
    public const double QueueTrendAbsoluteSlack = 8;

    /// <summary>Early-third vs late-third max. Used by the queue-depth soak gate (#990, #951).</summary>
    public static string Trend(
        IReadOnlyList<TimeSeriesWindow> windows,
        Func<TimeSeriesWindow, double> selector,
        string absentText)
    {
        if (windows.Count < 2)
            return "insufficient windows";
        double first = ThirdMedian(windows, selector, first: true);
        double last = ThirdMedian(windows, selector, first: false);
        if (last <= 0 && first <= 0)
            return windows.Any(w => selector(w) > 0) ? "flat" : absentText;
        if (last > GrowthLimit(first))
            return "growing";
        if (last < first * 0.75)
            return "shrinking";
        return "flat";
    }

    public static double GrowthLimit(double earlyThirdMax) =>
        Math.Max(earlyThirdMax * 1.25 + QueueTrendAbsoluteSlack, NextCountBoundsBucket(earlyThirdMax));

    /// <summary>
    /// Next <c>CountBounds</c> histogram ceiling strictly above <paramref name="early"/>.
    /// A lifetime scrape max ratchets to this bucket on one new high-water and is not a backlog.
    /// </summary>
    public static double NextCountBoundsBucket(double early)
    {
        if (early <= 0)
            return QueueTrendAbsoluteSlack;
        double bucket = 1;
        while (bucket <= early)
            bucket *= 2;
        return bucket;
    }

    public static double ThirdMedian(
        IReadOnlyList<TimeSeriesWindow> windows,
        Func<TimeSeriesWindow, double> selector,
        bool first)
    {
        if (windows.Count == 0)
            return 0;
        int size = Math.Max(1, windows.Count / 3);
        var values = (first ? windows.Take(size) : windows.TakeLast(size))
            .Select(selector)
            .OrderBy(v => v)
            .ToList();
        if (values.Count == 0)
            return 0;
        int mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;
    }
}