using System.Text;
using System.Text.Json;
using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Accounts;

/// <summary>
/// PostgreSQL-backed <see cref="IAccountStore"/>. In-process mutations reuse
/// <see cref="MemoryAccountStore"/>; normalized character/item/quest tables are the
/// durable character-domain source of truth. <c>store_snapshot</c> remains a backup export.
/// </summary>
public sealed partial class PostgresAccountStore : IAccountStore, IDisposable
{
    readonly NpgsqlDataSource _dataSource;
    readonly MemoryAccountStore _inner;
    readonly object _persistGate = new();

    PostgresAccountStore(NpgsqlDataSource dataSource, MemoryAccountStore inner)
    {
        _dataSource = dataSource;
        _inner = inner;
        // Backup JSON only — never UpsertFromSnapshot here (Claude B9 Login residual).
        // Character-domain SQL is written by RunSilent / TryAddCharacter paths.
        _inner.AfterSaveSnapshot = WriteSnapshotBackup;
    }

    public static PostgresAccountStore Open(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var dataSource = NpgsqlDataSource.Create(connectionString);
        try
        {
            ApplyMigrations(dataSource);
            string? existing = LoadSnapshot(dataSource);
            var inner = string.IsNullOrWhiteSpace(existing)
                ? MemoryAccountStore.InMemory()
                : MemoryAccountStore.FromSnapshot(existing);
            var store = new PostgresAccountStore(dataSource, inner);
            // Normalized tables are the character-domain SoT (FindById reads SQL). Reprojecting
            // from store_snapshot on every Open would let a stale Login/Zone JSON wipe live rows
            // (Claude B9). Bootstrap only when the SQL side is empty.
            if (!store.HasNormalizedCharacterRows())
                store.BackfillNormalizedFromSnapshot();
            return store;
        }
        catch
        {
            dataSource.Dispose();
            throw;
        }
    }

    /// <summary>Re-project normalized tables from the current in-memory snapshot (CLI / recovery).</summary>
    public void BackfillNormalizedFromSnapshot()
    {
        string snapshotJson = _inner.ExportSnapshot();
        using var _ = JsonDocument.Parse(snapshotJson);
        lock (_persistGate)
        {
            using var conn = _dataSource.OpenConnection();
            using var tx = conn.BeginTransaction();
            UpsertSnapshotRow(conn, tx, snapshotJson);
            NormalizedStoreWriter.UpsertFromSnapshot(conn, tx, snapshotJson);
            tx.Commit();
        }
    }

    public void Dispose() => _dataSource.Dispose();

    static void ApplyMigrations(NpgsqlDataSource dataSource)
    {
        string migrationsDir = FindMigrationsDirectory();
        using var conn = dataSource.OpenConnection();
        using (var lockCmd = conn.CreateCommand())
        {
            lockCmd.CommandText = "SELECT pg_advisory_lock(87201401)";
            lockCmd.ExecuteNonQuery();
        }

        try
        {
            using (var ensure = conn.CreateCommand())
            {
                ensure.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS schema_migrations (
                        id text PRIMARY KEY,
                        applied_at timestamptz NOT NULL DEFAULT now()
                    );
                    """;
                ensure.ExecuteNonQuery();
            }

            foreach (string file in Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
            {
                string id = Path.GetFileName(file);
                using (var check = conn.CreateCommand())
                {
                    check.CommandText = "SELECT 1 FROM schema_migrations WHERE id = @id";
                    check.Parameters.AddWithValue("id", id);
                    if (check.ExecuteScalar() != null)
                        continue;
                }

                string sql = File.ReadAllText(file);
                using var tx = conn.BeginTransaction();
                foreach (string statement in SplitSqlStatements(sql))
                {
                    using var apply = conn.CreateCommand();
                    apply.Transaction = tx;
                    apply.CommandText = statement;
                    apply.ExecuteNonQuery();
                }

                using (var mark = conn.CreateCommand())
                {
                    mark.Transaction = tx;
                    mark.CommandText = "INSERT INTO schema_migrations (id) VALUES (@id) ON CONFLICT (id) DO NOTHING";
                    mark.Parameters.AddWithValue("id", id);
                    mark.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }
        finally
        {
            using var unlock = conn.CreateCommand();
            unlock.CommandText = "SELECT pg_advisory_unlock(87201401)";
            unlock.ExecuteNonQuery();
        }
    }

    static IEnumerable<string> SplitSqlStatements(string sql)
    {
        // Strip -- comments before splitting on ';' so a semicolon inside a header comment
        // (e.g. "JSON mirror first; table later") cannot leak as a fake statement.
        var withoutComments = new StringBuilder(sql.Length);
        foreach (string line in sql.Split('\n'))
        {
            string t = line.TrimStart();
            if (t.StartsWith("--", StringComparison.Ordinal))
                continue;
            withoutComments.AppendLine(line);
        }

        foreach (string part in withoutComments.ToString()
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0)
                continue;
            yield return part;
        }
    }

    static string FindMigrationsDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "Persistence", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;
            candidate = Path.Combine(dir, "migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        string src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Persistence", "Migrations"));
        if (Directory.Exists(src))
            return src;

        throw new DirectoryNotFoundException("Could not locate Persistence/Migrations SQL folder.");
    }

    static string? LoadSnapshot(NpgsqlDataSource dataSource)
    {
        using var conn = dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload::text FROM store_snapshot WHERE id = 1";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Writes <c>store_snapshot</c> backup JSON only. Does not reproject normalized character tables —
    /// Login’s in-memory graph can lag Zone hot-path SQL (Claude B9).
    /// </summary>
    void WriteSnapshotBackup(string snapshotJson)
    {
        using var _ = JsonDocument.Parse(snapshotJson);
        lock (_persistGate)
        {
            using var conn = _dataSource.OpenConnection();
            using var tx = conn.BeginTransaction();
            UpsertSnapshotRow(conn, tx, snapshotJson);
            tx.Commit();
        }
    }

    static void UpsertSnapshotRow(NpgsqlConnection conn, NpgsqlTransaction tx, string snapshotJson)
    {
        using var upsert = conn.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText =
            """
            INSERT INTO store_snapshot (id, version, payload, updated_at)
            VALUES (1, 1, @payload::jsonb, now())
            ON CONFLICT (id) DO UPDATE
            SET payload = excluded.payload,
                updated_at = now();
            """;
        upsert.Parameters.AddWithValue("payload", snapshotJson);
        upsert.ExecuteNonQuery();
    }

    bool RunSilent(Func<bool> mutation, Action<NpgsqlConnection> sql)
    {
        bool previous = _inner.EmitSnapshots;
        _inner.EmitSnapshots = false;
        try
        {
            if (!mutation())
                return false;
        }
        finally
        {
            _inner.EmitSnapshots = previous;
        }

        // Hot paths write normalized SQL only. Rewriting store_snapshot from this process's
        // partial MemoryAccountStore would clobber the other process's backup JSON (Claude B9).
        lock (_persistGate)
        {
            using var conn = _dataSource.OpenConnection();
            sql(conn);
        }

        return true;
    }

    bool HasNormalizedCharacterRows()
    {
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM characters LIMIT 1)";
        return cmd.ExecuteScalar() is true;
    }

    // --- IAccountStore ---
}
