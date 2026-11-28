namespace Fineage.Agent.Identity;

/// <summary>
/// A newly issued integration credential. The plaintext secret is available only through
/// <see cref="RevealSecret"/> and is not included in <see cref="ToString"/>.
/// </summary>
public sealed class IssuedAgentCredential
{
    readonly string _secret;

    public IssuedAgentCredential(
        string credentialId,
        string secret,
        DateTimeOffset expiresAt,
        TenantId tenantId,
        PrincipalId principalId,
        AgentId agentId)
    {
        if (string.IsNullOrWhiteSpace(credentialId))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "CredentialId is required.");
        if (string.IsNullOrWhiteSpace(secret))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "A secret is required at issuance.");
        CredentialId = credentialId;
        _secret = secret;
        ExpiresAt = expiresAt;
        TenantId = tenantId;
        PrincipalId = principalId;
        AgentId = agentId;
    }

    public string CredentialId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public TenantId TenantId { get; }
    public PrincipalId PrincipalId { get; }
    public AgentId AgentId { get; }

    public string RevealSecret() => _secret;

    public override string ToString() => $"IssuedAgentCredential {CredentialId}";
}
