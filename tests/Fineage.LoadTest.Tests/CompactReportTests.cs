using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Models;
using Fineage.LoadTest.Reporting;
using Xunit;

namespace Fineage.LoadTest.Tests;

public class CompactReportTests
{
    static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "metrics-peak-sample.txt");

    [Fact]
    public void Missing_metrics_lists_absent_862_names()
    {
        string peak = File.ReadAllText(FixturePath);
        var missing = ExpectedMetricsCatalog.MissingFrom(peak);
        Assert.Contains("fineage_command_to_mailboxes_ms", missing);
        Assert.Contains("fineage_fanout_queue_oldest_age_ms", missing);
        Assert.DoesNotContain("fineage_tick_ms", missing);
    }

    [Fact]
    public void Latency_table_includes_tick_and_commands_stages()
    {
        string peak = File.ReadAllText(FixturePath);
        var rows = LatencyTableBuilder.FromPeakMetrics(peak, zoneLogPath: null);
        Assert.Contains(rows, r => r.Name.StartsWith("tick (histogram", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Name == "tick_stage/commands");
    }

    [Fact]
    public void Markdown_under_size_cap_and_includes_gates()
    {
        string peak = File.ReadAllText(FixturePath);
        var report = new RunReport
        {
            ScenarioName = "bots-50-town",
            Target = "fineage",
            BotCount = 50,
            BotsInWorld = 50,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            FinishedAt = DateTimeOffset.UtcNow,
            PeakMetricsText = peak,
            Server = PrometheusAggregate.FromSamples([PrometheusParser.Parse(peak)], peak),
            Passed = true
        };
        ReportEnricher.Enrich(report, new LoadScenario
        {
            Name = "bots-50-town",
            BotCount = 50,
            DurationSeconds = 60,
            RampUpSeconds = 10
        }, [PrometheusParser.Parse(peak)], [peak], [], [], [], zoneLogPath: null, [], runtime: null);

        string md = CompactMarkdownRenderer.Render(report);
        Assert.Contains("## Verdict vs #867 gates", md);
        Assert.Contains("client consistency oracle", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fanout drops", md);
        Assert.Contains("bots-50-town", md);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(md) <= CompactMarkdownRenderer.TargetMaxBytes);
    }

    [Fact]
    public void Performance_gate_marks_missing_fanout_metric()
    {
        var report = new RunReport
        {
            ScenarioName = "bots-50-town",
            Target = "fineage",
            BotCount = 50,
            Server = new PrometheusAggregate { TickP99Ms = 80 },
            Passed = true
        };
        var gates = PerformanceGates.Evaluate(report);
        var fanout = Assert.Single(gates, g => g.Gate.Contains("fanout queue"));
        Assert.False(fanout.Passed);
        Assert.Contains("MISSING", fanout.Note ?? "");
    }

    [Theory]
    [InlineData(30_800.0, false)]
    [InlineData(1_200.0, false)]
    [InlineData(236.8, true)]
    public void Reconnect_churn_gates_the_longest_tick_not_a_percentile(double tickMax, bool passes)
    {
        var report = new RunReport
        {
            ScenarioName = "reconnect-storm",
            Target = "fineage",
            BotCount = 200,
            // #990: one 30.8 s tick under a p99 that still passed.
            Server = new PrometheusAggregate { TickP99Ms = 57.6 },
            TickTrueMaxMs = tickMax,
            Passed = true
        };

        var gate = Assert.Single(PerformanceGates.Evaluate(report), g => g.Gate.StartsWith("tick max", StringComparison.Ordinal));
        Assert.Equal(PerformanceGates.ReconnectChurnTickMaxGateMs, gate.Limit);
        Assert.Equal(passes, gate.Passed);
    }

    [Fact]
    public void Reconnect_churn_without_a_tick_max_fails_its_gate()
    {
        var report = new RunReport
        {
            ScenarioName = "reconnect-storm",
            Target = "fineage",
            BotCount = 200,
            Server = new PrometheusAggregate { TickP99Ms = 40 },
            Passed = true
        };

        var gate = Assert.Single(PerformanceGates.Evaluate(report), g => g.Gate.StartsWith("tick max", StringComparison.Ordinal));
        Assert.False(gate.Passed);
        Assert.Contains("MISSING", gate.Note ?? "");
    }

    [Fact]
    public void Other_profiles_have_no_tick_max_gate()
    {
        var report = new RunReport { ScenarioName = "bots-50-town", Target = "fineage", Server = new PrometheusAggregate { TickP99Ms = 40 } };
        Assert.DoesNotContain(PerformanceGates.Evaluate(report), g => g.Gate.StartsWith("tick max", StringComparison.Ordinal));
    }
}
