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
            else
                store.SyncAllocationCountersFromDatabase();
            store.HydrateWeddingFromNormalized();
            store.HydrateCursedWeaponsFromNormalized();
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
        using (EnterPersist())
        {
            using var conn = _dataSource.OpenConnection();
            using var tx = conn.BeginTransaction();
            UpsertSnapshotRow(conn, tx, snapshotJson);
            NormalizedStoreWriter.UpsertFromSnapshot(conn, tx, snapshotJson);
            tx.Commit();
        }
    }

    /// <summary>The economy ledger journal in the same database.</summary>
    public PostgresLedgerJournal OpenLedgerJournal() => new(_dataSource);

    public void Dispose() => _dataSource.Dispose();

    /// <summary>
    /// Lock order is strict: durable persistence first, memory second. Snapshot callbacks run after the memory
    /// lock is released (see <see cref="MemoryAccountStore.AfterSaveSnapshot"/>), so the adapter may take this gate
    /// there. Any caller that enters this gate while already holding the memory store lock would recreate the
    /// register/create inversion and is rejected instead of deadlocking.
    /// </summary>
    readonly struct PersistGuard : IDisposable
    {
        readonly object _gate;

        internal PersistGuard(object gate) => _gate = gate;

        public void Dispose() => Monitor.Exit(_gate);
    }

    PersistGuard EnterPersist()
    {
        if (Monitor.IsEntered(_inner.SyncRoot))
            throw new InvalidOperationException(
                "durable store gate entered while holding the memory store lock; lock order is persist -> memory");
        Monitor.Enter(_persistGate);
        return new PersistGuard(_persistGate);
    }

    static void ApplyMigrations(NpgsqlDataSource dataSource)
    {
        // Boot uses the same runner as `Fineage.Login migrate`; checksums and pending plans are
        // validated before anything is written. See docs/operations/migrations.md.
        MigrationRunner runner = MigrationRunner.CreateDefault();
        using var conn = dataSource.OpenConnection();
        runner.Apply(new NpgsqlMigrationDatabase(conn));
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
        using (EnterPersist())
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
        using (EnterPersist())
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

    void SyncAllocationCountersFromDatabase()
    {
        using var conn = _dataSource.OpenConnection();
        var counters = NormalizedStoreMeta.ReadAllocationCounters(conn);
        _inner.SyncAllocationCounters(
            counters.NextCharacterId,
            counters.NextClanId,
            counters.NextItemObjectId);
    }

    // --- IAccountStore ---
}
