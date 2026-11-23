using System.Globalization;
using System.Text.RegularExpressions;
using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Models;

namespace Fineage.LoadTest.Consistency;

/// <summary>Post-run economy gates: auditable ledger invariants, reject reasons, zone log patterns.</summary>
public static class EconomyConsistencyOracle
{
    static readonly HashSet<string> ExplainedRejectCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "world", "range", "bad_request", "closed", "adena", "weight", "slots", "ledger",
        "not_owned", "dead", "busy", "period", "rights", "maintenance", "distance"
    };

    /// <summary>
    /// Normal refusals when many players contend for the same people and items: the target is already in a
    /// party or a trade, is out of trade range, the drop was picked up first, sit/social while fighting, the
    /// target died between pick and hit, a private store closed, sold out, moved away or is busy with another buyer,
    /// a cast raced its own reuse or a target that just died, the castle manor offers no seed this period. Counted per code, never a failure. A code outside both sets
    /// (for example <c>skill_unknown</c> or <c>unknown_opcode</c>) is a harness or server bug and fails the run.
    /// </summary>
    public static readonly IReadOnlySet<string> ExpectedDomainRejectCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "party_busy", "party_leader", "trade_busy", "trade_range", "pickup", "action_blocked", "target", "attack",
        "store_busy", "store_range", "store_closed", "store_item", "store_adena", "store_combat",
        "skill_reuse", "skill_target", "manor_product"
    };

    public enum RejectClass
    {
        Explained,
        ExpectedDomain,
        Unexpected
    }

    public static RejectClass Classify(string code) =>
        ExplainedRejectCodes.Contains(code) ? RejectClass.Explained
        : ExpectedDomainRejectCodes.Contains(code) ? RejectClass.ExpectedDomain
        : RejectClass.Unexpected;

    static readonly Regex LedgerRejectRx = new(
        @"ledger rejected|economy_commit_failed|auditable economy ledger invariant",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static EconomyConsistencyReport Analyze(
        IReadOnlyList<FineageBotClient> bots,
        LoadScenario scenario,
        IReadOnlyList<PrometheusSample> metrics,
        IEnumerable<string>? zoneLogPaths)
    {
        var report = new EconomyConsistencyReport
        {
            LedgerInvariantViolations = metrics.Count > 0 ? metrics.Max(s => s.LedgerInvariantViolations) : 0,
            LedgerFlushFailures = metrics.Count > 0 ? metrics.Max(s => s.LedgerFlushFailures) : 0
        };

        foreach (var bot in bots)
        {
            report.EconomyFlowsAttempted += bot.Stats.EconomyFlowsAttempted;
            report.EconomyFlowsSucceeded += bot.Stats.EconomyFlowsSucceeded;
            foreach (var (code, count) in bot.Stats.EconomyRejectCounts)
            {
                var kind = Classify(code);
                if (kind == RejectClass.Explained)
                    report.ExplainedEconomyRejects += count;
                else if (kind == RejectClass.ExpectedDomain)
                {
                    report.ExpectedDomainRejects += count;
                    report.ExpectedDomainRejectsByCode[code] =
                        report.ExpectedDomainRejectsByCode.GetValueOrDefault(code) + count;
                }
                else
                {
                    report.Issues.Add(new EconomyConsistencyIssue
                    {
                        Kind = EconomyConsistencyKind.UnexpectedReject,
                        BotId = bot.BotId,
                        ObjectId = bot.SelfObjectId,
                        Detail = $"reject code '{code}' ×{count}"
                    });
                }
            }

            foreach (string detail in bot.Stats.EconomyFlowFailures)
            {
                report.Issues.Add(new EconomyConsistencyIssue
                {
                    Kind = EconomyConsistencyKind.EconomyFlowFailed,
                    BotId = bot.BotId,
                    ObjectId = bot.SelfObjectId,
                    Detail = detail
                });
            }
        }

        if (scenario.Economy is { RequireLedgerInvariant: true })
        {
            if (report.LedgerInvariantViolations > 0)
            {
                report.Issues.Add(new EconomyConsistencyIssue
                {
                    Kind = EconomyConsistencyKind.LedgerInvariantViolation,
                    Detail = $"fineage_ledger_invariant_violations_total={report.LedgerInvariantViolations}"
                });
            }

            if (report.LedgerFlushFailures > 0)
            {
                report.Issues.Add(new EconomyConsistencyIssue
                {
                    Kind = EconomyConsistencyKind.EconomyCommitFailed,
                    Detail = $"fineage_ledger_flush_failures_total={report.LedgerFlushFailures}"
                });
            }
        }

        if (zoneLogPaths != null)
        {
            foreach (string path in zoneLogPaths)
            {
                if (!File.Exists(path))
                    continue;
                foreach (string line in File.ReadLines(path))
                {
                    if (!LedgerRejectRx.IsMatch(line))
                        continue;
                    report.Issues.Add(new EconomyConsistencyIssue
                    {
                        Kind = line.Contains("invariant", StringComparison.OrdinalIgnoreCase)
                            ? EconomyConsistencyKind.LedgerInvariantViolation
                            : EconomyConsistencyKind.LedgerRejected,
                        Detail = line.Length > 240 ? line[..240] : line
                    });
                }
            }
        }

        return report;
    }

    public static bool IsHardFailure(EconomyConsistencyKind kind) => kind is
        EconomyConsistencyKind.LedgerInvariantViolation or
        EconomyConsistencyKind.EconomyCommitFailed or
        EconomyConsistencyKind.LedgerRejected or
        EconomyConsistencyKind.UnexpectedReject;
}