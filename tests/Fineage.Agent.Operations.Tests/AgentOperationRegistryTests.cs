using Fineage.Agent.Contracts;
using Fineage.Agent.Operations;
using Xunit;

namespace Fineage.Agent.Operations.Tests;

public sealed class AgentOperationRegistryTests : IDisposable
{
    public AgentOperationRegistryTests() => AgentOperationTelemetry.ResetForTests();

    public void Dispose() => AgentOperationTelemetry.ResetForTests();

    [Fact]
    public void Duplicate_submit_of_the_same_key_and_payload_is_one_effect_and_the_same_receipt()
    {
        var clock = OperationHarness.Clock();
        AgentOperationRegistry registry = OperationHarness.Registry(out _, clock);
        var apply = new OperationHarness.ApplyCounter();
        AgentActionRequest request = OperationHarness.Request();

        AgentActionReceipt first = registry.Accept(request);
        AgentOperation done = registry.Execute(first.OperationId, apply.Apply);
        AgentActionReceipt second = registry.Accept(
            OperationHarness.Request(command: "cmd-2", correlation: "corr-2"));

        Assert.Equal(OperationStatus.Accepted, first.Status);
        Assert.Equal(first.OperationId, second.OperationId);
        Assert.Equal(first.CommandId, second.CommandId);
        Assert.Equal(first.CorrelationId, second.CorrelationId);
        Assert.Equal(OperationStatus.Accepted, second.Status);
        Assert.Equal(1, apply.Count);
        Assert.Equal(OperationStatus.Succeeded, done.Status);
        Assert.Equal(done.Status, registry.Get(request.Principal, first.OperationId)!.Status);
    }

    [Fact]
    public void Same_key_with_a_different_payload_is_an_explicit_conflict()
    {
        AgentOperationRegistry registry = OperationHarness.Registry(out _);
        var apply = new OperationHarness.ApplyCounter();
        AgentActionReceipt first = registry.Accept(OperationHarness.Request(json: """{"objectId":7,"count":1}"""));
        registry.Execute(first.OperationId, apply.Apply);

        AgentActionReceipt conflict = registry.Accept(OperationHarness.Request(json: """{"objectId":7,"count":2}"""));

        Assert.Equal(OperationStatus.Refused, conflict.Status);
        Assert.Equal(AgentRefusalCodes.IdempotencyConflict, conflict.RefusalCode);
        Assert.Equal(first.OperationId, conflict.OperationId);
        Assert.Equal(1, apply.Count);
    }

    [Fact]
    public void Cancel_of_the_request_does_not_cancel_an_accepted_operation_by_default()
    {
        AgentOperationRegistry registry = OperationHarness.Registry(out _);
        AgentActionRequest request = OperationHarness.Request();
        AgentActionReceipt receipt = registry.Accept(request);

        AgentOperationCancelResult denied = registry.Cancel(request.Principal, receipt.OperationId);
        AgentOperation? still = registry.Get(request.Principal, receipt.OperationId);

        Assert.False(denied.Cancelled);
        Assert.Equal(AgentRefusalCodes.CancelNotAllowed, denied.RefusalCode);
        Assert.Equal(OperationStatus.Accepted, still!.Status);

        var apply = new OperationHarness.ApplyCounter();
        AgentOperation executed = registry.Execute(receipt.OperationId, apply.Apply);
        Assert.Equal(OperationStatus.Succeeded, executed.Status);
        Assert.Equal(1, apply.Count);
    }

    [Fact]
    public void Explicit_cancel_is_allowed_only_before_execution_starts()
    {
        AgentOperationRegistry registry = OperationHarness.Registry(out _);
        AgentActionRequest request = OperationHarness.Request();
        AgentActionReceipt receipt = registry.Accept(request);

        AgentOperationCancelResult cancelled = registry.Cancel(request.Principal, receipt.OperationId, allowCancel: true);
        Assert.True(cancelled.Cancelled);
        Assert.Equal(OperationStatus.Cancelled, cancelled.Operation.Status);
        Assert.Equal(AgentRefusalCodes.Cancelled, cancelled.Operation.RefusalCode);

        var apply = new OperationHarness.ApplyCounter();
        AgentOperation after = registry.Execute(receipt.OperationId, apply.Apply);
        Assert.Equal(OperationStatus.Cancelled, after.Status);
        Assert.Equal(0, apply.Count);

        AgentActionReceipt again = registry.Accept(request);
        AgentOperationCancelResult tooLate = registry.Cancel(request.Principal, again.OperationId, allowCancel: true);
        Assert.False(tooLate.Cancelled);
        Assert.Equal(OperationStatus.Cancelled, tooLate.Operation.Status);
    }

    [Fact]
    public void Kill_between_accept_and_execute_recovers_the_operation_and_applies_once()
    {
        var clock = OperationHarness.Clock();
        AgentOperationRegistry original = OperationHarness.Registry(out MemoryAgentOperationStore store, clock);
        AgentActionRequest request = OperationHarness.Request();
        AgentActionReceipt receipt = original.Accept(request);
        Assert.Equal(OperationStatus.Accepted, receipt.Status);

        var recovered = new AgentOperationRegistry(store.Clone(), clock);
        IReadOnlyList<AgentOperation> open = recovered.Recover();
        Assert.Contains(open, op => op.OperationId == receipt.OperationId && op.Status == OperationStatus.Accepted);
        Assert.Equal(OperationStatus.Accepted, recovered.Get(request.Principal, receipt.OperationId)!.Status);

        var apply = new OperationHarness.ApplyCounter();
        AgentOperation done = recovered.Execute(receipt.OperationId, apply.Apply);
        Assert.Equal(OperationStatus.Succeeded, done.Status);
        Assert.Equal(1, apply.Count);
        Assert.Equal(receipt, recovered.ReceiptFor(receipt.OperationId));
    }

    [Fact]
    public void Kill_while_running_with_a_ledger_commit_reuses_it_and_does_not_apply_again()
    {
        var clock = OperationHarness.Clock();
        AgentOperationRegistry original = OperationHarness.Registry(out MemoryAgentOperationStore store, clock);
        AgentActionRequest request = OperationHarness.Request();
        AgentActionReceipt receipt = original.Accept(request);
        DateTimeOffset now = clock.GetUtcNow();
        AgentOperationRecord accepted = store.FindById(receipt.OperationId)!;
        Assert.True(store.TryUpdate(
            accepted,
            accepted with
            {
                Status = OperationStatus.Running,
                LedgerCommitId = "42.3",
                UpdatedAt = now,
            }));

        var recovered = new AgentOperationRegistry(store, clock);
        IReadOnlyList<AgentOperation> closed = recovered.Recover();
        AgentOperation? operation = recovered.Get(request.Principal, receipt.OperationId);

        Assert.Contains(closed, op => op.OperationId == receipt.OperationId && op.Status == OperationStatus.Succeeded);
        Assert.Equal(OperationStatus.Succeeded, operation!.Status);
        Assert.Contains("ledger", operation.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("42.3", store.FindById(receipt.OperationId)!.LedgerCommitId);

        var apply = new OperationHarness.ApplyCounter();
        AgentOperation again = recovered.Execute(receipt.OperationId, apply.Apply);
        Assert.Equal(OperationStatus.Succeeded, again.Status);
        Assert.Equal(0, apply.Count);
        Assert.Equal(receipt.OperationId, recovered.Accept(request).OperationId);
    }

    [Fact]
    public void Kill_while_running_without_a_commit_is_unknown_not_silently_succeeded()
    {
        var clock = OperationHarness.Clock();
        AgentOperationRegistry original = OperationHarness.Registry(out MemoryAgentOperationStore store, clock);
        AgentActionRequest request = OperationHarness.Request();
        AgentActionReceipt receipt = original.Accept(request);
        AgentOperationRecord accepted = store.FindById(receipt.OperationId)!;
        Assert.True(store.TryUpdate(
            accepted,
            accepted with { Status = OperationStatus.Running, UpdatedAt = clock.GetUtcNow() }));

        var recovered = new AgentOperationRegistry(store.Clone(), clock);
        recovered.Recover();
        AgentOperation? operation = recovered.Get(request.Principal, receipt.OperationId);

        Assert.Equal(OperationStatus.Unknown, operation!.Status);
        Assert.Null(operation.CompletedAt);
        var apply = new OperationHarness.ApplyCounter();
        AgentOperation again = recovered.Execute(receipt.OperationId, apply.Apply);
        Assert.Equal(OperationStatus.Unknown, again.Status);
        Assert.Equal(0, apply.Count);
    }

    [Fact]
    public void Expired_deadline_is_refused_and_is_not_executed()
    {
        var clock = OperationHarness.Clock();
        AgentOperationRegistry registry = new(new MemoryAgentOperationStore(), clock);
        var apply = new OperationHarness.ApplyCounter();
        AgentActionRequest request = OperationHarness.Request(deadline: OperationHarness.Start.AddMinutes(-1));

        AgentActionReceipt receipt = registry.Accept(request);
        Assert.Equal(OperationStatus.Refused, receipt.Status);
        Assert.Equal(AgentRefusalCodes.Expired, receipt.RefusalCode);
        AgentOperation? operation = registry.Get(request.Principal, receipt.OperationId);
        Assert.Equal(OperationStatus.Expired, operation!.Status);

        AgentOperation executed = registry.Execute(receipt.OperationId, apply.Apply);
        Assert.Equal(OperationStatus.Expired, executed.Status);
        Assert.Equal(0, apply.Count);

        AgentActionReceipt same = registry.Accept(request);
        Assert.Equal(receipt.OperationId, same.OperationId);
        Assert.Equal(AgentRefusalCodes.Expired, same.RefusalCode);
    }

    [Fact]
    public void Accepted_work_expires_on_recover_after_the_deadline()
    {
        var clock = OperationHarness.Clock();
        AgentOperationRegistry original = OperationHarness.Registry(out MemoryAgentOperationStore store, clock);
        AgentActionRequest request = OperationHarness.Request();
        AgentActionReceipt receipt = original.Accept(request);

        clock.Advance(TimeSpan.FromMinutes(11));
        var recovered = new AgentOperationRegistry(store.Clone(), clock);
        recovered.Recover();

        AgentOperation? operation = recovered.Get(request.Principal, receipt.OperationId);
        Assert.Equal(OperationStatus.Expired, operation!.Status);
        Assert.Equal(AgentRefusalCodes.Expired, operation.RefusalCode);
        var apply = new OperationHarness.ApplyCounter();
        Assert.Equal(0, recovered.Execute(receipt.OperationId, apply.Apply) is { Status: OperationStatus.Expired } ? apply.Count : -1);
    }

    [Fact]
    public async Task Concurrent_same_key_submissions_apply_once_and_share_the_receipt()
    {
        AgentOperationRegistry registry = OperationHarness.Registry(out _);
        var apply = new OperationHarness.ApplyCounter();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<AgentOperationExecutionResult> SlowApply(AgentActionRequest request, CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return apply.Apply(request, cancellationToken);
        }

        AgentActionRequest first = OperationHarness.Request(command: "cmd-a", correlation: "corr-a");
        AgentActionRequest second = OperationHarness.Request(command: "cmd-b", correlation: "corr-b");
        Task<AgentActionReceipt> left = registry.SubmitAsync(first, SlowApply);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<AgentActionReceipt> right = registry.SubmitAsync(second, SlowApply);
        await Task.Delay(50);
        Assert.False(right.IsCompleted);
        release.TrySetResult();

        AgentActionReceipt[] receipts = await Task.WhenAll(left, right).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(receipts[0].OperationId, receipts[1].OperationId);
        Assert.Equal(OperationStatus.Accepted, receipts[0].Status);
        Assert.Equal(OperationStatus.Accepted, receipts[1].Status);
        Assert.Equal(1, apply.Count);
        Assert.Equal(receipts[0].CommandId, receipts[1].CommandId);
    }

    [Fact]
    public void Stale_expected_snapshot_is_refused_with_a_clear_code()
    {
        AgentOperationRegistry registry = OperationHarness.Registry(out _);
        var apply = new OperationHarness.ApplyCounter();
        AgentActionRequest request = OperationHarness.Request(expectedSnapshot: new SnapshotVersion(3));

        AgentActionReceipt receipt = registry.Accept(request, currentSnapshot: new SnapshotVersion(5));
        Assert.Equal(OperationStatus.Refused, receipt.Status);
        Assert.Equal(AgentRefusalCodes.StaleSnapshot, receipt.RefusalCode);
        Assert.Contains("later than expected", receipt.Detail, StringComparison.Ordinal);
        Assert.Equal(0, registry.Execute(receipt.OperationId, apply.Apply).Status == OperationStatus.Refused ? apply.Count : -1);

        AgentActionReceipt fresh = OperationHarness.Registry(out _).Accept(request, currentSnapshot: new SnapshotVersion(3));
        Assert.Equal(OperationStatus.Accepted, fresh.Status);
    }

    [Fact]
    public void Status_and_cancel_are_only_available_to_the_owner()
    {
        AgentOperationRegistry registry = OperationHarness.Registry(out _);
        AgentActionRequest request = OperationHarness.Request();
        AgentActionReceipt receipt = registry.Accept(request);
        var other = OperationHarness.Principal(principal: "principal-2", agent: "agent-2");

        AgentContractException get = Assert.Throws<AgentContractException>(() => registry.Get(other, receipt.OperationId));
        Assert.Equal(AgentRefusalCodes.AgentMismatch, get.Code);
        AgentContractException cancel = Assert.Throws<AgentContractException>(
            () => registry.Cancel(other, receipt.OperationId, allowCancel: true));
        Assert.Equal(AgentRefusalCodes.AgentMismatch, cancel.Code);
        Assert.Equal(OperationStatus.Accepted, registry.Get(request.Principal, receipt.OperationId)!.Status);
    }

    [Fact]
    public void Payload_fingerprint_is_stable_across_argument_key_order()
    {
        AgentActionRequest left = OperationHarness.Request(json: """{"count":1,"objectId":7}""");
        AgentActionRequest right = OperationHarness.Request(json: """{"objectId":7,"count":1}""");
        Assert.Equal(PayloadFingerprint.Compute(left), PayloadFingerprint.Compute(right));
        Assert.NotEqual(
            PayloadFingerprint.Compute(left),
            PayloadFingerprint.Compute(OperationHarness.Request(json: """{"objectId":7,"count":2}""")));
    }
}
