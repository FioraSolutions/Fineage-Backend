using System.Text.Json;
using Fineage.Agent.Contracts;
using Fineage.Agent.Operations;

namespace Fineage.Agent.Operations.Tests;

static class OperationHarness
{
    public static readonly DateTimeOffset Start = new(2026, 11, 28, 6, 0, 0, TimeSpan.Zero);

    public static MutableTimeProvider Clock() => new(Start);

    public static AgentPrincipal Principal(
        string tenant = "tenant-1",
        string principal = "principal-1",
        string agent = "agent-1",
        string? character = "4242") =>
        AgentPrincipal.Create(
            new TenantId(tenant),
            new PrincipalId(principal),
            new AgentId(agent),
            character is null ? null : new CharacterId(character),
            Start.AddHours(2));

    public static AgentActionRequest Request(
        string key = "idem-1",
        string tool = "drop_item",
        string json = """{"objectId":7,"count":1}""",
        string command = "cmd-1",
        string correlation = "corr-1",
        AgentPrincipal? principal = null,
        DateTimeOffset? deadline = null,
        SnapshotVersion? expectedSnapshot = null) =>
        AgentActionRequest.Create(
            new CommandId(command),
            new CorrelationId(correlation),
            new IdempotencyKey(key),
            principal ?? Principal(),
            tool,
            JsonDocument.Parse(json).RootElement.Clone(),
            deadline ?? Start.AddMinutes(10),
            expectedSnapshot);

    public static AgentOperationRegistry Registry(out MemoryAgentOperationStore store, MutableTimeProvider? clock = null)
    {
        store = new MemoryAgentOperationStore();
        return new AgentOperationRegistry(store, clock ?? Clock());
    }

    public static AgentOperationExecutionResult Succeed(string? ledgerCommitId = null) =>
        AgentOperationExecutionResult.Succeeded(ledgerCommitId);

    public sealed class ApplyCounter
    {
        int _count;
        public int Count => Volatile.Read(ref _count);
        public string? LastCommit { get; private set; }

        public AgentOperationExecutionResult Apply(AgentActionRequest request, CancellationToken cancellationToken)
        {
            int n = Interlocked.Increment(ref _count);
            LastCommit = $"10.{n}";
            return AgentOperationExecutionResult.Succeeded(LastCommit);
        }

        public Task<AgentOperationExecutionResult> ApplyAsync(AgentActionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Apply(request, cancellationToken));
        }
    }
}

sealed class MutableTimeProvider : TimeProvider
{
    DateTimeOffset _now;

    public MutableTimeProvider(DateTimeOffset now) => _now = now.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
