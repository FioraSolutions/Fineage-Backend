namespace Fineage.Agent.Operations;

/// <summary>
/// Durable storage for the operation registry. Implementations must honor the unique
/// (tenant, principal, idempotency key) constraint atomically.
/// </summary>
public interface IAgentOperationStore
{
    /// <summary>
    /// Attempts to insert <paramref name="record"/>. Returns <c>true</c> when the row is new.
    /// When the idempotency key is already taken, returns <c>false</c> and the existing row.
    /// </summary>
    bool TryInsert(AgentOperationRecord record, out AgentOperationRecord stored);

    AgentOperationRecord? FindById(OperationId operationId);

    AgentOperationRecord? FindByIdempotency(TenantId tenantId, PrincipalId principalId, IdempotencyKey key);

    /// <summary>
    /// Compare-and-swap update. Succeeds only when the stored row still matches
    /// <paramref name="expected"/> (same operation id, status, and updated-at).
    /// </summary>
    bool TryUpdate(AgentOperationRecord expected, AgentOperationRecord next);

    /// <summary>Non-terminal rows, including expired accepted work that still needs a close.</summary>
    IReadOnlyList<AgentOperationRecord> ListOpen();
}
