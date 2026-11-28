using System.Text.Json;
using Fineage.Accounts;
using Fineage.Agent.Contracts;
using Fineage.Agent.Gate;
using Fineage.Agent.Identity;
using Fineage.Agent.Operations;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// AI-006: economy-mutating Agent tools go through the existing durable ledger path, and a
/// restarted Runtime reuses that commit instead of applying the move twice.
/// </summary>
public sealed class AgentOperationRecoveryTests
{
    const int Adena = 57;

    [Fact]
    public async Task Economy_mutating_retry_after_restart_reuses_the_durable_ledger_commit()
    {
        AgentOperationTelemetry.ResetForTests();
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        int slot = player.Slot(Adena);
        var (identity, character, adapter) = BindAgent(player, AgentScope.InventoryMutate);
        var store = new MemoryAgentOperationStore();
        var registry = new AgentOperationRegistry(store);
        var request = DropRequest(identity, character, slot, player, key: "idem-economy-1");

        AgentActionReceipt receipt = registry.Accept(request);
        Assert.Equal(OperationStatus.Accepted, receipt.Status);

        AgentOperation first = await registry.ExecuteAsync(
            receipt.OperationId,
            async (accepted, token) =>
            {
                Task<AgentCommandResult> pending = adapter.ExecuteAsync(identity, character, accepted, bench.World);
                bench.World.RunStepForTest();
                AgentCommandResult result = await pending.WaitAsync(TimeSpan.FromSeconds(10), token);
                if (!result.Ok)
                    return AgentOperationExecutionResult.Refused(AgentRefusalCodes.InvalidContract, result.IngressOutcome ?? result.Detail);
                string commit = bench.Ledger.Entries[^1].Transaction.Id.ToString();
                return AgentOperationExecutionResult.Succeeded(commit);
            });

        Assert.Equal(OperationStatus.Succeeded, first.Status);
        Assert.Equal(49, player.Saved().Single(item => item.ItemId == Adena).Count);
        int ledgerRows = bench.Ledger.Entries.Count;
        string commitId = store.FindById(receipt.OperationId)!.LedgerCommitId!;
        Assert.False(string.IsNullOrWhiteSpace(commitId));
        Assert.Equal(commitId, bench.Ledger.Entries[^1].Transaction.Id.ToString());

        var recovered = new AgentOperationRegistry(store);
        recovered.Recover();
        var retry = DropRequest(identity, character, slot, player, key: "idem-economy-1", command: "cmd-retry");
        AgentActionReceipt same = recovered.Accept(retry);
        Assert.Equal(receipt.OperationId, same.OperationId);
        Assert.Equal(OperationStatus.Accepted, same.Status);

        AgentOperation again = recovered.Execute(same.OperationId, (_, _) =>
            throw new InvalidOperationException("economy apply must not run again after the ledger commit is recorded"));
        Assert.Equal(OperationStatus.Succeeded, again.Status);
        Assert.Equal(ledgerRows, bench.Ledger.Entries.Count);
        Assert.Equal(49, player.Saved().Single(item => item.ItemId == Adena).Count);
        Assert.Equal(commitId, store.FindById(receipt.OperationId)!.LedgerCommitId);
    }

    static (VerifiedAgentIdentity Identity, CharacterId Character, AgentCommandAdapter Adapter) BindAgent(
        BenchPlayer player,
        AgentScope scope)
    {
        var store = new MemoryAgentIdentityStore();
        var ownership = new MemoryGameCharacterOwnership();
        var character = new CharacterId(player.Session.ObjectId.ToString());
        ownership.Add(character, player.Session.Account);
        var service = new AgentIdentityService(store, ownership);
        TenantId tenant = service.CreateTenant(new TenantId("tenant-ops"));
        PrincipalId principal = service.CreatePrincipal(tenant, new PrincipalId("principal-ops"));
        AgentId agent = service.CreateAgent(principal, [scope], new AgentId("agent-ops"));
        service.BindCharacter(agent, player.Session.Account, character);
        IssuedAgentCredential issued = service.IssueCredential(agent, DateTimeOffset.UtcNow.AddHours(1));
        return (service.Authenticate(issued.CredentialId, issued.RevealSecret()), character, new AgentCommandAdapter(service));
    }

    static AgentActionRequest DropRequest(
        VerifiedAgentIdentity identity,
        CharacterId character,
        int slot,
        BenchPlayer player,
        string key,
        string command = "cmd-economy-1")
    {
        var principal = AgentPrincipal.Create(identity.TenantId, identity.PrincipalId, identity.AgentId, character, identity.ExpiresAt);
        string json = $$"""{"objectId":{{slot}},"count":1,"x":{{player.Session.X}},"y":{{player.Session.Y}},"z":{{player.Session.Z}}}""";
        return AgentActionRequest.Create(
            new CommandId(command),
            new CorrelationId("corr-economy-1"),
            new IdempotencyKey(key),
            principal,
            "drop_item",
            JsonDocument.Parse(json).RootElement.Clone(),
            DateTimeOffset.UtcNow.AddMinutes(5));
    }
}
