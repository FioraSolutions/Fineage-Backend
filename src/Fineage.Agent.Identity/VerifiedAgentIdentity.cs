using Fineage.Agent.Contracts;

namespace Fineage.Agent.Identity;

/// <summary>Identity taken only from a verified credential, never from a tool payload.</summary>
public sealed record VerifiedAgentIdentity(
    string CredentialId,
    TenantId TenantId,
    PrincipalId PrincipalId,
    AgentId AgentId,
    IReadOnlyList<AgentScope> Scopes,
    DateTimeOffset ExpiresAt)
{
    public AgentPrincipal ToPrincipal() =>
        AgentPrincipal.Create(TenantId, PrincipalId, AgentId, characterId: null, ExpiresAt);
}
