namespace Fineage.LoadTest.Reporting;

/// <summary>Issue #867 SLO gates (Debug build, same workload).</summary>
public static class PerformanceGates
{
    public const double TickP99GateMs = 200;
    public const double HeavyPlayTickP99GateMs = 50;

    static readonly Dictionary<string, double> TickP99ByScenario = new(StringComparer.OrdinalIgnoreCase)
    {
        ["perf-aoe-train"] = HeavyPlayTickP99GateMs,
        ["perf-aoe-multiparty"] = HeavyPlayTickP99GateMs,
        ["perf-aoe-combined"] = HeavyPlayTickP99GateMs
    };

    static readonly Dictionary<string, double> CommandToMailboxesP99ByScenario = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bots-50-town"] = 150,
        ["bots-100-spread"] = 150,
        ["bots-200-spread"] = 180,
        ["bots-200-town"] = 180,
        ["bots-500-siege"] = 200,
        ["bots-1000-town"] = 200
    };

    public const double FanoutQueueAgeP99GateMs = 40;

    /// <summary>
    /// #1110: the longest single tick while every bot logs out and back in. A p99 hides a lone stall (the #990 matrix
    /// saw one 30.8 s tick under a p99 that passed), so churn profiles gate the true max. Debug, local stack.
    /// </summary>
    public const double ReconnectChurnTickMaxGateMs = 1000;

    static readonly Dictionary<string, double> TickMaxByScenario = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reconnect-storm"] = ReconnectChurnTickMaxGateMs
    };

    /// <summary>RSS/queue/GC trend gates are evidence gates only once a run is long enough to show a trend.</summary>
    public const int TrendGateMinDurationSeconds = 300;

    public const double RssGrowthPercentLimit = 10;
    public const double RssGrowthAbsoluteToleranceMb = 64;
    public const double GcHeapGrowthPercentLimit = 10;
    public const double GcHeapAbsoluteToleranceMb = 32;
    public const double AllocRateGrowthRatioLimit = 1.5;

    public static IReadOnlyList<GateVerdict> Evaluate(RunReport report)
    {
        var verdicts = new List<GateVerdict>();
        string scenario = report.ScenarioName;

        double tickP99 = report.Server?.TickP99Ms ?? 0;
        double tickLimit = TickP99ByScenario.TryGetValue(scenario, out double heavyLimit) ? heavyLimit : TickP99GateMs;
        verdicts.Add(new GateVerdict
        {
            Gate = $"tick p99 <= {tickLimit:0} ms ({scenario})",
            Limit = tickLimit,
            Observed = tickP99,
            Unit = "ms",
            Passed = tickP99 <= tickLimit || tickP99 <= 0,
            Note = tickP99 <= 0 ? "no scrape data" : null
        });
        double tickP50 = report.Server?.TickP50Ms ?? 0;
        double tickMax = report.TickTrueMaxMs ?? report.Server?.TickMaxMs ?? 0;
        if (TickP99ByScenario.ContainsKey(scenario))
        {
            verdicts.Add(new GateVerdict
            {
                Gate = "heavy-play tick p50 (informative)",
                Limit = null,
                Observed = tickP50,
                Unit = "ms",
                Passed = true
            });
            verdicts.Add(new GateVerdict
            {
                Gate = "heavy-play tick max (informative)",
                Limit = null,
                Observed = tickMax,
                Unit = "ms",
                Passed = true
            });
        }

        if (TickMaxByScenario.TryGetValue(scenario, out double tickMaxLimit))
        {
            bool missingMax = tickMax <= 0;
            verdicts.Add(new GateVerdict
            {
                Gate = $"tick max <= {tickMaxLimit:0} ms ({scenario})",
                Limit = tickMaxLimit,
                Observed = tickMax,
                Unit = "ms",
                Passed = !missingMax && tickMax <= tickMaxLimit,
                Note = missingMax
                    ? "MISSING: fineage_tick_max_ms gauge and tick histogram max; a churn run without the longest tick proves nothing"
                    : "longest single tick (fineage_tick_max_ms), not a percentile"
            });
        }

        // #867 command→mailboxes is fineage_command_to_mailboxes_ms (commit to first mailbox enqueue).
        // It is not the client consistency oracle and not submit_to_mailbox, which includes the tick wait.
        double cmdMailboxes = report.CommandToMailboxesP99Ms ?? 0;
        if (CommandToMailboxesP99ByScenario.TryGetValue(scenario, out double cmdLimit))
        {
            bool missingCmd = cmdMailboxes <= 0;
            verdicts.Add(new GateVerdict
            {
                Gate = $"command→mailboxes p99 <= {cmdLimit:0} ms ({scenario})",
                Limit = cmdLimit,
                Observed = cmdMailboxes,
                Unit = "ms",
                Passed = !missingCmd && cmdMailboxes <= cmdLimit,
                Note = missingCmd
                    ? "MISSING: fineage_command_to_mailboxes_ms (server histogram; the client consistency oracle is not this gate)"
                    : "server fineage_command_to_mailboxes_ms, not the client consistency oracle"
            });
        }
        else
        {
            verdicts.Add(new GateVerdict
            {
                Gate = "command→mailboxes p99 (no scenario gate defined)",
                Limit = null,
                Observed = cmdMailboxes,
                Unit = "ms",
                Passed = true,
                Note = $"scenario `{scenario}` has no mapped #867 gate; see bots-50-town … bots-1000-town"
            });
        }

        double submitToMailbox = report.SubmitToMailboxP99Ms ?? 0;
        const double submitLimitMs = 1000;
        bool missingSubmit = submitToMailbox <= 0;
        verdicts.Add(new GateVerdict
        {
            Gate = "submit→mailbox p99 <= 1000 ms",
            Limit = submitLimitMs,
            Observed = submitToMailbox,
            Unit = "ms",
            Passed = !missingSubmit && submitToMailbox <= submitLimitMs,
            Note = missingSubmit
                ? "MISSING: fineage_command_timeline_ms phase=submit_to_mailbox (server histogram; the client consistency oracle is not this gate)"
                : "end-to-end submit_to_mailbox, including the wait for the next tick; not the #867 fanout-enqueue limit and not the client consistency oracle"
        });

        double fanoutAge = report.FanoutQueueOldestAgeP99Ms ?? 0;
        verdicts.Add(new GateVerdict
        {
            Gate = "fanout queue age p99 <= 40 ms",
            Limit = FanoutQueueAgeP99GateMs,
            Observed = fanoutAge,
            Unit = "ms",
            Passed = fanoutAge > 0 && fanoutAge <= FanoutQueueAgeP99GateMs,
            Note = fanoutAge <= 0 ? "MISSING: fineage_fanout_queue_oldest_age_ms not on scrape (PR #862)" : null
        });

        AppendResourceTrendGates(verdicts, report);

        long reliableDrops = report.ReliableOverflowTotal ?? report.Server?.OutboundDropped ?? 0;
        verdicts.Add(new GateVerdict
        {
            Gate = "reliable drops / overflow == 0",
            Limit = 0,
            Observed = reliableDrops,
            Unit = "count",
            Passed = reliableDrops == 0,
            Note = report.ReliableOverflowTotal == null && report.Server != null
                ? "using fineage_outbound_dropped_total until fineage_reliable_overflow_total exists"
                : null
        });

        if (!report.Passed)
        {
            verdicts.Add(new GateVerdict
            {
                Gate = "load-test runner pass",
                Limit = null,
                Observed = report.FailReasons.Count,
                Unit = "fail_reasons",
                Passed = false,
                Note = string.Join("; ", report.FailReasons.Take(3))
            });
        }

        return verdicts;
    }

    static void AppendResourceTrendGates(List<GateVerdict> verdicts, RunReport report)
    {
        bool evidenceRun = report.DurationSeconds >= TrendGateMinDurationSeconds;
        var trend = report.ResourceTrend;

        if (trend is { HasRssTrend: true })
        {
            double start = trend.RssMbStart ?? 0;
            double growth = trend.RssGrowthMb ?? 0;
            double allowed = Math.Max(RssGrowthAbsoluteToleranceMb, start * RssGrowthPercentLimit / 100.0);
            verdicts.Add(new GateVerdict
            {
                Gate = "RSS steady-state growth <= max(10%, 64 MiB)",
                Limit = allowed,
                Observed = growth,
                Unit = "MiB",
                Passed = growth <= allowed,
                Note =
                    $"start **{start:0.0}** MiB → end **{trend.RssMbEnd ?? 0:0.0}** MiB, max **{trend.RssMbMax ?? 0:0.0}** MiB, " +
                    $"**{trend.RssGrowthPercent ?? 0:0.0}%** over {trend.RssSampleCount} steady-state samples"
            });
        }
        else
        {
            AddMissingTrendGate(verdicts, "RSS steady-state growth", evidenceRun,
                "MISSING: no steady-state RSS samples; pass --zone-pid and ensure /proc or ps can read the Zone process");
        }

        if (trend is { HasGcTrend: true })
        {
            double start = trend.GcHeapMbStart ?? 0;
            double growth = trend.GcHeapGrowthMb ?? 0;
            double allowed = Math.Max(GcHeapAbsoluteToleranceMb, start * GcHeapGrowthPercentLimit / 100.0);
            verdicts.Add(new GateVerdict
            {
                Gate = "GC heap steady-state growth <= max(10%, 32 MiB)",
                Limit = allowed,
                Observed = growth,
                Unit = "MiB",
                Passed = growth <= allowed,
                Note =
                    $"start **{start:0.0}** MiB → end **{trend.GcHeapMbEnd ?? 0:0.0}** MiB, " +
                    $"gen2 Δ **{trend.Gen2CountDelta}** over {trend.GcHeapSampleCount} steady-state samples"
            });
        }
        else
        {
            AddMissingTrendGate(verdicts, "GC heap steady-state growth", evidenceRun,
                "MISSING: no steady-state dotnet-counters gc-heap-size samples (install dotnet-counters and pass --zone-pid)");
        }

        if (trend is { HasAllocTrend: true } && trend.AllocRateGrowthRatio is { } ratio)
        {
            verdicts.Add(new GateVerdict
            {
                Gate = "allocation rate last third / first third <= 1.5",
                Limit = AllocRateGrowthRatioLimit,
                Observed = ratio,
                Unit = "ratio",
                Passed = ratio <= AllocRateGrowthRatioLimit,
                Note =
                    $"first third **{trend.AllocRateFirstThirdMbPerSec ?? 0:0.0}** MiB/s, " +
                    $"last third **{trend.AllocRateLastThirdMbPerSec ?? 0:0.0}** MiB/s over {trend.AllocRateSampleCount} samples"
            });
        }
        else
        {
            AddMissingTrendGate(verdicts, "allocation rate trend", evidenceRun,
                "MISSING: no steady-state alloc-rate samples, or fewer than 3 samples");
        }

        AppendQueueTrendGate(verdicts, report, "fanout depth", w => w.FanoutQueueDepthMax,
            "fanout_queue_depth metric absent", "fineage_fanout_queue_depth", evidenceRun);
        AppendQueueTrendGate(verdicts, report, "command depth", w => w.CommandQueueDepthMax,
            "command_queue_depth metric absent", "fineage_command_queue_depth", evidenceRun);
        AppendQueueTrendGate(verdicts, report, "command ingress depth", w => w.CommandIngressQueuedMax,
            "fineage_command_ingress_queued metric absent", "fineage_command_ingress_queued", evidenceRun);
    }

    static void AppendQueueTrendGate(
        List<GateVerdict> verdicts,
        RunReport report,
        string label,
        Func<TimeSeriesWindow, double> selector,
        string absentText,
        string metricName,
        bool evidenceRun)
    {
        var windows = report.TimeSeriesWindows;
        string trend = TimeSeriesSummaryBuilder.Trend(windows, selector, absentText);
        // A present metric with an all-zero histogram is flat, not missing. The peak scrape is the
        // presence evidence; the numeric series alone cannot distinguish zero from absent.
        if (trend == absentText &&
            report.PeakMetricsText?.Contains(metricName, StringComparison.Ordinal) == true)
            trend = "flat";
        double first = ThirdMax(windows, selector, first: true);
        double last = ThirdMax(windows, selector, first: false);
        double limit = first * 1.25 + 1;
        bool trendKnown = trend is not ("insufficient windows" or "growing") && !trend.Contains("absent", StringComparison.Ordinal);
        bool passed = trend == "flat" || trend == "shrinking" || (trendKnown && last <= limit);
        string note =
            $"early third max **{first:0}**, late third max **{last:0}**, trend **{trend}**; " +
            "gate fails on 'growing' over a run of at least 5 minutes";
        if (!evidenceRun && (windows.Count < 2 || !trendKnown))
        {
            passed = true;
            note = $"informative only: run is shorter than {TrendGateMinDurationSeconds}s ({note})";
        }
        else if (!trendKnown)
        {
            passed = false;
        }

        verdicts.Add(new GateVerdict
        {
            Gate = $"queue trend: {label} not growing",
            Limit = first > 0 ? limit : null,
            Observed = last,
            Unit = "depth",
            Passed = passed,
            Note = note
        });
    }

    static double ThirdMax(IReadOnlyList<TimeSeriesWindow> windows, Func<TimeSeriesWindow, double> selector, bool first)
    {
        if (windows.Count == 0)
            return 0;
        int size = Math.Max(1, windows.Count / 3);
        var slice = first ? windows.Take(size) : windows.TakeLast(size);
        return slice.Max(selector);
    }

    static void AddMissingTrendGate(List<GateVerdict> verdicts, string name, bool evidenceRun, string missing)
    {
        verdicts.Add(new GateVerdict
        {
            Gate = $"{name} (evidence)",
            Limit = null,
            Observed = 0,
            Unit = "",
            Passed = !evidenceRun,
            Note = evidenceRun
                ? missing
                : $"informative only: run is shorter than {TrendGateMinDurationSeconds}s; {missing}"
        });
    }
}

public sealed class GateVerdict
{
    public required string Gate { get; init; }
    public double? Limit { get; init; }
    public double Observed { get; init; }
    public string Unit { get; init; } = "";
    public bool Passed { get; init; }
    public string? Note { get; init; }
}