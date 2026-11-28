namespace Fineage.Agent.Identity;

/// <summary>
/// Issues and verifies Agent integration credentials, binds agents to characters, and
/// authorizes scoped operations. Tenant, principal and agent always come from a verified
/// credential. Game-account passwords are not used here.
/// </summary>
public sealed class AgentIdentityService
{
    readonly IAgentIdentityStore _store;
    readonly IGameCharacterOwnership _characters;
    readonly TimeProvider _clock;

    public AgentIdentityService(
        IAgentIdentityStore store,
        IGameCharacterOwnership characters,
        TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        _clock = clock ?? TimeProvider.System;
    }

    public TenantId CreateTenant(TenantId? tenantId = null)
    {
        TenantId id = tenantId ?? new TenantId(AgentSecretHasher.NewId("ten_"));
        _store.InsertTenant(new AgentTenantRecord(id, Now(), null));
        return id;
    }

    public PrincipalId CreatePrincipal(TenantId tenantId, PrincipalId? principalId = null)
    {
        RequireActiveTenant(tenantId);
        PrincipalId id = principalId ?? new PrincipalId(AgentSecretHasher.NewId("pr_"));
        _store.InsertPrincipal(new AgentPrincipalRecord(id, tenantId, Now(), null));
        return id;
    }

    public AgentId CreateAgent(
        PrincipalId principalId,
        IReadOnlyList<AgentScope> scopes,
        AgentId? agentId = null)
    {
        AgentPrincipalRecord principal = RequireActivePrincipal(principalId);
        if (scopes is null || scopes.Count == 0)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "An Agent requires at least one scope.");
        var unique = new List<AgentScope>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (AgentScope scope in scopes)
        {
            if (!seen.Add(scope.Value))
                continue;
            unique.Add(scope);
        }

        AgentId id = agentId ?? new AgentId(AgentSecretHasher.NewId("ag_"));
        _store.InsertAgent(new AgentRecord(id, principal.PrincipalId, principal.TenantId, unique, Now(), null));
        return id;
    }

    public AgentBindingRecord BindCharacter(AgentId agentId, string gameAccount, CharacterId characterId)
    {
        if (string.IsNullOrWhiteSpace(gameAccount))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Game account is required for a binding.");
        string account = gameAccount.Trim();
        AgentRecord agent = RequireActiveAgent(agentId);
        if (!_characters.TryGetOwnerAccount(characterId, out string owner) ||
            !string.Equals(owner, account, StringComparison.Ordinal))
        {
            throw new AgentContractException(
                AgentRefusalCodes.CharacterNotBound,
                "Character is not owned by the supplied game account.");
        }

        var binding = new AgentBindingRecord(
            agent.AgentId,
            agent.PrincipalId,
            agent.TenantId,
            account,
            characterId,
            Now(),
            null);
        _store.InsertBinding(binding);
        return binding;
    }

    public IssuedAgentCredential IssueCredential(AgentId agentId, DateTimeOffset expiresAt)
    {
        AgentRecord agent = RequireActiveAgent(agentId);
        DateTimeOffset now = Now();
        if (expiresAt <= now)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Credential expiry must be in the future.");

        string secret = AgentSecretHasher.NewSecret();
        var (hash, salt, iterations) = AgentSecretHasher.Hash(secret);
        string credentialId = AgentSecretHasher.NewId("cr_");
        _store.InsertCredential(new AgentCredentialRecord(
            credentialId,
            agent.AgentId,
            agent.PrincipalId,
            agent.TenantId,
            hash,
            salt,
            iterations,
            now,
            expiresAt.ToUniversalTime(),
            null));
        return new IssuedAgentCredential(
            credentialId,
            secret,
            expiresAt.ToUniversalTime(),
            agent.TenantId,
            agent.PrincipalId,
            agent.AgentId);
    }

    public bool TryAuthenticate(string credentialId, string secret, out VerifiedAgentIdentity identity, out string? refusalCode)
    {
        identity = null!;
        refusalCode = AuthenticateCore(credentialId, secret, out VerifiedAgentIdentity? verified);
        if (refusalCode != null)
        {
            AgentIdentityTelemetry.RecordDenial(refusalCode);
            return false;
        }

        identity = verified!;
        return true;
    }

    public VerifiedAgentIdentity Authenticate(string credentialId, string secret)
    {
        if (!TryAuthenticate(credentialId, secret, out VerifiedAgentIdentity identity, out string? refusalCode))
        {
            throw new AgentContractException(refusalCode ?? AgentRefusalCodes.InvalidCredential, "Authentication refused.");
        }

        return identity;
    }

    public AgentAuthorization Authorize(
        VerifiedAgentIdentity identity,
        CharacterId characterId,
        AgentScope requiredScope,
        UntrustedIdentityClaims? claims = null)
    {
        AgentAuthorization result = AuthorizeCore(identity, characterId, requiredScope, claims);
        if (!result.Allowed)
            AgentIdentityTelemetry.RecordDenial(result.RefusalCode ?? AgentRefusalCodes.InvalidContract);
        return result;
    }

    public AgentAuthorization AuthorizeTool(
        VerifiedAgentIdentity identity,
        CharacterId characterId,
        string toolName,
        UntrustedIdentityClaims? claims = null)
    {
        if (!AgentToolScopes.TryGetRequiredScope(toolName, out AgentScope scope))
        {
            AgentIdentityTelemetry.RecordDenial(AgentRefusalCodes.UnknownTool);
            return AgentAuthorization.Deny(AgentRefusalCodes.UnknownTool, $"Tool '{toolName}' is not published.");
        }

        return Authorize(identity, characterId, scope, claims);
    }

    public void RevokeCredential(string credentialId, string reason)
    {
        DateTimeOffset now = Now();
        AgentCredentialRecord? row = _store.FindCredential(credentialId);
        if (row is null)
            throw new AgentContractException(AgentRefusalCodes.InvalidCredential, "Credential was not found.");
        _store.MarkCredentialRevoked(credentialId, now);
        LogRevocation(row.TenantId, row.PrincipalId, row.AgentId, credentialId, reason);
    }

    public void RevokeAgent(AgentId agentId, string reason)
    {
        DateTimeOffset now = Now();
        AgentRecord agent = RequireAgent(agentId);
        _store.MarkAgentRevoked(agent.AgentId, now);
        _store.MarkAgentCredentialsRevoked(agent.AgentId, now);
        LogRevocation(agent.TenantId, agent.PrincipalId, agent.AgentId, null, reason);
    }

    public void RevokePrincipal(PrincipalId principalId, string reason)
    {
        DateTimeOffset now = Now();
        AgentPrincipalRecord principal = RequirePrincipal(principalId);
        _store.MarkPrincipalRevoked(principal.PrincipalId, now);
        _store.MarkPrincipalCredentialsRevoked(principal.PrincipalId, now);
        foreach (AgentRecord agent in _store.ListAgents(principal.PrincipalId))
        {
            _store.MarkAgentRevoked(agent.AgentId, now);
            _store.MarkAgentCredentialsRevoked(agent.AgentId, now);
        }

        LogRevocation(principal.TenantId, principal.PrincipalId, null, null, reason);
    }

    string? AuthenticateCore(string credentialId, string secret, out VerifiedAgentIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(credentialId) || string.IsNullOrEmpty(secret))
            return AgentRefusalCodes.InvalidCredential;

        AgentCredentialRecord? credential = _store.FindCredential(credentialId);
        // Same code for unknown id and bad secret so existence is not leaked.
        if (credential is null ||
            !AgentSecretHasher.Verify(secret, credential.SecretHash, credential.SecretSalt, credential.Iterations))
        {
            return AgentRefusalCodes.InvalidCredential;
        }

        if (IsRevoked(credential))
            return AgentRefusalCodes.Revoked;
        if (credential.ExpiresAt <= Now())
            return AgentRefusalCodes.Expired;

        AgentRecord? agent = _store.FindAgent(credential.AgentId);
        AgentPrincipalRecord? principal = _store.FindPrincipal(credential.PrincipalId);
        AgentTenantRecord? tenant = _store.FindTenant(credential.TenantId);
        if (agent is null || principal is null || tenant is null)
            return AgentRefusalCodes.InvalidCredential;
        if (agent.TenantId.Value != credential.TenantId.Value ||
            principal.TenantId.Value != credential.TenantId.Value)
        {
            return AgentRefusalCodes.CrossTenant;
        }

        if (IsRevoked(tenant) || IsRevoked(principal) || IsRevoked(agent))
            return AgentRefusalCodes.Revoked;

        identity = new VerifiedAgentIdentity(
            credential.CredentialId,
            credential.TenantId,
            credential.PrincipalId,
            credential.AgentId,
            agent.Scopes,
            credential.ExpiresAt);
        return null;
    }

    AgentAuthorization AuthorizeCore(
        VerifiedAgentIdentity identity,
        CharacterId characterId,
        AgentScope requiredScope,
        UntrustedIdentityClaims? claims)
    {
        ArgumentNullException.ThrowIfNull(identity);

        // Re-load so a revocation after authenticate is visible immediately.
        string? stale = AuthenticateLive(identity, out VerifiedAgentIdentity? live);
        if (stale != null)
            return AgentAuthorization.Deny(stale, "The credential is no longer usable.");
        identity = live!;

        if (claims?.TenantId is { } claimedTenant && claimedTenant.Value != identity.TenantId.Value)
        {
            return AgentAuthorization.Deny(
                AgentRefusalCodes.CrossTenant,
                "tenantId must come from the verified credential.");
        }

        if (claims?.AgentId is { } claimedAgent && claimedAgent.Value != identity.AgentId.Value)
        {
            return AgentAuthorization.Deny(
                AgentRefusalCodes.AgentMismatch,
                "agentId does not match the verified credential.");
        }

        if (claims?.CharacterId is { } claimedCharacter && claimedCharacter.Value != characterId.Value)
        {
            return AgentAuthorization.Deny(
                AgentRefusalCodes.CharacterNotBound,
                "Character in the payload does not match the action character.");
        }

        if (!identity.Scopes.Any(scope => scope.Value == requiredScope.Value))
        {
            return AgentAuthorization.Deny(
                AgentRefusalCodes.ScopeDenied,
                $"Scope {requiredScope.Value} is not granted.");
        }

        AgentBindingRecord? binding = _store.FindActiveBinding(identity.AgentId, characterId);
        if (binding is null)
        {
            AgentBindingRecord? other = _store.FindActiveBindingByCharacter(characterId);
            if (other is not null && other.TenantId.Value != identity.TenantId.Value)
            {
                return AgentAuthorization.Deny(
                    AgentRefusalCodes.CrossTenant,
                    "Character belongs to another tenant.");
            }

            return AgentAuthorization.Deny(
                AgentRefusalCodes.CharacterNotBound,
                "Character is not on this Agent's allowlist.");
        }

        if (binding.TenantId.Value != identity.TenantId.Value)
        {
            return AgentAuthorization.Deny(
                AgentRefusalCodes.CrossTenant,
                "Character binding tenant does not match the verified credential.");
        }

        return AgentAuthorization.Permit(identity, characterId, requiredScope);
    }

    string? AuthenticateLive(VerifiedAgentIdentity identity, out VerifiedAgentIdentity? live)
    {
        live = null;
        AgentCredentialRecord? credential = _store.FindCredential(identity.CredentialId);
        if (credential is null)
            return AgentRefusalCodes.InvalidCredential;
        if (IsRevoked(credential))
            return AgentRefusalCodes.Revoked;
        if (credential.ExpiresAt <= Now())
            return AgentRefusalCodes.Expired;
        if (credential.AgentId.Value != identity.AgentId.Value ||
            credential.PrincipalId.Value != identity.PrincipalId.Value ||
            credential.TenantId.Value != identity.TenantId.Value)
        {
            return AgentRefusalCodes.CrossTenant;
        }

        AgentRecord? agent = _store.FindAgent(credential.AgentId);
        AgentPrincipalRecord? principal = _store.FindPrincipal(credential.PrincipalId);
        AgentTenantRecord? tenant = _store.FindTenant(credential.TenantId);
        if (agent is null || principal is null || tenant is null)
            return AgentRefusalCodes.InvalidCredential;
        if (IsRevoked(tenant) || IsRevoked(principal) || IsRevoked(agent))
            return AgentRefusalCodes.Revoked;

        live = identity with { Scopes = agent.Scopes, ExpiresAt = credential.ExpiresAt };
        return null;
    }

    void LogRevocation(TenantId tenantId, PrincipalId? principalId, AgentId? agentId, string? credentialId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            reason = "revoked";
        _store.AppendRevocationLog(new AgentRevocationLogRecord(
            Now(),
            tenantId,
            principalId,
            agentId,
            credentialId,
            reason,
            RevocationPolicy.AcceptedCompleteWithAudit));
    }

    AgentTenantRecord RequireActiveTenant(TenantId tenantId)
    {
        AgentTenantRecord? row = _store.FindTenant(tenantId);
        if (row is null)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Tenant was not found.");
        if (IsRevoked(row))
            throw new AgentContractException(AgentRefusalCodes.Revoked, "Tenant is revoked.");
        return row;
    }

    AgentPrincipalRecord RequireActivePrincipal(PrincipalId principalId)
    {
        AgentPrincipalRecord row = RequirePrincipal(principalId);
        if (IsRevoked(row))
            throw new AgentContractException(AgentRefusalCodes.Revoked, "Principal is revoked.");
        RequireActiveTenant(row.TenantId);
        return row;
    }

    AgentPrincipalRecord RequirePrincipal(PrincipalId principalId) =>
        _store.FindPrincipal(principalId)
        ?? throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Principal was not found.");

    AgentRecord RequireActiveAgent(AgentId agentId)
    {
        AgentRecord row = RequireAgent(agentId);
        if (IsRevoked(row))
            throw new AgentContractException(AgentRefusalCodes.Revoked, "Agent is revoked.");
        RequireActivePrincipal(row.PrincipalId);
        return row;
    }

    AgentRecord RequireAgent(AgentId agentId) =>
        _store.FindAgent(agentId)
        ?? throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Agent was not found.");

    static bool IsRevoked(AgentTenantRecord row) => row.RevokedAt is not null;
    static bool IsRevoked(AgentPrincipalRecord row) => row.RevokedAt is not null;
    static bool IsRevoked(AgentRecord row) => row.RevokedAt is not null;
    static bool IsRevoked(AgentCredentialRecord row) => row.RevokedAt is not null;

    DateTimeOffset Now() => _clock.GetUtcNow();
}
