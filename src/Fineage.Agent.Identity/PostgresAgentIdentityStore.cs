using Npgsql;

namespace Fineage.Agent.Identity;

/// <summary>Postgres persistence for Agent identity tables created by migration 024.</summary>
public sealed class PostgresAgentIdentityStore : IAgentIdentityStore
{
    readonly NpgsqlDataSource _dataSource;

    public PostgresAgentIdentityStore(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public void InsertTenant(AgentTenantRecord tenant) =>
        Execute(
            "INSERT INTO agent_tenants (tenant_id, created_at, revoked_at) VALUES (@id, @at, @rev)",
            cmd =>
            {
                cmd.Parameters.AddWithValue("id", tenant.TenantId.Value);
                cmd.Parameters.AddWithValue("at", tenant.CreatedAt);
                cmd.Parameters.AddWithValue("rev", (object?)tenant.RevokedAt ?? DBNull.Value);
            });

    public AgentTenantRecord? FindTenant(TenantId tenantId) =>
        QueryOne(
            "SELECT tenant_id, created_at, revoked_at FROM agent_tenants WHERE tenant_id = @id",
            cmd => cmd.Parameters.AddWithValue("id", tenantId.Value),
            reader => new AgentTenantRecord(
                new TenantId(reader.GetString(0)),
                ReadTime(reader, 1),
                ReadTimeOrNull(reader, 2)));

    public void MarkTenantRevoked(TenantId tenantId, DateTimeOffset at) =>
        Execute(
            "UPDATE agent_tenants SET revoked_at = @at WHERE tenant_id = @id AND revoked_at IS NULL",
            cmd =>
            {
                cmd.Parameters.AddWithValue("id", tenantId.Value);
                cmd.Parameters.AddWithValue("at", at);
            });

    public void InsertPrincipal(AgentPrincipalRecord principal) =>
        Execute(
            "INSERT INTO agent_principals (principal_id, tenant_id, created_at, revoked_at) VALUES (@id, @tenant, @at, @rev)",
            cmd =>
            {
                cmd.Parameters.AddWithValue("id", principal.PrincipalId.Value);
                cmd.Parameters.AddWithValue("tenant", principal.TenantId.Value);
                cmd.Parameters.AddWithValue("at", principal.CreatedAt);
                cmd.Parameters.AddWithValue("rev", (object?)principal.RevokedAt ?? DBNull.Value);
            });

    public AgentPrincipalRecord? FindPrincipal(PrincipalId principalId) =>
        QueryOne(
            "SELECT principal_id, tenant_id, created_at, revoked_at FROM agent_principals WHERE principal_id = @id",
            cmd => cmd.Parameters.AddWithValue("id", principalId.Value),
            reader => new AgentPrincipalRecord(
                new PrincipalId(reader.GetString(0)),
                new TenantId(reader.GetString(1)),
                ReadTime(reader, 2),
                ReadTimeOrNull(reader, 3)));

    public void MarkPrincipalRevoked(PrincipalId principalId, DateTimeOffset at) =>
        Execute(
            "UPDATE agent_principals SET revoked_at = @at WHERE principal_id = @id AND revoked_at IS NULL",
            cmd =>
            {
                cmd.Parameters.AddWithValue("id", principalId.Value);
                cmd.Parameters.AddWithValue("at", at);
            });

    public void InsertAgent(AgentRecord agent)
    {
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO agent_agents (agent_id, principal_id, tenant_id, created_at, revoked_at) VALUES (@id, @principal, @tenant, @at, @rev)";
            cmd.Parameters.AddWithValue("id", agent.AgentId.Value);
            cmd.Parameters.AddWithValue("principal", agent.PrincipalId.Value);
            cmd.Parameters.AddWithValue("tenant", agent.TenantId.Value);
            cmd.Parameters.AddWithValue("at", agent.CreatedAt);
            cmd.Parameters.AddWithValue("rev", (object?)agent.RevokedAt ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        foreach (AgentScope scope in agent.Scopes)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO agent_scopes (agent_id, scope) VALUES (@id, @scope)";
            cmd.Parameters.AddWithValue("id", agent.AgentId.Value);
            cmd.Parameters.AddWithValue("scope", scope.Value);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public AgentRecord? FindAgent(AgentId agentId)
    {
        AgentRecord? header = QueryOne(
            "SELECT agent_id, principal_id, tenant_id, created_at, revoked_at FROM agent_agents WHERE agent_id = @id",
            cmd => cmd.Parameters.AddWithValue("id", agentId.Value),
            reader => new AgentRecord(
                new AgentId(reader.GetString(0)),
                new PrincipalId(reader.GetString(1)),
                new TenantId(reader.GetString(2)),
                [],
                ReadTime(reader, 3),
                ReadTimeOrNull(reader, 4)));
        if (header is null)
            return null;
        return header with { Scopes = LoadScopes(agentId) };
    }

    public void MarkAgentRevoked(AgentId agentId, DateTimeOffset at) =>
        Execute(
            "UPDATE agent_agents SET revoked_at = @at WHERE agent_id = @id AND revoked_at IS NULL",
            cmd =>
            {
                cmd.Parameters.AddWithValue("id", agentId.Value);
                cmd.Parameters.AddWithValue("at", at);
            });

    public IReadOnlyList<AgentRecord> ListAgents(PrincipalId principalId)
    {
        var list = new List<AgentRecord>();
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT agent_id, principal_id, tenant_id, created_at, revoked_at FROM agent_agents WHERE principal_id = @id";
        cmd.Parameters.AddWithValue("id", principalId.Value);
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AgentRecord(
                new AgentId(reader.GetString(0)),
                new PrincipalId(reader.GetString(1)),
                new TenantId(reader.GetString(2)),
                [],
                ReadTime(reader, 3),
                ReadTimeOrNull(reader, 4)));
        }

        return list.Select(row => row with { Scopes = LoadScopes(row.AgentId) }).ToArray();
    }

    public void InsertBinding(AgentBindingRecord binding) =>
        Execute(
            """
            INSERT INTO agent_character_bindings
                (agent_id, principal_id, tenant_id, game_account, character_id, bound_at, unbound_at)
            VALUES (@agent, @principal, @tenant, @account, @character, @at, @unbound)
            """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("agent", binding.AgentId.Value);
                cmd.Parameters.AddWithValue("principal", binding.PrincipalId.Value);
                cmd.Parameters.AddWithValue("tenant", binding.TenantId.Value);
                cmd.Parameters.AddWithValue("account", binding.GameAccount);
                cmd.Parameters.AddWithValue("character", binding.CharacterId.Value);
                cmd.Parameters.AddWithValue("at", binding.BoundAt);
                cmd.Parameters.AddWithValue("unbound", (object?)binding.UnboundAt ?? DBNull.Value);
            });

    public AgentBindingRecord? FindActiveBinding(AgentId agentId, CharacterId characterId) =>
        QueryOne(
            """
            SELECT agent_id, principal_id, tenant_id, game_account, character_id, bound_at, unbound_at
            FROM agent_character_bindings
            WHERE agent_id = @agent AND character_id = @character AND unbound_at IS NULL
            """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("agent", agentId.Value);
                cmd.Parameters.AddWithValue("character", characterId.Value);
            },
            ReadBinding);

    public AgentBindingRecord? FindActiveBindingByCharacter(CharacterId characterId) =>
        QueryOne(
            """
            SELECT agent_id, principal_id, tenant_id, game_account, character_id, bound_at, unbound_at
            FROM agent_character_bindings
            WHERE character_id = @character AND unbound_at IS NULL
            """,
            cmd => cmd.Parameters.AddWithValue("character", characterId.Value),
            ReadBinding);

    public IReadOnlyList<AgentBindingRecord> ListActiveBindings(AgentId agentId)
    {
        var list = new List<AgentBindingRecord>();
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT agent_id, principal_id, tenant_id, game_account, character_id, bound_at, unbound_at
            FROM agent_character_bindings
            WHERE agent_id = @agent AND unbound_at IS NULL
            """;
        cmd.Parameters.AddWithValue("agent", agentId.Value);
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadBinding(reader));
        return list;
    }

    public void InsertCredential(AgentCredentialRecord credential) =>
        Execute(
            """
            INSERT INTO agent_credentials
                (credential_id, agent_id, principal_id, tenant_id, secret_hash, secret_salt, iterations, created_at, expires_at, revoked_at)
            VALUES (@id, @agent, @principal, @tenant, @hash, @salt, @iter, @at, @exp, @rev)
            """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("id", credential.CredentialId);
                cmd.Parameters.AddWithValue("agent", credential.AgentId.Value);
                cmd.Parameters.AddWithValue("principal", credential.PrincipalId.Value);
                cmd.Parameters.AddWithValue("tenant", credential.TenantId.Value);
                cmd.Parameters.AddWithValue("hash", credential.SecretHash);
                cmd.Parameters.AddWithValue("salt", credential.SecretSalt);
                cmd.Parameters.AddWithValue("iter", credential.Iterations);
                cmd.Parameters.AddWithValue("at", credential.CreatedAt);
                cmd.Parameters.AddWithValue("exp", credential.ExpiresAt);
                cmd.Parameters.AddWithValue("rev", (object?)credential.RevokedAt ?? DBNull.Value);
            });

    public AgentCredentialRecord? FindCredential(string credentialId) =>
        QueryOne(
            """
            SELECT credential_id, agent_id, principal_id, tenant_id, secret_hash, secret_salt, iterations, created_at, expires_at, revoked_at
            FROM agent_credentials WHERE credential_id = @id
            """,
            cmd => cmd.Parameters.AddWithValue("id", credentialId),
            reader => new AgentCredentialRecord(
                reader.GetString(0),
                new AgentId(reader.GetString(1)),
                new PrincipalId(reader.GetString(2)),
                new TenantId(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                ReadTime(reader, 7),
                ReadTime(reader, 8),
                ReadTimeOrNull(reader, 9)));

    public void MarkCredentialRevoked(string credentialId, DateTimeOffset at) =>
        Execute(
            "UPDATE agent_credentials SET revoked_at = @at WHERE credential_id = @id AND revoked_at IS NULL",
            cmd =>
            {
                cmd.Parameters.AddWithValue("id", credentialId);
                cmd.Parameters.AddWithValue("at", at);
            });

    public void MarkAgentCredentialsRevoked(AgentId agentId, DateTimeOffset at) =>
        Execute(
            "UPDATE agent_credentials SET revoked_at = @at WHERE agent_id = @id AND revoked_at IS NULL",
            cmd =>
            {
                cmd.Parameters.AddWithValue("id", agentId.Value);
                cmd.Parameters.AddWithValue("at", at);
            });

    public void MarkPrincipalCredentialsRevoked(PrincipalId principalId, DateTimeOffset at) =>
        Execute(
            "UPDATE agent_credentials SET revoked_at = @at WHERE principal_id = @id AND revoked_at IS NULL",
            cmd =>
            {
                cmd.Parameters.AddWithValue("id", principalId.Value);
                cmd.Parameters.AddWithValue("at", at);
            });

    public void AppendRevocationLog(AgentRevocationLogRecord entry) =>
        Execute(
            """
            INSERT INTO agent_revocation_log (at, tenant_id, principal_id, agent_id, credential_id, reason, policy)
            VALUES (@at, @tenant, @principal, @agent, @credential, @reason, @policy)
            """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("at", entry.At);
                cmd.Parameters.AddWithValue("tenant", entry.TenantId.Value);
                cmd.Parameters.AddWithValue("principal", (object?)entry.PrincipalId?.Value ?? DBNull.Value);
                cmd.Parameters.AddWithValue("agent", (object?)entry.AgentId?.Value ?? DBNull.Value);
                cmd.Parameters.AddWithValue("credential", (object?)entry.CredentialId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("reason", entry.Reason);
                cmd.Parameters.AddWithValue("policy", entry.Policy);
            });

    public IReadOnlyList<AgentRevocationLogRecord> ListRevocationLog()
    {
        var list = new List<AgentRevocationLogRecord>();
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT at, tenant_id, principal_id, agent_id, credential_id, reason, policy FROM agent_revocation_log ORDER BY id";
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AgentRevocationLogRecord(
                ReadTime(reader, 0),
                new TenantId(reader.GetString(1)),
                reader.IsDBNull(2) ? null : new PrincipalId(reader.GetString(2)),
                reader.IsDBNull(3) ? null : new AgentId(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return list;
    }

    IReadOnlyList<AgentScope> LoadScopes(AgentId agentId)
    {
        var list = new List<AgentScope>();
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT scope FROM agent_scopes WHERE agent_id = @id";
        cmd.Parameters.AddWithValue("id", agentId.Value);
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(AgentScope.Parse(reader.GetString(0)));
        return list;
    }

    void Execute(string sql, Action<NpgsqlCommand> bind)
    {
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        bind(cmd);
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "A unique identity constraint was violated.");
        }
    }

    T? QueryOne<T>(string sql, Action<NpgsqlCommand> bind, Func<NpgsqlDataReader, T> map)
        where T : class
    {
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        bind(cmd);
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        return reader.Read() ? map(reader) : null;
    }

    static AgentBindingRecord ReadBinding(NpgsqlDataReader reader) =>
        new(
            new AgentId(reader.GetString(0)),
            new PrincipalId(reader.GetString(1)),
            new TenantId(reader.GetString(2)),
            reader.GetString(3),
            new CharacterId(reader.GetString(4)),
            ReadTime(reader, 5),
            ReadTimeOrNull(reader, 6));

    static DateTimeOffset ReadTime(NpgsqlDataReader reader, int ordinal) =>
        new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    static DateTimeOffset? ReadTimeOrNull(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}
