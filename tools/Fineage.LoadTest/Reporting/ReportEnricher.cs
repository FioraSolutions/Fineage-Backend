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
        RuntimeCounterStats? runtime,
        int? zonePid = null)
    {
        report.Environment = RunEnvironmentCollector.Collect(zonePid);
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

        if (report.PeakMetricsText != null)
        {
            var peak = PrometheusParser.Parse(report.PeakMetricsText);
            report.SubmitToMailboxP99Ms = peak.SubmitToMailboxP99Ms;
            report.CommandToMailboxesP99Ms = peak.SubmitToMailboxP99Ms;
            report.FanoutEnqueueToMailboxP99Ms = peak.FanoutEnqueueToMailboxP99Ms;
            if (PrometheusParser.ParseSingletonHistogram(report.PeakMetricsText, "fineage_fanout_queue_oldest_age_ms") is { } f)
                report.FanoutQueueOldestAgeP99Ms = f.P99;
            report.TickTrueMaxMs = peak.TickTrueMaxMs;
            report.WatchdogStallMaxMs = peak.WatchdogStallMaxMs;
            report.WatchdogStalls = peak.WatchdogStalls;
            report.FanoutDropped = peak.FanoutDropped;
            report.ReliableOverflowTotal = PrometheusParser.ParseCounterSumStatic(report.PeakMetricsText, "fineage_reliable_overflow_total");
            if (report.ReliableOverflowTotal == 0)
                report.ReliableOverflowTotal = null;
        }

        if (report.SubmitToMailboxP99Ms is null or 0 && promSamples.Count > 0)
        {
            report.SubmitToMailboxP99Ms = promSamples.Max(s => s.SubmitToMailboxP99Ms);
            report.CommandToMailboxesP99Ms = report.SubmitToMailboxP99Ms;
        }

        if (report.FanoutEnqueueToMailboxP99Ms is null or 0 && promSamples.Count > 0)
            report.FanoutEnqueueToMailboxP99Ms = promSamples.Max(s => s.FanoutEnqueueToMailboxP99Ms);
        if (report.FanoutQueueOldestAgeP99Ms is null or 0 && promSamples.Count > 0)
            report.FanoutQueueOldestAgeP99Ms = promSamples.Max(s => s.FanoutQueueOldestAgeMs);
        if (promSamples.Count > 0)
        {
            report.TickTrueMaxMs = Math.Max(report.TickTrueMaxMs ?? 0, promSamples.Max(s => s.TickTrueMaxMs));
            report.WatchdogStallMaxMs = Math.Max(report.WatchdogStallMaxMs ?? 0, promSamples.Max(s => s.WatchdogStallMaxMs));
            report.WatchdogStalls = Math.Max(report.WatchdogStalls, promSamples.Max(s => s.WatchdogStalls));
            report.FanoutDropped = Math.Max(report.FanoutDropped, promSamples.Max(s => s.FanoutDropped));
        }

        report.GateVerdicts = PerformanceGates.Evaluate(report).ToList();
    }
}