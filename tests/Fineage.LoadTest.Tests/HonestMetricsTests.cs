using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Models;
using Fineage.LoadTest.Reporting;
using Xunit;

namespace Fineage.LoadTest.Tests;

public class HonestMetricsTests
{
    const string Peak = """
        # TYPE fineage_command_timeline_ms histogram
        fineage_command_timeline_ms_bucket{phase="submit_to_mailbox",le="10"} 10
        fineage_command_timeline_ms_bucket{phase="submit_to_mailbox",le="50"} 20
        fineage_command_timeline_ms_bucket{phase="submit_to_mailbox",le="+Inf"} 20
        fineage_command_timeline_ms_sum{phase="submit_to_mailbox"} 200
        fineage_command_timeline_ms_count{phase="submit_to_mailbox"} 20
        fineage_command_timeline_ms_bucket{phase="fanout_enqueue_to_mailbox",le="5"} 20
        fineage_command_timeline_ms_bucket{phase="fanout_enqueue_to_mailbox",le="+Inf"} 20
        fineage_command_timeline_ms_sum{phase="fanout_enqueue_to_mailbox"} 40
        fineage_command_timeline_ms_count{phase="fanout_enqueue_to_mailbox"} 20
        # TYPE fineage_fanout_queue_depth histogram
        fineage_fanout_queue_depth_bucket{le="1"} 0
        fineage_fanout_queue_depth_bucket{le="8"} 4
        fineage_fanout_queue_depth_bucket{le="64"} 10
        fineage_fanout_queue_depth_bucket{le="+Inf"} 10
        fineage_fanout_queue_depth_sum 80
        fineage_fanout_queue_depth_count 10
        fineage_command_coalesced_total 7
        fineage_command_rejected_total{reason="queue_depth"} 3
        fineage_command_rejected_total{reason="queue_age"} 1
        fineage_tick_max_ms 4123
        fineage_step_watchdog_stall_max_ms 30000
        fineage_step_watchdog_stalls 2
        fineage_outbound_dropped_total 4
        fineage_fanout_dropped_total 9
        """;

    [Fact]
    public void Parser_keeps_submit_mailbox_separate_from_fanout_enqueue_and_reads_histogram_depth()
    {
        var sample = PrometheusParser.Parse(Peak);
        Assert.True(sample.SubmitToMailboxP99Ms > sample.FanoutEnqueueToMailboxP99Ms);
        Assert.True(sample.FanoutEnqueueToMailboxP99Ms > 0);
        Assert.True(sample.FanoutEnqueueToMailboxP99Ms <= 5);
        Assert.Equal(64, sample.FanoutQueueDepth);
        Assert.Equal(7, sample.CommandCoalesced);
        Assert.Equal(4, sample.CommandRejected);
        Assert.Equal(4123, sample.TickTrueMaxMs);
        Assert.Equal(30000, sample.WatchdogStallMaxMs);
        Assert.Equal(2, sample.WatchdogStalls);
        Assert.Equal(4, sample.OutboundDropped);
        Assert.Equal(9, sample.FanoutDropped);
    }

    [Fact]
    public void Gate_uses_submit_to_mailbox_and_fails_when_the_series_is_missing()
    {
        var report = new RunReport
        {
            ScenarioName = "bots-50-town",
            Target = "fineage",
            Server = new PrometheusAggregate { TickP99Ms = 40 }
        };
        var gates = PerformanceGates.Evaluate(report);
        var gate = Assert.Single(gates, g => g.Gate.Contains("submit→mailbox"));
        Assert.False(gate.Passed);
        Assert.Contains("MISSING", gate.Note);
        Assert.Contains("oracle", gate.Note, StringComparison.OrdinalIgnoreCase);
        var command = Assert.Single(gates, g => g.Gate.Contains("command→mailboxes"));
        Assert.False(command.Passed);
        Assert.Contains("fineage_command_to_mailboxes_ms", command.Note);
    }

    [Fact]
    public void Enrich_gates_on_samples_when_the_peak_scrape_has_no_timeline()
    {
        var report = new RunReport
        {
            ScenarioName = "bots-50-town",
            Target = "fineage",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            FinishedAt = DateTimeOffset.UtcNow,
            PeakMetricsText = "# no command timeline on this scrape\n",
            Server = new PrometheusAggregate { TickP99Ms = 40 }
        };
        var sample = PrometheusParser.Parse(Peak);
        ReportEnricher.Enrich(
            report,
            new LoadScenario { Name = "bots-50-town", BotCount = 50, DurationSeconds = 60, RampUpSeconds = 10 },
            [sample],
            [Peak],
            [],
            [],
            [],
            zoneLogPath: null,
            [],
            runtime: null);
        var gate = Assert.Single(report.GateVerdicts, g => g.Gate.Contains("submit→mailbox"));
        Assert.True(gate.Observed > 0);
        Assert.True(gate.Passed);
    }

    [Fact]
    public void Gc_mode_comes_from_the_zone_runtimeconfig_not_a_guess()
    {
        Assert.Equal("Server", RunEnvironmentCollector.GcModeFromRuntimeConfig(
            """{"configProperties":{"System.GC.Server": true}}""", "zone.runtimeconfig.json"));
        Assert.Equal("Workstation", RunEnvironmentCollector.GcModeFromRuntimeConfig(
            """{"configProperties":{"System.GC.Server": false}}""", "zone.runtimeconfig.json"));
        Assert.Contains("MISSING", RunEnvironmentCollector.GcModeFromRuntimeConfig("{}", "zone.runtimeconfig.json"));
    }

    [Fact]
    public void Csv_collect_format_supplies_real_cpu_percent()
    {
        const string csv = """
            Timestamp,Provider,Counter Name,Counter Type,Value
            2026-11-23T00:00:00Z,System.Runtime,CPU Usage (%),Metric,12.5
            2026-11-23T00:00:01Z,System.Runtime,CPU Usage (%),Metric,40
            2026-11-23T00:00:01Z,System.Runtime,GC Heap Size (MB),Metric,220
            2026-11-23T00:00:01Z,System.Runtime,Gen 0 GC Count,Metric,8
            2026-11-23T00:00:02Z,System.Runtime,Gen 0 GC Count,Metric,11
            """;
        var stats = RuntimeCountersSampler.ParseCsv(csv);
        Assert.Equal("dotnet-counters csv", stats.Source);
        Assert.Null(stats.UnavailableReason);
        Assert.Equal(12.5, stats.CpuPercentMin);
        Assert.Equal(40, stats.CpuPercentMax);
        Assert.Equal(220, stats.GcHeapMbMax);
        Assert.Equal(3, stats.Gen0CountDelta);
    }

    [Fact]
    public void Csv_interval_increments_sum_instead_of_looking_like_a_cumulative_gauge()
    {
        const string csv = """
            Timestamp,Provider,Counter Name,Counter Type,Mean/Increment
            2026-11-23T00:00:00Z,System.Runtime,CPU Usage (%),Metric,8
            2026-11-23T00:00:01Z,System.Runtime,Gen 0 GC Count (Count / 1 sec),Metric,2
            2026-11-23T00:00:02Z,System.Runtime,Gen 0 GC Count (Count / 1 sec),Metric,3
            """;
        var stats = RuntimeCountersSampler.ParseCsv(csv);
        Assert.Equal(5, stats.Gen0CountDelta);
        Assert.Equal(8, stats.CpuPercentAvg);
    }
}
