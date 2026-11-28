namespace Fineage.Agent.Operations;

/// <summary>
/// Process-local durable store. A restarted Runtime constructs a new registry over the
/// same instance (or a clone) so in-flight rows survive the host restart in tests and
/// single-process deployments.
/// </summary>
public sealed class MemoryAgentOperationStore : IAgentOperationStore
{
    readonly object _gate = new();
    readonly Dictionary<string, AgentOperationRecord> _byId = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _byIdempotency = new(StringComparer.Ordinal);

    public MemoryAgentOperationStore()
    {
    }

    MemoryAgentOperationStore(
        Dictionary<string, AgentOperationRecord> byId,
        Dictionary<string, string> byIdempotency)
    {
        foreach (var pair in byId)
            _byId[pair.Key] = pair.Value;
        foreach (var pair in byIdempotency)
            _byIdempotency[pair.Key] = pair.Value;
    }

    public bool TryInsert(AgentOperationRecord record, out AgentOperationRecord stored)
    {
        ArgumentNullException.ThrowIfNull(record);
        string idempotency = IdempotencyKeyOf(record);
        lock (_gate)
        {
            if (_byIdempotency.TryGetValue(idempotency, out string? existingId) &&
                _byId.TryGetValue(existingId, out var existing))
            {
                stored = existing;
                return false;
            }

            _byId[record.OperationId.Value] = record;
            _byIdempotency[idempotency] = record.OperationId.Value;
            stored = record;
            return true;
        }
    }

    public AgentOperationRecord? FindById(OperationId operationId)
    {
        lock (_gate)
            return _byId.GetValueOrDefault(operationId.Value);
    }

    public AgentOperationRecord? FindByIdempotency(TenantId tenantId, PrincipalId principalId, IdempotencyKey key)
    {
        string idempotency = ComposeIdempotency(tenantId, principalId, key);
        lock (_gate)
        {
            return _byIdempotency.TryGetValue(idempotency, out string? id)
                ? _byId.GetValueOrDefault(id)
                : null;
        }
    }

    public bool TryUpdate(AgentOperationRecord expected, AgentOperationRecord next)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(next);
        if (expected.OperationId.Value != next.OperationId.Value)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Update must keep the same operation id.");

        lock (_gate)
        {
            if (!_byId.TryGetValue(expected.OperationId.Value, out var current))
                return false;
            if (current.Status != expected.Status || current.UpdatedAt != expected.UpdatedAt)
                return false;
            _byId[next.OperationId.Value] = next;
            return true;
        }
    }

    public IReadOnlyList<AgentOperationRecord> ListOpen()
    {
        lock (_gate)
        {
            return _byId.Values
                .Where(row => !row.IsTerminal)
                .OrderBy(row => row.CreatedAt)
                .ToArray();
        }
    }

    /// <summary>Independent copy of the current rows, used to simulate a fresh Runtime over durable state.</summary>
    public MemoryAgentOperationStore Clone()
    {
        lock (_gate)
            return new MemoryAgentOperationStore(_byId, _byIdempotency);
    }

    static string IdempotencyKeyOf(AgentOperationRecord record) =>
        ComposeIdempotency(record.TenantId, record.PrincipalId, record.IdempotencyKey);

    static string ComposeIdempotency(TenantId tenantId, PrincipalId principalId, IdempotencyKey key) =>
        tenantId.Value + "\n" + principalId.Value + "\n" + key.Value;
}
