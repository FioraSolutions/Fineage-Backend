using System.Collections.Concurrent;

namespace Fineage.Agent.Operations;

/// <summary>
/// Durable operation registry for the Agent Runtime host. Admission returns immediately;
/// execution is a separate step so a restart can recover accepted work without holding
/// an inbound request open. Idempotency is per principal.
/// </summary>
public sealed class AgentOperationRegistry
{
    readonly IAgentOperationStore _store;
    readonly TimeProvider _time;
    readonly ConcurrentDictionary<string, Task<AgentOperation>> _inflight = new(StringComparer.Ordinal);

    public AgentOperationRegistry(IAgentOperationStore store, TimeProvider? time = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _time = time ?? TimeProvider.System;
    }

    public AgentActionReceipt Accept(AgentActionRequest request, SnapshotVersion? currentSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        DateTimeOffset now = _time.GetUtcNow();
        string fingerprint = PayloadFingerprint.Compute(request);
        OperationId operationId = new("op-" + Guid.NewGuid().ToString("N"));

        if (request.ExpectedSnapshotVersion is { } expected)
        {
            if (currentSnapshot is not { } current)
            {
                return InsertOrReuse(
                    request,
                    fingerprint,
                    operationId,
                    now,
                    OperationStatus.Refused,
                    AgentRefusalCodes.StaleSnapshot,
                    "ExpectedSnapshotVersion requires the current snapshot.");
            }

            if (current.Value > expected.Value)
            {
                return InsertOrReuse(
                    request,
                    fingerprint,
                    operationId,
                    now,
                    OperationStatus.Refused,
                    AgentRefusalCodes.StaleSnapshot,
                    $"Snapshot {current.Value} is later than expected {expected.Value}.");
            }
        }

        if (request.Deadline <= now)
        {
            return InsertOrReuse(
                request,
                fingerprint,
                operationId,
                now,
                OperationStatus.Expired,
                AgentRefusalCodes.Expired,
                "The operation deadline has passed.");
        }

        var record = NewRecord(request, fingerprint, operationId, now, OperationStatus.Accepted, null, null, completed: false);
        return InsertOrReuse(record);
    }

    public AgentActionReceipt Submit(
        AgentActionRequest request,
        Func<AgentActionRequest, CancellationToken, AgentOperationExecutionResult> execute,
        SnapshotVersion? currentSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execute);
        AgentActionReceipt receipt = Accept(request, currentSnapshot);
        if (receipt.Status != OperationStatus.Accepted)
            return receipt;

        _ = Execute(receipt.OperationId, execute, cancellationToken);
        return receipt;
    }

    public async Task<AgentActionReceipt> SubmitAsync(
        AgentActionRequest request,
        Func<AgentActionRequest, CancellationToken, Task<AgentOperationExecutionResult>> execute,
        SnapshotVersion? currentSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execute);
        AgentActionReceipt receipt = Accept(request, currentSnapshot);
        if (receipt.Status != OperationStatus.Accepted)
            return receipt;

        await ExecuteAsync(receipt.OperationId, execute, cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    public AgentOperation Execute(
        OperationId operationId,
        Func<AgentActionRequest, CancellationToken, AgentOperationExecutionResult> execute,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return ExecuteAsync(
            operationId,
            (request, token) => Task.FromResult(execute(request, token)),
            cancellationToken).GetAwaiter().GetResult();
    }

    public Task<AgentOperation> ExecuteAsync(
        OperationId operationId,
        Func<AgentActionRequest, CancellationToken, Task<AgentOperationExecutionResult>> execute,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execute);
        if (_inflight.TryGetValue(operationId.Value, out Task<AgentOperation>? existing))
            return existing;

        var completion = new TaskCompletionSource<AgentOperation>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inflight.TryAdd(operationId.Value, completion.Task))
            return _inflight[operationId.Value];

        return CompleteAsync();

        async Task<AgentOperation> CompleteAsync()
        {
            try
            {
                AgentOperation result = await RunAsync(operationId, execute, cancellationToken).ConfigureAwait(false);
                completion.TrySetResult(result);
                return result;
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
                throw;
            }
        }
    }

    public AgentOperation? Get(AgentPrincipal principal, OperationId operationId)
    {
        ArgumentNullException.ThrowIfNull(principal);
        AgentOperationRecord? record = _store.FindById(operationId);
        if (record is null)
            return null;
        return record.IsOwnedBy(principal)
            ? record.ToContract()
            : throw new AgentContractException(OwnershipCode(record, principal), "Operation status is only available to the owner.");
    }

    public AgentOperationCancelResult Cancel(AgentPrincipal principal, OperationId operationId, bool allowCancel = false)
    {
        ArgumentNullException.ThrowIfNull(principal);
        AgentOperationRecord record = RequireOwned(principal, operationId);
        if (!allowCancel)
        {
            return AgentOperationCancelResult.Denied(
                record.ToContract(),
                AgentRefusalCodes.CancelNotAllowed,
                "Cancel of the request does not cancel an accepted operation.");
        }

        if (record.Status != OperationStatus.Accepted)
        {
            return AgentOperationCancelResult.Denied(
                record.ToContract(),
                AgentRefusalCodes.CancelNotAllowed,
                "The game action is already in flight or finished and was not cancelled.");
        }

        DateTimeOffset now = _time.GetUtcNow();
        var cancelled = record with
        {
            Status = OperationStatus.Cancelled,
            RefusalCode = AgentRefusalCodes.Cancelled,
            Detail = "The operation was cancelled before execution.",
            UpdatedAt = now,
            CompletedAt = now,
        };
        if (!_store.TryUpdate(record, cancelled))
        {
            AgentOperationRecord latest = _store.FindById(operationId) ?? record;
            if (!latest.IsOwnedBy(principal))
                throw new AgentContractException(OwnershipCode(latest, principal), "Operation status is only available to the owner.");
            return AgentOperationCancelResult.Denied(
                latest.ToContract(),
                AgentRefusalCodes.CancelNotAllowed,
                "The game action is already in flight or finished and was not cancelled.");
        }

        AgentOperationTelemetry.NoteClosed(cancelled.Request.ToolName, DurationMs(cancelled.CreatedAt, now));
        return AgentOperationCancelResult.Allowed(cancelled.ToContract());
    }

    /// <summary>
    /// Reconcile open rows after a Runtime or Zone restart. Expired deadlines are closed.
    /// A running operation that already recorded a ledger commit is completed without a
    /// second apply. A running operation with no commit is marked Unknown, never silently
    /// treated as succeeded. Accepted work that is still inside its deadline stays accepted
    /// so the host can execute it once.
    /// </summary>
    public IReadOnlyList<AgentOperation> Recover()
    {
        DateTimeOffset now = _time.GetUtcNow();
        var recovered = new List<AgentOperation>();
        foreach (AgentOperationRecord open in _store.ListOpen())
        {
            AgentOperationRecord? next = RecoverOne(open, now);
            if (next is not null)
                recovered.Add(next.ToContract());
        }

        return recovered;
    }

    public AgentActionReceipt ReceiptFor(OperationId operationId)
    {
        AgentOperationRecord record = _store.FindById(operationId)
            ?? throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Operation was not found.");
        return record.ToReceipt();
    }

    async Task<AgentOperation> RunAsync(
        OperationId operationId,
        Func<AgentActionRequest, CancellationToken, Task<AgentOperationExecutionResult>> execute,
        CancellationToken cancellationToken)
    {
        try
        {
            AgentOperationRecord? record = _store.FindById(operationId);
            if (record is null)
            {
                throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Operation was not found.");
            }

            DateTimeOffset now = _time.GetUtcNow();
            if (record.IsTerminal || record.Status == OperationStatus.Unknown)
                return record.ToContract();

            if (record.Deadline <= now)
            {
                AgentOperationRecord? expired = Transition(
                    record,
                    record with
                    {
                        Status = OperationStatus.Expired,
                        RefusalCode = AgentRefusalCodes.Expired,
                        Detail = "The operation deadline has passed.",
                        UpdatedAt = now,
                        CompletedAt = now,
                    },
                    closeMetrics: record.Status is OperationStatus.Accepted or OperationStatus.Running);
                return (expired ?? _store.FindById(operationId) ?? record).ToContract();
            }

            if (record.Status == OperationStatus.Accepted)
            {
                var running = record with
                {
                    Status = OperationStatus.Running,
                    UpdatedAt = now,
                };
                if (!_store.TryUpdate(record, running))
                {
                    AgentOperationRecord latest = _store.FindById(operationId) ?? record;
                    if (latest.IsTerminal || latest.Status == OperationStatus.Unknown)
                        return latest.ToContract();
                    record = latest;
                }
                else
                {
                    record = running;
                }
            }

            if (record.Status != OperationStatus.Running)
                return record.ToContract();

            AgentOperationExecutionResult result;
            try
            {
                result = await execute(record.Request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not AgentContractException and not OperationCanceledException)
            {
                DateTimeOffset failedAt = _time.GetUtcNow();
                var failed = record with
                {
                    Status = OperationStatus.Failed,
                    RefusalCode = AgentRefusalCodes.InvalidContract,
                    Detail = AgentOperationExecutionResult.TruncateDetail(ex.Message),
                    UpdatedAt = failedAt,
                    CompletedAt = failedAt,
                };
                AgentOperationRecord? storedFailed = Transition(record, failed, closeMetrics: true);
                return (storedFailed ?? _store.FindById(operationId) ?? failed).ToContract();
            }

            DateTimeOffset completedAt = _time.GetUtcNow();
            var completed = record with
            {
                Status = result.Status,
                RefusalCode = result.RefusalCode,
                Detail = result.Detail,
                LedgerCommitId = result.LedgerCommitId ?? record.LedgerCommitId,
                UpdatedAt = completedAt,
                CompletedAt = completedAt,
            };
            AgentOperationRecord? stored = Transition(record, completed, closeMetrics: true);
            return (stored ?? _store.FindById(operationId) ?? completed).ToContract();
        }
        finally
        {
            _inflight.TryRemove(operationId.Value, out _);
        }
    }

    AgentOperationRecord? RecoverOne(AgentOperationRecord open, DateTimeOffset now)
    {
        if (open.IsTerminal)
            return open;

        if (open.Deadline <= now && open.Status is OperationStatus.Accepted or OperationStatus.Running)
        {
            var expired = open with
            {
                Status = OperationStatus.Expired,
                RefusalCode = AgentRefusalCodes.Expired,
                Detail = "The operation deadline has passed.",
                UpdatedAt = now,
                CompletedAt = now,
            };
            return Transition(open, expired, closeMetrics: true) ?? _store.FindById(open.OperationId);
        }

        if (open.Status == OperationStatus.Running && !string.IsNullOrWhiteSpace(open.LedgerCommitId))
        {
            var succeeded = open with
            {
                Status = OperationStatus.Succeeded,
                RefusalCode = null,
                Detail = "Reused the durable ledger commit after restart.",
                UpdatedAt = now,
                CompletedAt = now,
            };
            return Transition(open, succeeded, closeMetrics: true) ?? _store.FindById(open.OperationId);
        }

        if (open.Status == OperationStatus.Running)
        {
            var unknown = open with
            {
                Status = OperationStatus.Unknown,
                RefusalCode = null,
                Detail = "Execution was interrupted; the world outcome is unknown and will not be retried automatically.",
                UpdatedAt = now,
                CompletedAt = null,
            };
            return Transition(open, unknown, closeMetrics: true) ?? _store.FindById(open.OperationId);
        }

        return open;
    }

    AgentActionReceipt InsertOrReuse(
        AgentActionRequest request,
        string fingerprint,
        OperationId operationId,
        DateTimeOffset now,
        OperationStatus status,
        string refusalCode,
        string detail)
    {
        var record = NewRecord(request, fingerprint, operationId, now, status, refusalCode, detail, completed: true);
        return InsertOrReuse(record);
    }

    AgentActionReceipt InsertOrReuse(AgentOperationRecord record)
    {
        if (_store.TryInsert(record, out AgentOperationRecord stored))
        {
            if (!record.IsTerminal)
                AgentOperationTelemetry.NoteOpened();
            else
                AgentOperationTelemetry.Duration.Record(0, new KeyValuePair<string, object?>(AgentOperationTelemetry.OperationTag, record.Request.ToolName));
            return stored.ToReceipt();
        }

        if (!string.Equals(stored.PayloadFingerprint, record.PayloadFingerprint, StringComparison.Ordinal))
        {
            return AgentActionReceipt.Refused(
                record.Request.CommandId,
                record.Request.CorrelationId,
                stored.OperationId,
                AgentRefusalCodes.IdempotencyConflict,
                "The idempotency key was already used with a different payload.");
        }

        return stored.ToReceipt();
    }

    AgentOperationRecord? Transition(AgentOperationRecord expected, AgentOperationRecord next, bool closeMetrics)
    {
        if (!_store.TryUpdate(expected, next))
            return null;
        if (closeMetrics)
            AgentOperationTelemetry.NoteClosed(next.Request.ToolName, DurationMs(next.CreatedAt, next.UpdatedAt));
        return next;
    }

    AgentOperationRecord RequireOwned(AgentPrincipal principal, OperationId operationId)
    {
        AgentOperationRecord record = _store.FindById(operationId)
            ?? throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Operation was not found.");
        if (!record.IsOwnedBy(principal))
            throw new AgentContractException(OwnershipCode(record, principal), "Operation status is only available to the owner.");
        return record;
    }

    static string OwnershipCode(AgentOperationRecord record, AgentPrincipal principal) =>
        record.TenantId.Value != principal.TenantId.Value
            ? AgentRefusalCodes.CrossTenant
            : AgentRefusalCodes.AgentMismatch;

    static AgentOperationRecord NewRecord(
        AgentActionRequest request,
        string fingerprint,
        OperationId operationId,
        DateTimeOffset now,
        OperationStatus status,
        string? refusalCode,
        string? detail,
        bool completed) =>
        new(
            operationId,
            request,
            fingerprint,
            status,
            refusalCode,
            detail,
            LedgerCommitId: null,
            CreatedAt: now,
            UpdatedAt: now,
            CompletedAt: completed ? now : null);

    static double DurationMs(DateTimeOffset createdAt, DateTimeOffset completedAt)
    {
        double ms = (completedAt - createdAt).TotalMilliseconds;
        return ms < 0 ? 0 : ms;
    }
}
