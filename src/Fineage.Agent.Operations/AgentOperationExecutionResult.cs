namespace Fineage.Agent.Operations;

/// <summary>Outcome of the one apply attempt the registry will allow for an accepted operation.</summary>
public sealed class AgentOperationExecutionResult
{
    public const int MaxDetailLength = 512;

    AgentOperationExecutionResult(
        OperationStatus status,
        string? refusalCode,
        string? detail,
        string? ledgerCommitId)
    {
        if (status is not (OperationStatus.Succeeded or OperationStatus.Refused or OperationStatus.Failed))
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                "An execution result must be Succeeded, Refused, or Failed.");
        }

        if (status is OperationStatus.Refused or OperationStatus.Failed && string.IsNullOrWhiteSpace(refusalCode))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{status} requires a refusal code.");
        if (status is OperationStatus.Succeeded && !string.IsNullOrWhiteSpace(refusalCode))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, $"{status} must not carry a refusal code.");
        Status = status;
        RefusalCode = refusalCode;
        Detail = TruncateDetail(detail);
        LedgerCommitId = string.IsNullOrWhiteSpace(ledgerCommitId) ? null : ledgerCommitId.Trim();
    }

    public OperationStatus Status { get; }
    public string? RefusalCode { get; }
    public string? Detail { get; }

    /// <summary>
    /// Durable ledger transaction id (tick.sequence) when the tool mutated economy state.
    /// Recovery reuses this id and must not apply the move again.
    /// </summary>
    public string? LedgerCommitId { get; }

    public static AgentOperationExecutionResult Succeeded(string? ledgerCommitId = null, string? detail = null) =>
        new(OperationStatus.Succeeded, null, detail, ledgerCommitId);

    public static AgentOperationExecutionResult Refused(string refusalCode, string? detail = null, string? ledgerCommitId = null) =>
        new(OperationStatus.Refused, refusalCode, detail, ledgerCommitId);

    public static AgentOperationExecutionResult Failed(string refusalCode, string? detail = null, string? ledgerCommitId = null) =>
        new(OperationStatus.Failed, refusalCode, detail, ledgerCommitId);

    public static string? TruncateDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return null;
        string trimmed = detail.Trim();
        return trimmed.Length <= MaxDetailLength ? trimmed : trimmed[..MaxDetailLength];
    }
}
