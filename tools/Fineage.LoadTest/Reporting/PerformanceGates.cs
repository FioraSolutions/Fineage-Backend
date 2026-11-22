namespace Fineage.LoadTest.Reporting;

/// <summary>Issue #867 SLO gates (Debug build, same workload).</summary>
public static class PerformanceGates
{
    public const double TickP99GateMs = 200;

    static readonly Dictionary<string, double> CommandToMailboxesP99ByScenario = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bots-50-town"] = 150,
        ["bots-100-spread"] = 150,
        ["bots-200-spread"] = 180,
        ["bots-500-siege"] = 200,
        ["bots-1000-town"] = 200
    };

    public const double FanoutQueueAgeP99GateMs = 40;

    public static IReadOnlyList<GateVerdict> Evaluate(RunReport report)
    {
        var verdicts = new List<GateVerdict>();
        string scenario = report.ScenarioName;

        double tickP99 = report.Server?.TickP99Ms ?? 0;
        verdicts.Add(new GateVerdict
        {
            Gate = "tick p99 <= 200 ms",
            Limit = TickP99GateMs,
            Observed = tickP99,
            Unit = "ms",
            Passed = tickP99 <= TickP99GateMs || tickP99 <= 0,
            Note = tickP99 <= 0 ? "no scrape data" : null
        });

        double cmdMailboxes = report.CommandToMailboxesP99Ms ?? report.Consistency?.BroadcastLatencyP99Ms ?? 0;
        if (CommandToMailboxesP99ByScenario.TryGetValue(scenario, out double cmdLimit))
        {
            verdicts.Add(new GateVerdict
            {
                Gate = $"command→mailboxes p99 <= {cmdLimit:0} ms ({scenario})",
                Limit = cmdLimit,
                Observed = cmdMailboxes,
                Unit = "ms",
                Passed = cmdMailboxes <= cmdLimit || cmdMailboxes <= 0,
                Note = cmdMailboxes <= 0 ? "metric missing or no bot oracle samples" : null
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
                Note = $"scenario `{scenario}` has no mapped gate; see #867 for bots-50-town … bots-1000-town"
            });
        }

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