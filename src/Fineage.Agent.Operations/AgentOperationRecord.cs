namespace Fineage.Agent.Operations;

/// <summary>Durable registry row. The public contract is <see cref="AgentOperation"/>.</summary>
public sealed record AgentOperationRecord(
    OperationId OperationId,
    AgentActionRequest Request,
    string PayloadFingerprint,
    OperationStatus Status,
    string? RefusalCode,
    string? Detail,
    string? LedgerCommitId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt)
{
    public TenantId TenantId => Request.Principal.TenantId;
    public PrincipalId PrincipalId => Request.Principal.PrincipalId;
    public AgentId AgentId => Request.Principal.AgentId;
    public CharacterId? CharacterId => Request.Principal.CharacterId;
    public IdempotencyKey IdempotencyKey => Request.IdempotencyKey;
    public DateTimeOffset Deadline => Request.Deadline;

    public bool IsTerminal => IsTerminalStatus(Status);

    public static bool IsTerminalStatus(OperationStatus status) =>
        status is OperationStatus.Succeeded
            or OperationStatus.Refused
            or OperationStatus.Failed
            or OperationStatus.Expired
            or OperationStatus.Cancelled;

    public AgentOperation ToContract() =>
        AgentOperation.Create(
            OperationId,
            Request.CommandId,
            Request.CorrelationId,
            Request.IdempotencyKey,
            Request.ToolName,
            Status,
            CreatedAt,
            CompletedAt,
            RefusalCode,
            Detail);

    public AgentActionReceipt ToReceipt() =>
        Status is OperationStatus.Accepted or OperationStatus.Running or OperationStatus.Succeeded or OperationStatus.Unknown
            ? AgentActionReceipt.Accepted(Request.CommandId, Request.CorrelationId, OperationId)
            : AgentActionReceipt.Refused(
                Request.CommandId,
                Request.CorrelationId,
                OperationId,
                RefusalCode ?? AgentRefusalCodes.InvalidContract,
                Detail);

    public bool IsOwnedBy(AgentPrincipal principal)
    {
        if (principal is null)
            return false;
        if (TenantId.Value != principal.TenantId.Value)
            return false;
        if (PrincipalId.Value != principal.PrincipalId.Value)
            return false;
        if (AgentId.Value != principal.AgentId.Value)
            return false;
        string? bound = CharacterId?.Value;
        string? claimed = principal.CharacterId?.Value;
        return string.Equals(bound, claimed, StringComparison.Ordinal);
    }
}
