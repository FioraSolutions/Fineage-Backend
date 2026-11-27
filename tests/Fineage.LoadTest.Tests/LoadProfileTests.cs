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
