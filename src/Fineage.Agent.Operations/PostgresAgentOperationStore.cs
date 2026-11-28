using Npgsql;
using NpgsqlTypes;

namespace Fineage.Agent.Operations;

/// <summary>Postgres persistence for the operation registry created by migration 025.</summary>
public sealed class PostgresAgentOperationStore : IAgentOperationStore
{
    const string Columns = """
        operation_id, tenant_id, principal_id, agent_id, character_id, idempotency_key,
        command_id, correlation_id, tool_name, payload_fingerprint, request_json::text,
        status, refusal_code, detail, ledger_commit_id, deadline, created_at, updated_at, completed_at
        """;

    readonly NpgsqlDataSource _dataSource;

    public PostgresAgentOperationStore(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public bool TryInsert(AgentOperationRecord record, out AgentOperationRecord stored)
    {
        ArgumentNullException.ThrowIfNull(record);
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO agent_operations (
                operation_id, tenant_id, principal_id, agent_id, character_id, idempotency_key,
                command_id, correlation_id, tool_name, payload_fingerprint, request_json,
                status, refusal_code, detail, ledger_commit_id, deadline, created_at, updated_at, completed_at)
            VALUES (
                @operation_id, @tenant_id, @principal_id, @agent_id, @character_id, @idempotency_key,
                @command_id, @correlation_id, @tool_name, @payload_fingerprint, @request_json,
                @status, @refusal_code, @detail, @ledger_commit_id, @deadline, @created_at, @updated_at, @completed_at)
            ON CONFLICT (tenant_id, principal_id, idempotency_key) DO NOTHING
            RETURNING {Columns}
            """;
        Bind(cmd, record);
        using (NpgsqlDataReader reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                stored = Read(reader);
                return true;
            }
        }

        AgentOperationRecord? existing = FindByIdempotency(conn, record.TenantId, record.PrincipalId, record.IdempotencyKey);
        stored = existing ?? throw new AgentContractException(
            AgentRefusalCodes.InvalidContract,
            "The idempotency key conflicted but the existing operation could not be read.");
        return false;
    }

    public AgentOperationRecord? FindById(OperationId operationId)
    {
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM agent_operations WHERE operation_id = @id";
        cmd.Parameters.AddWithValue("id", operationId.Value);
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public AgentOperationRecord? FindByIdempotency(TenantId tenantId, PrincipalId principalId, IdempotencyKey key)
    {
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        return FindByIdempotency(conn, tenantId, principalId, key);
    }

    public bool TryUpdate(AgentOperationRecord expected, AgentOperationRecord next)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(next);
        if (expected.OperationId.Value != next.OperationId.Value)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Update must keep the same operation id.");

        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE agent_operations SET
                status = @status,
                refusal_code = @refusal_code,
                detail = @detail,
                ledger_commit_id = @ledger_commit_id,
                updated_at = @updated_at,
                completed_at = @completed_at
            WHERE operation_id = @operation_id
              AND status = @expected_status
              AND updated_at = @expected_updated
            """;
        cmd.Parameters.AddWithValue("status", next.Status.ToString());
        cmd.Parameters.AddWithValue("refusal_code", (object?)next.RefusalCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("detail", (object?)next.Detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ledger_commit_id", (object?)next.LedgerCommitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updated_at", next.UpdatedAt);
        cmd.Parameters.AddWithValue("completed_at", (object?)next.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("operation_id", next.OperationId.Value);
        cmd.Parameters.AddWithValue("expected_status", expected.Status.ToString());
        cmd.Parameters.AddWithValue("expected_updated", expected.UpdatedAt);
        return cmd.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<AgentOperationRecord> ListOpen()
    {
        using NpgsqlConnection conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {Columns} FROM agent_operations
            WHERE status IN ('Accepted', 'Running', 'Unknown')
            ORDER BY created_at
            """;
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        var list = new List<AgentOperationRecord>();
        while (reader.Read())
            list.Add(Read(reader));
        return list;
    }

    static AgentOperationRecord? FindByIdempotency(
        NpgsqlConnection conn,
        TenantId tenantId,
        PrincipalId principalId,
        IdempotencyKey key)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {Columns} FROM agent_operations
            WHERE tenant_id = @tenant AND principal_id = @principal AND idempotency_key = @key
            """;
        cmd.Parameters.AddWithValue("tenant", tenantId.Value);
        cmd.Parameters.AddWithValue("principal", principalId.Value);
        cmd.Parameters.AddWithValue("key", key.Value);
        using NpgsqlDataReader reader = cmd.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    static void Bind(NpgsqlCommand cmd, AgentOperationRecord record)
    {
        cmd.Parameters.AddWithValue("operation_id", record.OperationId.Value);
        cmd.Parameters.AddWithValue("tenant_id", record.TenantId.Value);
        cmd.Parameters.AddWithValue("principal_id", record.PrincipalId.Value);
        cmd.Parameters.AddWithValue("agent_id", record.AgentId.Value);
        cmd.Parameters.AddWithValue("character_id", (object?)record.CharacterId?.Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("idempotency_key", record.IdempotencyKey.Value);
        cmd.Parameters.AddWithValue("command_id", record.Request.CommandId.Value);
        cmd.Parameters.AddWithValue("correlation_id", record.Request.CorrelationId.Value);
        cmd.Parameters.AddWithValue("tool_name", record.Request.ToolName);
        cmd.Parameters.AddWithValue("payload_fingerprint", record.PayloadFingerprint);
        cmd.Parameters.Add("request_json", NpgsqlDbType.Jsonb).Value = AgentContractJson.Serialize(record.Request);
        cmd.Parameters.AddWithValue("status", record.Status.ToString());
        cmd.Parameters.AddWithValue("refusal_code", (object?)record.RefusalCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("detail", (object?)record.Detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ledger_commit_id", (object?)record.LedgerCommitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("deadline", record.Deadline);
        cmd.Parameters.AddWithValue("created_at", record.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", record.UpdatedAt);
        cmd.Parameters.AddWithValue("completed_at", (object?)record.CompletedAt ?? DBNull.Value);
    }

    static AgentOperationRecord Read(NpgsqlDataReader reader)
    {
        string requestJson = reader.GetString(10);
        AgentActionRequest request = AgentContractJson.Deserialize<AgentActionRequest>(requestJson);
        return new AgentOperationRecord(
            new OperationId(reader.GetString(0)),
            request,
            reader.GetString(9),
            Enum.Parse<OperationStatus>(reader.GetString(11)),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            ReadTime(reader, 16),
            ReadTime(reader, 17),
            ReadTimeOrNull(reader, 18));
    }

    static DateTimeOffset ReadTime(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    static DateTimeOffset? ReadTimeOrNull(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}
