using System.Diagnostics;
using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Reporting;
using Xunit;

namespace Fineage.LoadTest.Tests;

public class LoadProfileTests
{
    static readonly string[] RequiredProfiles =
    [
        "pve-farming",
        "party-farming",
        "raid-boss-observe",
        "reconnect-storm",
        "full-world-1-player"
    ];

    [Fact]
    public void Required_profiles_exist_and_load()
    {
        foreach (string name in RequiredProfiles)
        {
            string path = Path.Combine(RepoRoot(), "tools", "Fineage.LoadTest", "scenarios", $"{name}.yaml");
            Assert.True(File.Exists(path), path);
            var scenario = Fineage.LoadTest.ScenarioLoader.Load(path);
            Assert.Equal(name, scenario.Name);
            Assert.True(scenario.BotCount > 0);
            Assert.True(scenario.DurationSeconds > 0);
        }
    }

    [Fact]
    public void Matrix_script_lists_every_required_profile()
    {
        string script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "run-load-test.sh"));
        foreach (string name in RequiredProfiles)
            Assert.Contains(name, script);
        Assert.Contains("fast)", script);
        Assert.Contains("soak)", script);
        Assert.Contains("endurance)", script);
        Assert.Contains("stability)", script);
    }

    [Fact]
    public void Matrix_script_is_valid_bash()
    {
        if (OperatingSystem.IsWindows())
            return;
        string script = Path.Combine(RepoRoot(), "scripts", "run-load-test.sh");
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add(script);
        using var proc = Process.Start(psi)!;
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, stderr);
    }

    [Fact]
    public void Stable_resource_trends_pass_the_release_gates()
    {
        var gates = PerformanceGates.Evaluate(Report(HealthyTrend(), durationSeconds: 900, windows: StableQueues()));
        Assert.All(gates.Where(g => g.Gate.Contains("RSS", StringComparison.Ordinal) ||
                                    g.Gate.Contains("GC heap", StringComparison.Ordinal) ||
                                    g.Gate.Contains("allocation", StringComparison.Ordinal) ||
                                    g.Gate.Contains("queue trend", StringComparison.Ordinal)),
            gate => Assert.True(gate.Passed, $"{gate.Gate}: {gate.Note}"));
    }

    [Fact]
    public void Growing_rss_fails_the_release_gate()
    {
        var trend = HealthyTrend() with { RssGrowthMb = 200, RssGrowthPercent = 40 };
        var gates = PerformanceGates.Evaluate(Report(trend, durationSeconds: 900, windows: StableQueues()));
        var gate = Assert.Single(gates, g => g.Gate.Contains("RSS", StringComparison.Ordinal));
        Assert.False(gate.Passed);
        Assert.Contains("MiB", gate.Unit);
    }

    [Fact]
    public void Growing_queue_depth_fails_the_release_gate()
    {
        var windows = StableQueues();
        for (int i = windows.Count - 2; i < windows.Count; i++)
            windows[i] = new TimeSeriesWindow { FanoutQueueDepthMax = 30, CommandQueueDepthMax = 5, CommandIngressQueuedMax = 3 };
        var gates = PerformanceGates.Evaluate(Report(HealthyTrend(), durationSeconds: 900, windows: windows));
        var gate = Assert.Single(gates, g => g.Gate.Contains("queue trend: fanout depth", StringComparison.Ordinal));
        Assert.False(gate.Passed);
        Assert.Contains("growing", gate.Note ?? "");
    }

    [Fact]
    public void Lifetime_histogram_high_water_moving_one_bucket_is_not_queue_growth()
    {
        // Mirrors the #951 Debug 300 s run: one sample in (2, 4] ratcheted the
        // cumulative fineage_fanout_queue_depth max from 2 to 4. That is not a backlog.
        var windows = Enumerable.Range(0, 9)
            .Select(i => new TimeSeriesWindow
            {
                FanoutQueueDepthMax = i < 5 ? 2 : 4,
                CommandQueueDepthMax = 1,
                CommandIngressQueuedMax = 0
            })
            .ToList();
        var gates = PerformanceGates.Evaluate(Report(HealthyTrend(), durationSeconds: 300, windows: windows));
        var gate = Assert.Single(gates, g => g.Gate.Contains("queue trend: fanout depth", StringComparison.Ordinal));
        Assert.True(gate.Passed, gate.Note);
        Assert.True(gate.Passed, gate.Note);
        Assert.Contains("flat", gate.Note ?? "");
    }

    [Fact]
    public void Lifetime_histogram_high_water_moving_one_count_bounds_bucket_is_not_queue_growth()
    {
        // #867 300 s bots-200-town: 38 of 109406 samples in (32, 64] ratcheted the
        // cumulative fanout_queue_depth max from 32 to 64. Age p99 stayed ~1.3 ms.
        var windows = Enumerable.Range(0, 9)
            .Select(i => new TimeSeriesWindow
            {
                FanoutQueueDepthMax = i < 5 ? 32 : 64,
                CommandQueueDepthMax = 1,
                CommandIngressQueuedMax = 0
            })
            .ToList();
        var gates = PerformanceGates.Evaluate(Report(HealthyTrend(), durationSeconds: 300, windows: windows));
        var gate = Assert.Single(gates, g => g.Gate.Contains("queue trend: fanout depth", StringComparison.Ordinal));
        Assert.True(gate.Passed, gate.Note);
        Assert.Contains("flat", gate.Note ?? "");
    }

    [Fact]
    public void One_late_gauge_spike_is_not_queue_growth()
    {
        // #867 confirmation run: command ingress is a point-in-time gauge. Almost every
        // 10 s window was 0; one late scrape saw 79 commands waiting for the next tick.
        var windows = Enumerable.Range(0, 30)
            .Select(i => new TimeSeriesWindow
            {
                FanoutQueueDepthMax = 8,
                CommandQueueDepthMax = 1,
                CommandIngressQueuedMax = i == 26 ? 79 : 0
            })
            .ToList();
        var gates = PerformanceGates.Evaluate(Report(HealthyTrend(), durationSeconds: 300, windows: windows));
        var gate = Assert.Single(gates, g => g.Gate.Contains("queue trend: command ingress", StringComparison.Ordinal));
        Assert.True(gate.Passed, gate.Note);
        Assert.DoesNotContain("trend **growing**", gate.Note ?? "");
    }

    [Fact]
    public void Zero_queue_with_the_metric_present_is_flat_not_missing()
    {
        var windows = Enumerable.Range(0, 6)
            .Select(_ => new TimeSeriesWindow())
            .ToList();
        var report = Report(HealthyTrend(), durationSeconds: 900, windows: windows,
            peakMetrics: "# TYPE fineage_fanout_queue_depth histogram\n" +
                         "fineage_command_queue_depth_bucket{le=\"+Inf\"} 0\n" +
                         "fineage_command_ingress_queued 0\n");
        var gates = PerformanceGates.Evaluate(report);
        Assert.All(gates.Where(g => g.Gate.Contains("queue trend", StringComparison.Ordinal)),
            gate => Assert.True(gate.Passed, $"{gate.Gate}: {gate.Note}"));
    }

    [Fact]
    public void Missing_trend_evidence_is_informative_only_for_short_runs_and_hard_for_soaks()
    {
        var shortGates = PerformanceGates.Evaluate(Report(null, durationSeconds: 60, windows: StableQueues()));
        Assert.All(shortGates.Where(g => g.Gate.Contains("RSS", StringComparison.Ordinal)), gate => Assert.True(gate.Passed));

        var soakGates = PerformanceGates.Evaluate(Report(null, durationSeconds: 900, windows: StableQueues()));
        Assert.Contains(soakGates, gate => gate.Gate.Contains("RSS", StringComparison.Ordinal) && !gate.Passed);
    }

    [Fact]
    public void Growing_heap_on_a_short_run_is_informative_and_fails_a_soak()
    {
        var trend = HealthyTrend() with { GcHeapGrowthMb = 200, GcHeapMbEnd = 400 };
        var shortGates = PerformanceGates.Evaluate(Report(trend, durationSeconds: 120, windows: StableQueues()));
        var shortHeap = Assert.Single(shortGates, g => g.Gate.Contains("GC heap", StringComparison.Ordinal));
        Assert.True(shortHeap.Passed, shortHeap.Note);
        Assert.Contains("informative only", shortHeap.Note);

        var soakGates = PerformanceGates.Evaluate(Report(trend, durationSeconds: 300, windows: StableQueues()));
        var soakHeap = Assert.Single(soakGates, g => g.Gate.Contains("GC heap", StringComparison.Ordinal));
        Assert.False(soakHeap.Passed, soakHeap.Note);
        Assert.DoesNotContain("informative only", soakHeap.Note ?? "");
    }

    [Fact]
    public void Full_world_one_player_gates_use_honest_histogram_windows()
    {
        var pass = FullWorldReport(overallP99: 11.9, steadyP99: 1.9, steadyP999: 7.9, tickMax: 81.7);
        var passGates = PerformanceGates.Evaluate(pass);
        Assert.All(passGates.Where(g => g.Gate.StartsWith("full-world", StringComparison.Ordinal)),
            g => Assert.True(g.Passed, $"{g.Gate}: {g.Observed} ({g.Note})"));

        var fail = FullWorldReport(overallP99: 80, steadyP99: 60, steadyP999: 150, tickMax: 250);
        var failGates = PerformanceGates.Evaluate(fail);
        Assert.Contains(failGates, g => g.Gate.StartsWith("full-world overall tick p99", StringComparison.Ordinal) && !g.Passed);
        Assert.Contains(failGates, g => g.Gate.StartsWith("full-world steady tick p99 ", StringComparison.Ordinal) && !g.Passed);
        Assert.Contains(failGates, g => g.Gate.StartsWith("full-world steady tick p99.9", StringComparison.Ordinal) && !g.Passed);
        Assert.Contains(failGates, g => g.Gate.StartsWith("full-world tick max", StringComparison.Ordinal) && !g.Passed);
    }

    static RunReport FullWorldReport(double overallP99, double steadyP99, double steadyP999, double tickMax) =>
        new()
        {
            ScenarioName = "full-world-1-player",
            Target = "fineage",
            BotCount = 1,
            DurationSeconds = 120,
            ResourceTrend = HealthyTrend(),
            TimeSeriesWindows = StableQueues(),
            Server = new PrometheusAggregate { TickP99Ms = 10 },
            TickTrueMaxMs = tickMax,
            Honest = new HonestLoadSummary
            {
                TickOverall = new TickWindowStats
                {
                    Rule = "whole run, ramp included",
                    Ticks = 588,
                    P99Ms = overallP99,
                    P999Ms = 88,
                    MaxMs = tickMax,
                    MaxExact = true
                },
                TickSteady = new TickWindowStats
                {
                    Rule = "from first scrape with zone online = 1 + 30 s",
                    Ticks = 426,
                    P99Ms = steadyP99,
                    P999Ms = steadyP999,
                    MaxMs = 10,
                    MaxExact = false
                }
            }
        };

    [Fact]
    public void Csv_runtime_samples_expose_gc_and_allocation_series()
    {
        const string csv = """
            Timestamp,Provider,Counter Name,Counter Type,Value
            2026-11-23T00:00:01Z,System.Runtime,GC Heap Size (MB),Metric,220
            2026-11-23T00:00:02Z,System.Runtime,GC Heap Size (MB),Metric,230
            2026-11-23T00:00:01Z,System.Runtime,Allocation Rate (B / sec),Metric,1048576
            2026-11-23T00:00:02Z,System.Runtime,Allocation Rate (B / sec),Metric,2097152
            """;
        var stats = RuntimeCountersSampler.ParseCsv(csv);
        Assert.Equal(2, stats.GcHeapMbSeries.Count);
        Assert.Equal(220, stats.GcHeapMbSeries[0].Value);
        Assert.Equal(2, stats.AllocRateMbPerSecSeries.Count);
        Assert.Equal(2, stats.AllocRateMbPerSecSeries[^1].Value);
    }

    [Fact]
    public void Runtime_csv_naive_timestamps_are_local_clock_not_utc()
    {
        const string csv = """
            Timestamp,Provider,Counter Name,Counter Type,Value
            2026-11-28T05:19:36,System.Runtime,GC Heap Size (MB),Metric,466
            2026-11-28T05:19:36Z,System.Runtime,GC Heap Size (MB),Metric,999
            2026-11-28T05:19:36+03:00,System.Runtime,GC Heap Size (MB),Metric,100
            """;
        var stats = RuntimeCountersSampler.ParseCsv(csv);
        Assert.Equal(3, stats.GcHeapMbSeries.Count);
        var naive = stats.GcHeapMbSeries[0].At.ToUniversalTime();
        var zulu = stats.GcHeapMbSeries[1].At.ToUniversalTime();
        var offset = stats.GcHeapMbSeries[2].At.ToUniversalTime();
        var expectedLocal = new DateTimeOffset(new DateTime(2026, 11, 28, 5, 19, 36, DateTimeKind.Local)).ToUniversalTime();
        Assert.Equal(expectedLocal, naive);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 5, 19, 36, TimeSpan.Zero), zulu);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 2, 19, 36, TimeSpan.Zero), offset);
    }

    [Fact]
    public void Gc_heap_trend_excludes_samples_before_steady_state()
    {
        var runtime = new RuntimeCounterStats
        {
            GcHeapMbSeries =
            [
                new RuntimeMetricPoint(new DateTimeOffset(2026, 11, 28, 2, 19, 36, TimeSpan.Zero), 466),
                new RuntimeMetricPoint(new DateTimeOffset(2026, 11, 28, 2, 21, 6, TimeSpan.Zero), 747),
                new RuntimeMetricPoint(new DateTimeOffset(2026, 11, 28, 2, 25, 36, TimeSpan.Zero), 804)
            ]
        };
        var trend = ResourceTrendSummaryBuilder.Build(
            new DateTimeOffset(2026, 11, 28, 2, 21, 5, TimeSpan.Zero),
            rssSamples: null,
            runtime);
        Assert.Equal(2, trend.GcHeapSampleCount);
        Assert.Equal(747, trend.GcHeapMbStart);
        Assert.Equal(804, trend.GcHeapMbEnd);
        Assert.Equal(57, trend.GcHeapGrowthMb);
        // 10% of 747 is 74.7, above the 32 MiB floor, so this window passes the #990 heap gate.
        var report = Report(trend, durationSeconds: 300, windows: StableQueues());
        var gate = Assert.Single(PerformanceGates.Evaluate(report), g => g.Gate.Contains("GC heap", StringComparison.Ordinal));
        Assert.True(gate.Passed, gate.Note);
    }

    static RunReport Report(
        ResourceTrendSummary? trend,
        int durationSeconds,
        List<TimeSeriesWindow> windows,
        string? peakMetrics = null) =>
        new()
        {
            ScenarioName = "soak-fixture",
            Target = "fineage",
            BotCount = 50,
            DurationSeconds = durationSeconds,
            ResourceTrend = trend,
            TimeSeriesWindows = windows,
            PeakMetricsText = peakMetrics,
            Server = new PrometheusAggregate { TickP99Ms = 10 }
        };

    static ResourceTrendSummary HealthyTrend() => new()
    {
        RssSampleCount = 12,
        RssMbStart = 500,
        RssMbEnd = 510,
        RssMbMax = 515,
        RssGrowthMb = 10,
        RssGrowthPercent = 2,
        GcHeapSampleCount = 12,
        GcHeapMbStart = 200,
        GcHeapMbEnd = 205,
        GcHeapMbMax = 210,
        GcHeapGrowthMb = 5,
        AllocRateSampleCount = 12,
        AllocRateFirstThirdMbPerSec = 1,
        AllocRateLastThirdMbPerSec = 1,
        AllocRateGrowthRatio = 1,
        Gen2CountDelta = 0
    };

    static List<TimeSeriesWindow> StableQueues() =>
        Enumerable.Range(0, 6)
            .Select(_ => new TimeSeriesWindow
            {
                FanoutQueueDepthMax = 10,
                CommandQueueDepthMax = 5,
                CommandIngressQueuedMax = 3
            })
            .ToList();

    static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools", "Fineage.LoadTest", "scenarios")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repository root not found from " + AppContext.BaseDirectory);
    }
}
