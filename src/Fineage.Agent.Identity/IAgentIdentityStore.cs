namespace Fineage.Agent.Identity;

public sealed record AgentTenantRecord(TenantId TenantId, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

public sealed record AgentPrincipalRecord(
    PrincipalId PrincipalId,
    TenantId TenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

public sealed record AgentRecord(
    AgentId AgentId,
    PrincipalId PrincipalId,
    TenantId TenantId,
    IReadOnlyList<AgentScope> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

public sealed record AgentBindingRecord(
    AgentId AgentId,
    PrincipalId PrincipalId,
    TenantId TenantId,
    string GameAccount,
    CharacterId CharacterId,
    DateTimeOffset BoundAt,
    DateTimeOffset? UnboundAt);

public sealed record AgentCredentialRecord(
    string CredentialId,
    AgentId AgentId,
    PrincipalId PrincipalId,
    TenantId TenantId,
    string SecretHash,
    string SecretSalt,
    int Iterations,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt);

public sealed record AgentRevocationLogRecord(
    DateTimeOffset At,
    TenantId TenantId,
    PrincipalId? PrincipalId,
    AgentId? AgentId,
    string? CredentialId,
    string Reason,
    string Policy);

public interface IAgentIdentityStore
{
    void InsertTenant(AgentTenantRecord tenant);
    AgentTenantRecord? FindTenant(TenantId tenantId);
    void MarkTenantRevoked(TenantId tenantId, DateTimeOffset at);

    void InsertPrincipal(AgentPrincipalRecord principal);
    AgentPrincipalRecord? FindPrincipal(PrincipalId principalId);
    void MarkPrincipalRevoked(PrincipalId principalId, DateTimeOffset at);

    void InsertAgent(AgentRecord agent);
    AgentRecord? FindAgent(AgentId agentId);
    void MarkAgentRevoked(AgentId agentId, DateTimeOffset at);
    IReadOnlyList<AgentRecord> ListAgents(PrincipalId principalId);

    void InsertBinding(AgentBindingRecord binding);
    AgentBindingRecord? FindActiveBinding(AgentId agentId, CharacterId characterId);
    AgentBindingRecord? FindActiveBindingByCharacter(CharacterId characterId);
    IReadOnlyList<AgentBindingRecord> ListActiveBindings(AgentId agentId);

    void InsertCredential(AgentCredentialRecord credential);
    AgentCredentialRecord? FindCredential(string credentialId);
    void MarkCredentialRevoked(string credentialId, DateTimeOffset at);
    void MarkAgentCredentialsRevoked(AgentId agentId, DateTimeOffset at);
    void MarkPrincipalCredentialsRevoked(PrincipalId principalId, DateTimeOffset at);

    void AppendRevocationLog(AgentRevocationLogRecord entry);
    IReadOnlyList<AgentRevocationLogRecord> ListRevocationLog();
}
