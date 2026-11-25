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