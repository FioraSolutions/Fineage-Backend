using Fineage.Agent.Contracts;
using Fineage.Agent.Identity;

namespace Fineage.Agent.Identity.Tests;

sealed class IdentityHarness
{
    public static readonly CharacterId AliceCharacter = new("4242");
    public static readonly CharacterId BobCharacter = new("7777");
    public const string AliceAccount = "alice";
    public const string BobAccount = "bob";

    public MemoryAgentIdentityStore Store { get; } = new();
    public MemoryGameCharacterOwnership Characters { get; } = new();
    public AgentIdentityService Service { get; }
    public FakeTimeProvider Clock { get; } = new();

    public IdentityHarness()
    {
        Clock.SetUtcNow(new DateTimeOffset(2026, 11, 28, 6, 0, 0, TimeSpan.Zero));
        Characters.Add(AliceCharacter, AliceAccount);
        Characters.Add(BobCharacter, BobAccount);
        Service = new AgentIdentityService(Store, Characters, Clock);
    }

    public SealedAgent Provision(
        string tenant,
        string principal,
        string agent,
        CharacterId character,
        string gameAccount,
        params AgentScope[] scopes)
    {
        TenantId tenantId = Service.CreateTenant(new TenantId(tenant));
        PrincipalId principalId = Service.CreatePrincipal(tenantId, new PrincipalId(principal));
        AgentId agentId = Service.CreateAgent(
            principalId,
            scopes.Length == 0 ? [AgentScope.WorldRead] : scopes,
            new AgentId(agent));
        Service.BindCharacter(agentId, gameAccount, character);
        IssuedAgentCredential issued = Service.IssueCredential(agentId, Clock.GetUtcNow().AddHours(1));
        return new SealedAgent(tenantId, principalId, agentId, character, issued);
    }
}

sealed record SealedAgent(
    TenantId TenantId,
    PrincipalId PrincipalId,
    AgentId AgentId,
    CharacterId CharacterId,
    IssuedAgentCredential Issued);

sealed class FakeTimeProvider : TimeProvider
{
    DateTimeOffset _now = DateTimeOffset.UtcNow;

    public void SetUtcNow(DateTimeOffset now) => _now = now.ToUniversalTime();

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    public override DateTimeOffset GetUtcNow() => _now;
}
