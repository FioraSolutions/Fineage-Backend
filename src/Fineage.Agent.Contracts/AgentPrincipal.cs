namespace Fineage.Agent.Contracts;

/// <summary>
/// Identity binding: tenant, principal, agent, and optional character are distinct ids.
/// </summary>
public sealed record AgentPrincipal : IAgentContractDocument
{
    public AgentPrincipal(
        int schemaVersion,
        TenantId tenantId,
        PrincipalId principalId,
        AgentId agentId,
        CharacterId? characterId,
        DateTimeOffset? expiry)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        SchemaVersion = schemaVersion;
        TenantId = tenantId;
        PrincipalId = principalId;
        AgentId = agentId;
        CharacterId = characterId;
        Expiry = expiry is { } value ? ContractValidation.AsUtc(value) : null;
    }

    public int SchemaVersion { get; }
    public TenantId TenantId { get; }
    public PrincipalId PrincipalId { get; }
    public AgentId AgentId { get; }
    public CharacterId? CharacterId { get; }
    public DateTimeOffset? Expiry { get; }

    public static AgentPrincipal Create(
        TenantId tenantId,
        PrincipalId principalId,
        AgentId agentId,
        CharacterId? characterId = null,
        DateTimeOffset? expiry = null) =>
        new(AgentContractSchema.Current, tenantId, principalId, agentId, characterId, expiry);
}
