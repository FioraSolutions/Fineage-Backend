namespace Fineage.Agent.Identity;

public sealed class MemoryAgentIdentityStore : IAgentIdentityStore
{
    readonly object _gate = new();
    readonly Dictionary<string, AgentTenantRecord> _tenants = new(StringComparer.Ordinal);
    readonly Dictionary<string, AgentPrincipalRecord> _principals = new(StringComparer.Ordinal);
    readonly Dictionary<string, AgentRecord> _agents = new(StringComparer.Ordinal);
    readonly List<AgentBindingRecord> _bindings = [];
    readonly Dictionary<string, AgentCredentialRecord> _credentials = new(StringComparer.Ordinal);
    readonly List<AgentRevocationLogRecord> _log = [];

    public void InsertTenant(AgentTenantRecord tenant)
    {
        lock (_gate)
        {
            if (!_tenants.TryAdd(tenant.TenantId.Value, tenant))
                throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Tenant already exists.");
        }
    }

    public AgentTenantRecord? FindTenant(TenantId tenantId)
    {
        lock (_gate)
            return _tenants.GetValueOrDefault(tenantId.Value);
    }

    public void MarkTenantRevoked(TenantId tenantId, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (!_tenants.TryGetValue(tenantId.Value, out var row))
                throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Tenant was not found.");
            if (row.RevokedAt is null)
                _tenants[tenantId.Value] = row with { RevokedAt = at };
        }
    }

    public void InsertPrincipal(AgentPrincipalRecord principal)
    {
        lock (_gate)
        {
            if (!_principals.TryAdd(principal.PrincipalId.Value, principal))
                throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Principal already exists.");
        }
    }

    public AgentPrincipalRecord? FindPrincipal(PrincipalId principalId)
    {
        lock (_gate)
            return _principals.GetValueOrDefault(principalId.Value);
    }

    public void MarkPrincipalRevoked(PrincipalId principalId, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (!_principals.TryGetValue(principalId.Value, out var row))
                throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Principal was not found.");
            if (row.RevokedAt is null)
                _principals[principalId.Value] = row with { RevokedAt = at };
        }
    }

    public void InsertAgent(AgentRecord agent)
    {
        lock (_gate)
        {
            if (!_agents.TryAdd(agent.AgentId.Value, agent))
                throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Agent already exists.");
        }
    }

    public AgentRecord? FindAgent(AgentId agentId)
    {
        lock (_gate)
            return _agents.GetValueOrDefault(agentId.Value);
    }

    public void MarkAgentRevoked(AgentId agentId, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (!_agents.TryGetValue(agentId.Value, out var row))
                throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Agent was not found.");
            if (row.RevokedAt is null)
                _agents[agentId.Value] = row with { RevokedAt = at };
        }
    }

    public IReadOnlyList<AgentRecord> ListAgents(PrincipalId principalId)
    {
        lock (_gate)
            return _agents.Values.Where(row => row.PrincipalId.Value == principalId.Value).ToArray();
    }

    public void InsertBinding(AgentBindingRecord binding)
    {
        lock (_gate)
        {
            if (_bindings.Any(row => row.UnboundAt is null && row.CharacterId.Value == binding.CharacterId.Value))
            {
                throw new AgentContractException(
                    AgentRefusalCodes.InvalidContract,
                    "Character already has an active Agent binding.");
            }

            _bindings.Add(binding);
        }
    }

    public AgentBindingRecord? FindActiveBinding(AgentId agentId, CharacterId characterId)
    {
        lock (_gate)
        {
            return _bindings.FirstOrDefault(row =>
                row.UnboundAt is null &&
                row.AgentId.Value == agentId.Value &&
                row.CharacterId.Value == characterId.Value);
        }
    }

    public AgentBindingRecord? FindActiveBindingByCharacter(CharacterId characterId)
    {
        lock (_gate)
        {
            return _bindings.FirstOrDefault(row =>
                row.UnboundAt is null && row.CharacterId.Value == characterId.Value);
        }
    }

    public IReadOnlyList<AgentBindingRecord> ListActiveBindings(AgentId agentId)
    {
        lock (_gate)
            return _bindings.Where(row => row.UnboundAt is null && row.AgentId.Value == agentId.Value).ToArray();
    }

    public void InsertCredential(AgentCredentialRecord credential)
    {
        lock (_gate)
        {
            if (!_credentials.TryAdd(credential.CredentialId, credential))
                throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Credential already exists.");
        }
    }

    public AgentCredentialRecord? FindCredential(string credentialId)
    {
        lock (_gate)
            return _credentials.GetValueOrDefault(credentialId);
    }

    public void MarkCredentialRevoked(string credentialId, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (!_credentials.TryGetValue(credentialId, out var row))
                throw new AgentContractException(AgentRefusalCodes.InvalidCredential, "Credential was not found.");
            if (row.RevokedAt is null)
                _credentials[credentialId] = row with { RevokedAt = at };
        }
    }

    public void MarkAgentCredentialsRevoked(AgentId agentId, DateTimeOffset at)
    {
        lock (_gate)
        {
            foreach (var row in _credentials.Values.Where(item => item.AgentId.Value == agentId.Value && item.RevokedAt is null).ToArray())
                _credentials[row.CredentialId] = row with { RevokedAt = at };
        }
    }

    public void MarkPrincipalCredentialsRevoked(PrincipalId principalId, DateTimeOffset at)
    {
        lock (_gate)
        {
            foreach (var row in _credentials.Values.Where(item => item.PrincipalId.Value == principalId.Value && item.RevokedAt is null).ToArray())
                _credentials[row.CredentialId] = row with { RevokedAt = at };
        }
    }

    public void AppendRevocationLog(AgentRevocationLogRecord entry)
    {
        lock (_gate)
            _log.Add(entry);
    }

    public IReadOnlyList<AgentRevocationLogRecord> ListRevocationLog()
    {
        lock (_gate)
            return _log.ToArray();
    }

    /// <summary>Test hook: reports whether a plaintext secret was retained in memory.</summary>
    public bool ContainsPlaintext(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return false;
        lock (_gate)
        {
            foreach (var row in _credentials.Values)
            {
                if (row.SecretHash == secret || row.SecretSalt == secret || row.CredentialId == secret)
                    return true;
            }

            return false;
        }
    }
}
