namespace Fineage.LoadTest.Consistency;

public enum EconomyConsistencyKind
{
    LedgerInvariantViolation,
    EconomyCommitFailed,
    LedgerRejected,
    UnexpectedReject,
    EconomyFlowFailed
}

public sealed class EconomyConsistencyIssue
{
    public EconomyConsistencyKind Kind { get; init; }
    public int? BotId { get; init; }
    public int? ObjectId { get; init; }
    public required string Detail { get; init; }
}

public sealed class EconomyConsistencyReport
{
    public List<EconomyConsistencyIssue> Issues { get; init; } = [];
    public long LedgerInvariantViolations { get; init; }
    public long LedgerFlushFailures { get; init; }
    public long ExplainedEconomyRejects { get; set; }
    /// <summary>Reject codes outside both the explained and the expected-domain sets: harness or server bugs.</summary>
    public long UnexplainedEconomyRejects { get; set; }
    /// <summary>Contention refusals (<see cref="EconomyConsistencyOracle.ExpectedDomainRejectCodes"/>); counted, not failed.</summary>
    public long ExpectedDomainRejects { get; set; }
    public SortedDictionary<string, long> ExpectedDomainRejectsByCode { get; init; } = new(StringComparer.Ordinal);
    public long EconomyFlowsAttempted { get; set; }
    public long EconomyFlowsSucceeded { get; set; }
}