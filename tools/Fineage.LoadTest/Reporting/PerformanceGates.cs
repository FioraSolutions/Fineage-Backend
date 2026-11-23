using Fineage.LoadTest.Metrics;

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
        ["bots-200-town"] = 180,
        ["bots-500-siege"] = 200,
        ["bots-1000-town"] = 200,
        // Heavy scenarios use the 200-town command budget. A new name must not pass for lack of a row.
        ["heavy-raid-kutus"] = 180,
        ["heavy-aoe-pack"] = 180,
        ["heavy-respawn-wave"] = 180
    };

    public const double FanoutQueueAgeP99GateMs = 40;

    /// <summary>request:enter on the tick is the insert. The old on-tick materialization was hundreds of ms.</summary>
    public const double EnterApplyP99GateMs = 20;

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
            Passed = tickP99 > 0 && tickP99 <= TickP99GateMs,
            Note = tickP99 <= 0 ? "MISSING: fineage_tick_ms (no scrape data is not a pass)" : null
        });

        var enter = string.IsNullOrEmpty(report.PeakMetricsText)
            ? null
            : PrometheusParser.ParseLabeledHistogram(
                report.PeakMetricsText, "fineage_command_apply_ms", "command", "request:enter");
        double enterP99 = enter?.P99 ?? 0;
        bool missingEnter = enter is not { Count: > 0 };
        verdicts.Add(new GateVerdict
        {
            Gate = "request:enter p99 <= 20 ms",
            Limit = EnterApplyP99GateMs,
            Observed = enterP99,
            Unit = "ms",
            Passed = !missingEnter && enterP99 <= EnterApplyP99GateMs,
            Note = missingEnter
                ? "MISSING: fineage_command_apply_ms{command=\"request:enter\"}"
                : "insert on the simulation thread; self frames are off the tick"
        });

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