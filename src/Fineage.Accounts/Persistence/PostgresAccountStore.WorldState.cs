using Npgsql;
using NpgsqlTypes;

namespace Fineage.Accounts;

public sealed partial class PostgresAccountStore
{
    public IReadOnlyList<WorldStateDocument> LoadWorldState()
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = new NpgsqlCommand(
            "SELECT system, schema_version, revision, payload::text FROM world_state ORDER BY system", conn);
        return ReadDocuments(cmd);
    }

    public WorldStateDocument? LoadWorldState(string system)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        using var conn = _dataSource.OpenConnection();
        using var cmd = new NpgsqlCommand(
            "SELECT system, schema_version, revision, payload::text FROM world_state WHERE system = @system", conn);
        cmd.Parameters.AddWithValue("system", system);
        return ReadDocuments(cmd).SingleOrDefault();
    }

    public bool TryImportWorldState(WorldStateDocument document, WorldStateImport import)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(import);
        document.Validate();
        if (import.System != document.System)
            throw new ArgumentException($"import record names '{import.System}' but the document is '{document.System}'", nameof(import));
        using var conn = _dataSource.OpenConnection();
        using var tx = conn.BeginTransaction();
        using (var insert = new NpgsqlCommand(
                   """
                   INSERT INTO world_state (system, schema_version, revision, payload)
                   VALUES (@system, @version, @revision, @payload)
                   ON CONFLICT (system) DO NOTHING
                   """, conn, tx))
        {
            AddDocument(insert, document);
            if (insert.ExecuteNonQuery() == 0)
                return false;
        }

        using (var record = new NpgsqlCommand(
                   """
                   INSERT INTO world_state_imports (system, source_path, source_sha256, archived_path, imported_at)
                   VALUES (@system, @source, @sha, @archived, @at)
                   ON CONFLICT (system) DO UPDATE SET source_path = EXCLUDED.source_path,
                       source_sha256 = EXCLUDED.source_sha256, archived_path = EXCLUDED.archived_path,
                       imported_at = EXCLUDED.imported_at
                   """, conn, tx))
        {
            record.Parameters.AddWithValue("system", import.System);
            record.Parameters.AddWithValue("source", import.SourcePath);
            record.Parameters.AddWithValue("sha", import.SourceSha256);
            record.Parameters.AddWithValue("archived", import.ArchivedPath);
            record.Parameters.AddWithValue("at", import.ImportedAt.ToUniversalTime());
            record.ExecuteNonQuery();
        }

        tx.Commit();
        return true;
    }

    public IReadOnlyList<WorldStateImport> ListWorldStateImports()
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = new NpgsqlCommand(
            "SELECT system, source_path, source_sha256, archived_path, imported_at FROM world_state_imports ORDER BY system", conn);
        using var reader = cmd.ExecuteReader();
        var rows = new List<WorldStateImport>();
        while (reader.Read())
        {
            rows.Add(new WorldStateImport(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc))));
        }

        return rows;
    }

    /// <summary>Inside the economy commit's transaction. A row with a newer revision is left as it is.</summary>
    static void WriteWorldState(NpgsqlConnection conn, NpgsqlTransaction tx, IReadOnlyList<WorldStateDocument> documents)
    {
        foreach (var document in documents)
        {
            using var cmd = new NpgsqlCommand(
                """
                INSERT INTO world_state (system, schema_version, revision, payload, updated_at)
                VALUES (@system, @version, @revision, @payload, now())
                ON CONFLICT (system) DO UPDATE SET schema_version = EXCLUDED.schema_version,
                    revision = EXCLUDED.revision, payload = EXCLUDED.payload, updated_at = now()
                WHERE world_state.revision < EXCLUDED.revision
                """, conn, tx);
            AddDocument(cmd, document);
            cmd.ExecuteNonQuery();
        }
    }

    static void AddDocument(NpgsqlCommand cmd, WorldStateDocument document)
    {
        cmd.Parameters.AddWithValue("system", document.System);
        cmd.Parameters.AddWithValue("version", document.SchemaVersion);
        cmd.Parameters.AddWithValue("revision", document.Revision);
        cmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = document.Json });
    }

    static List<WorldStateDocument> ReadDocuments(NpgsqlCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var rows = new List<WorldStateDocument>();
        while (reader.Read())
            rows.Add(new WorldStateDocument(reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetString(3)));
        return rows;
    }
}
