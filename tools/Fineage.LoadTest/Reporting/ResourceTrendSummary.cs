using Fineage.LoadTest.Metrics;

namespace Fineage.LoadTest.Reporting;

/// <summary>
/// Steady-state resource trend used by the #990 soak/endurance gates. Values are measured after the ramp,
/// so login/enter-world warm-up is not mistaken for a leak.
/// </summary>
public sealed record ResourceTrendSummary
{
    public int RssSampleCount { get; init; }
    public double? RssMbStart { get; init; }
    public double? RssMbEnd { get; init; }
    public double? RssMbMax { get; init; }
    public double? RssGrowthMb { get; init; }
    public double? RssGrowthPercent { get; init; }

    public int GcHeapSampleCount { get; init; }
    public double? GcHeapMbStart { get; init; }
    public double? GcHeapMbEnd { get; init; }
    public double? GcHeapMbMax { get; init; }
    public double? GcHeapGrowthMb { get; init; }

    public int AllocRateSampleCount { get; init; }
    public double? AllocRateFirstThirdMbPerSec { get; init; }
    public double? AllocRateLastThirdMbPerSec { get; init; }
    public double? AllocRateGrowthRatio { get; init; }
    public long Gen2CountDelta { get; init; }

    public bool HasRssTrend => RssSampleCount >= 2 && RssMbStart is > 0;

    public bool HasGcTrend => GcHeapSampleCount >= 2 && GcHeapMbStart is >= 0;

    public bool HasAllocTrend => AllocRateSampleCount >= 3;
}

public static class ResourceTrendSummaryBuilder
{
    public static ResourceTrendSummary Build(
        DateTimeOffset steadyStateStartedAt,
        IReadOnlyList<RssSample>? rssSamples,
        RuntimeCounterStats? runtime)
    {
        var rss = (rssSamples ?? [])
            .Where(s => s.At >= steadyStateStartedAt)
            .OrderBy(s => s.At)
            .ToList();
        var gc = (runtime?.GcHeapMbSeries ?? [])
            .Where(s => s.At >= steadyStateStartedAt)
            .OrderBy(s => s.At)
            .ToList();
        var alloc = (runtime?.AllocRateMbPerSecSeries ?? [])
            .Where(s => s.At >= steadyStateStartedAt)
            .OrderBy(s => s.At)
            .ToList();

        double? rssStart = rss.Count > 0 ? rss[0].RssMb : null;
        double? rssEnd = rss.Count > 0 ? rss[^1].RssMb : null;
        double? rssGrowth = rssStart is > 0 && rssEnd is { } end ? end - rssStart.Value : null;
        double? rssGrowthPercent = rssStart is > 0 && rssGrowth is { } growth
            ? growth / rssStart.Value * 100.0
            : null;

        double? gcStart = gc.Count > 0 ? gc[0].Value : null;
        double? gcEnd = gc.Count > 0 ? gc[^1].Value : null;
        double? gcGrowth = gcStart is { } start && gcEnd is { } end2 ? end2 - start : null;

        double? allocFirst = ThirdAverage(alloc.Select(p => p.Value).ToList(), first: true);
        double? allocLast = ThirdAverage(alloc.Select(p => p.Value).ToList(), first: false);
        double? allocRatio = allocFirst is > 0 && allocLast is { } last
            ? last / allocFirst.Value
            : null;

        return new ResourceTrendSummary
        {
            RssSampleCount = rss.Count,
            RssMbStart = rssStart,
            RssMbEnd = rssEnd,
            RssMbMax = rss.Count > 0 ? rss.Max(s => s.RssMb) : null,
            RssGrowthMb = rssGrowth,
            RssGrowthPercent = rssGrowthPercent,
            GcHeapSampleCount = gc.Count,
            GcHeapMbStart = gcStart,
            GcHeapMbEnd = gcEnd,
            GcHeapMbMax = gc.Count > 0 ? gc.Max(p => p.Value) : null,
            GcHeapGrowthMb = gcGrowth,
            AllocRateSampleCount = alloc.Count,
            AllocRateFirstThirdMbPerSec = allocFirst,
            AllocRateLastThirdMbPerSec = allocLast,
            AllocRateGrowthRatio = allocRatio,
            Gen2CountDelta = runtime?.Gen2CountDelta ?? 0
        };
    }

    static double? ThirdAverage(IReadOnlyList<double> values, bool first)
    {
        if (values.Count < 3)
            return null;
        int size = Math.Max(1, values.Count / 3);
        var slice = first ? values.Take(size) : values.TakeLast(size);
        return slice.Average();
    }
}
