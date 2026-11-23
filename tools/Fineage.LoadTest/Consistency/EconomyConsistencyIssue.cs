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
    public long UnexplainedEconomyRejects { get; set; }
    public long EconomyFlowsAttempted { get; set; }
    public long EconomyFlowsSucceeded { get; set; }
}