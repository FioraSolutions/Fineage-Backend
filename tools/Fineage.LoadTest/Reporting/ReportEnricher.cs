using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Models;

namespace Fineage.LoadTest.Reporting;

public static class ReportEnricher
{
    public static void Enrich(
        RunReport report,
        LoadScenario scenario,
        IReadOnlyList<PrometheusSample> promSamples,
        IReadOnlyList<string> rawScrapes,
        IEnumerable<BotObservationLog.EntityTracker.LatencySample> attackSamples,
        IEnumerable<BotObservationLog.EntityTracker.LatencySample> skillSamples,
        IEnumerable<BotObservationLog.EntityTracker.LatencySample> legacyClientSamples,
        string? zoneLogPath,
        IEnumerable<string> logPaths,
        RuntimeCounterStats? runtime)
    {
        report.Environment = RunEnvironmentCollector.Collect();
        report.DurationSeconds = scenario.DurationSeconds;
        report.RampUpSeconds = scenario.RampUpSeconds;
        report.LatencyTable = LatencyTableBuilder.FromPeakMetrics(report.PeakMetricsText, zoneLogPath);
        report.MissingExpectedMetrics = ExpectedMetricsCatalog.MissingFrom(report.PeakMetricsText);
        report.Honest = HonestLoadSummaryBuilder.Build(
            promSamples,
            rawScrapes,
            report.StartedAt,
            report.SteadyStateStartedAt,
            attackSamples,
            skillSamples,
            legacyClientSamples);
        if (report.Honest.DataErrors.Count > 0)
            report.MissingExpectedMetrics.AddRange(report.Honest.DataErrors);
        report.TimeSeriesWindows = TimeSeriesSummaryBuilder.Build(promSamples);
        report.TimeSeriesBacklogTrend = TimeSeriesSummaryBuilder.BacklogTrend(report.TimeSeriesWindows);
        report.Runtime = runtime;
        report.StructuredLogs = StructuredLogScanner.Scan(logPaths);
        report.GateVerdicts = PerformanceGates.Evaluate(report).ToList();

        if (report.PeakMetricsText != null)
        {
            if (PrometheusParser.ParseSingletonHistogram(report.PeakMetricsText, "fineage_command_to_mailboxes_ms") is { } m)
                report.CommandToMailboxesP99Ms = m.P99;
            if (PrometheusParser.ParseSingletonHistogram(report.PeakMetricsText, "fineage_fanout_queue_oldest_age_ms") is { } f)
                report.FanoutQueueOldestAgeP99Ms = f.P99;
            report.ReliableOverflowTotal = PrometheusParser.ParseCounterSumStatic(report.PeakMetricsText, "fineage_reliable_overflow_total");
            if (report.ReliableOverflowTotal == 0)
                report.ReliableOverflowTotal = null;
        }

        if (report.CommandToMailboxesP99Ms is null or 0 && promSamples.Count > 0)
            report.CommandToMailboxesP99Ms = promSamples.Max(s => s.CommandToMailboxesP99Ms);
        if (report.FanoutQueueOldestAgeP99Ms is null or 0 && promSamples.Count > 0)
            report.FanoutQueueOldestAgeP99Ms = promSamples.Max(s => s.FanoutQueueOldestAgeMs);
    }
}