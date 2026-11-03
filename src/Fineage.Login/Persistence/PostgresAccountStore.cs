using System.Text.Json;
using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Login;

/// <summary>
/// PostgreSQL-backed <see cref="IAccountStore"/>. In-process mutations reuse
/// <see cref="JsonAccountStore"/>; normalized character/item/quest tables are the
/// durable character-domain source of truth. <c>store_snapshot</c> remains a backup export.
/// </summary>
public sealed class PostgresAccountStore : IAccountStore, IDisposable
{
    readonly NpgsqlDataSource _dataSource;
    readonly JsonAccountStore _inner;
    readonly object _persistGate = new();

    PostgresAccountStore(NpgsqlDataSource dataSource, JsonAccountStore inner)
    {
        _dataSource = dataSource;
        _inner = inner;
        _inner.AfterSaveSnapshot = PersistSnapshot;
    }

    public static PostgresAccountStore Open(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var dataSource = NpgsqlDataSource.Create(connectionString);
        ApplyMigrations(dataSource);
        string? existing = LoadSnapshot(dataSource);
        var inner = string.IsNullOrWhiteSpace(existing)
            ? JsonAccountStore.InMemory()
            : JsonAccountStore.FromSnapshot(existing);
        var store = new PostgresAccountStore(dataSource, inner);
        // Ensure normalized tables match memory (assigns object ids, backfills SQL).
        store.PersistSnapshot(inner.ExportSnapshot());
        return store;
    }

    /// <summary>One-shot: validate accounts.json and replace the Postgres snapshot + normalized tables.</summary>
    public static void ImportJsonFileToDatabase(string connectionString, string jsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        string json = File.ReadAllText(jsonPath);
        string snapshot = JsonAccountStore.FromSnapshot(json).ExportSnapshot();
        var dataSource = NpgsqlDataSource.Create(connectionString);
        ApplyMigrations(dataSource);
        using var store = new PostgresAccountStore(dataSource, JsonAccountStore.FromSnapshot(snapshot));
        store.PersistSnapshot(snapshot);
    }

    /// <summary>Re-project normalized tables from the current in-memory snapshot (CLI / recovery).</summary>
    public void BackfillNormalizedFromSnapshot()
    {
        PersistSnapshot(_inner.ExportSnapshot());
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
        foreach (string part in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Strip comment-only lines; do not skip the whole chunk when it starts with a header comment
            // (002_normalize_characters.sql leads with comments then ALTER … hair_style).
            var meaningful = new List<string>();
            foreach (string line in part.Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith("--", StringComparison.Ordinal))
                    continue;
                meaningful.Add(line);
            }

            if (meaningful.Count == 0)
                continue;
            yield return string.Join('\n', meaningful);
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

    void PersistSnapshot(string snapshotJson)
    {
        using var _ = JsonDocument.Parse(snapshotJson);

        lock (_persistGate)
        {
            using var conn = _dataSource.OpenConnection();
            using var tx = conn.BeginTransaction();
            using (var upsert = conn.CreateCommand())
            {
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

            NormalizedStoreWriter.UpsertFromSnapshot(conn, tx, snapshotJson);
            tx.Commit();
        }
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

        // Export before taking _persistGate so we never nest with JsonAccountStore._gate.
        string snapshot = _inner.ExportSnapshot();
        lock (_persistGate)
        {
            using var conn = _dataSource.OpenConnection();
            sql(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO store_snapshot (id, version, payload, updated_at)
                VALUES (1, 1, @payload::jsonb, now())
                ON CONFLICT (id) DO UPDATE
                SET payload = excluded.payload,
                    updated_at = now();
                """;
            cmd.Parameters.AddWithValue("payload", snapshot);
            cmd.ExecuteNonQuery();
        }

        return true;
    }

    // --- IAccountStore ---

    public bool AccountExists(string account) => _inner.AccountExists(account);

    public AddAccountStatus TryAddAccount(string name, string passwordHash, string passwordSalt, int iterations, string? email) =>
        _inner.TryAddAccount(name, passwordHash, passwordSalt, iterations, email);

    public bool TryGetCredentialByEmail(string email, out AccountCredential credential) =>
        _inner.TryGetCredentialByEmail(email, out credential);

    public bool TryGetCredential(string account, out AccountCredential credential) =>
        _inner.TryGetCredential(account, out credential);

    public CreateCharacterStatus TryAddCharacter(
        string account,
        string name,
        int classId,
        int race,
        int sex,
        out CharacterRecord? created,
        IReadOnlyList<StoredItem>? items = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? now = null,
        int hairStyle = 0,
        int hairColor = 0,
        int face = 0,
        bool hasPosition = false,
        int x = 0,
        int y = 0,
        int z = 0,
        int level = 1,
        int maxHp = 0,
        int hp = 0,
        int maxMp = 0,
        int mp = 0,
        int sp = 0,
        long adena = 0,
        string title = "",
        int? maxCharacters = null) =>
        _inner.TryAddCharacter(
            account, name, classId, race, sex, out created, items, createdAt, now,
            hairStyle, hairColor, face, hasPosition, x, y, z, level, maxHp, hp, maxMp, mp, sp, adena, title, maxCharacters);

    public bool TryReplaceItems(string account, int characterId, IReadOnlyList<StoredItem> items) =>
        RunSilent(
            () => _inner.TryReplaceItems(account, characterId, items),
            conn =>
            {
                var record = _inner.FindById(account, characterId)
                    ?? throw new InvalidOperationException($"Character {characterId} missing after item replace.");
                NormalizedStoreWriter.ReplaceInventoryOnly(conn, characterId, record.Items ?? []);
            });

    public bool TryReplaceWarehouse(string account, int characterId, IReadOnlyList<StoredItem> items) =>
        RunSilent(
            () => _inner.TryReplaceWarehouse(account, characterId, items),
            conn =>
            {
                var record = _inner.FindById(account, characterId)
                    ?? throw new InvalidOperationException($"Character {characterId} missing after warehouse replace.");
                NormalizedStoreWriter.ReplaceWarehouseOnly(conn, characterId, record.Warehouse ?? []);
            });

    public bool TrySavePresence(string account, int characterId, int x, int y, int z, DateTimeOffset? lastAccess) =>
        RunSilent(
            () => _inner.TrySavePresence(account, characterId, x, y, z, lastAccess),
            conn => NormalizedStoreWriter.UpdatePresence(conn, characterId, x, y, z, lastAccess));

    public bool TrySaveVitals(string account, int characterId, int hp, int mp) =>
        RunSilent(
            () => _inner.TrySaveVitals(account, characterId, hp, mp),
            conn => NormalizedStoreWriter.UpdateVitals(conn, characterId, hp, mp));

    public bool TrySaveProgress(string account, int characterId, int level, long exp, int sp, int maxHp, int hp, int maxMp, int mp) =>
        RunSilent(
            () => _inner.TrySaveProgress(account, characterId, level, exp, sp, maxHp, hp, maxMp, mp),
            conn => NormalizedStoreWriter.UpdateProgress(conn, characterId, level, exp, sp, maxHp, hp, maxMp, mp));

    public bool TrySaveClass(string account, int characterId, int classId) =>
        RunSilent(
            () => _inner.TrySaveClass(account, characterId, classId),
            conn => NormalizedStoreWriter.UpdateClass(conn, characterId, classId));

    public bool TryReplaceQuests(string account, int characterId, IReadOnlyList<QuestSaveRow> quests) =>
        RunSilent(
            () => _inner.TryReplaceQuests(account, characterId, quests),
            conn =>
            {
                using var tx = conn.BeginTransaction();
                NormalizedStoreWriter.ReplaceQuests(conn, tx, characterId, quests);
                tx.Commit();
            });

    public bool TryReplaceLearnedSkills(string account, int characterId, IReadOnlyList<StoredSkill> skills) =>
        RunSilent(
            () => _inner.TryReplaceLearnedSkills(account, characterId, skills),
            conn =>
            {
                using var tx = conn.BeginTransaction();
                NormalizedStoreWriter.ReplaceLearnedSkills(conn, tx, characterId, skills);
                tx.Commit();
            });

    public bool TryReplaceShortcuts(string account, int characterId, IReadOnlyList<StoredShortcut> shortcuts) =>
        RunSilent(
            () => _inner.TryReplaceShortcuts(account, characterId, shortcuts),
            conn =>
            {
                using var tx = conn.BeginTransaction();
                NormalizedStoreWriter.ReplaceShortcuts(conn, tx, characterId, shortcuts);
                tx.Commit();
            });

    public bool TrySetLastAccess(string account, int characterId, DateTimeOffset when) =>
        _inner.TrySetLastAccess(account, characterId, when);

    public bool TrySetDeleteAt(string account, int characterId, DateTimeOffset deleteAt) =>
        _inner.TrySetDeleteAt(account, characterId, deleteAt);

    public bool TryClearDeletion(string account, int characterId) =>
        _inner.TryClearDeletion(account, characterId);

    public bool TryHardDelete(string account, int characterId) =>
        _inner.TryHardDelete(account, characterId);

    public CharacterRecord? FindById(string account, int characterId)
    {
        using var conn = _dataSource.OpenConnection();
        var record = NormalizedStoreReader.TryLoadById(conn, account, characterId);
        if (record == null)
            return null;
        Hydrate(conn, record);
        return record;
    }

    public CharacterRecord? FindByName(string account, string name)
    {
        using var conn = _dataSource.OpenConnection();
        var record = NormalizedStoreReader.TryLoadByName(conn, account, name);
        if (record == null)
            return null;
        Hydrate(conn, record);
        return record;
    }

    public IReadOnlyList<CharacterRecord> List(string account, DateTimeOffset now)
    {
        using var conn = _dataSource.OpenConnection();
        var rows = NormalizedStoreReader.ListByAccount(conn, account);
        if (rows.Count == 0)
            return [];

        var cred = NormalizedStoreReader.TryLoadAccount(conn, account);
        foreach (var row in rows)
        {
            _inner.HydrateCharacter(
                row,
                cred?.PasswordHash,
                cred?.PasswordSalt,
                cred?.Iterations ?? 0,
                cred?.Email);
        }

        // Honour pending-delete purge rules from the in-memory store after hydrate.
        return _inner.List(account, now);
    }

    public IReadOnlyList<CharacterRecord> List(string account) =>
        List(account, DateTimeOffset.UtcNow);

    void Hydrate(NpgsqlConnection conn, CharacterRecord record)
    {
        var cred = NormalizedStoreReader.TryLoadAccount(conn, record.Account);
        _inner.HydrateCharacter(
            record,
            cred?.PasswordHash,
            cred?.PasswordSalt,
            cred?.Iterations ?? 0,
            cred?.Email);
    }

    public IReadOnlyList<ClanRecord> ListClans() => _inner.ListClans();

    public ClanRecord? FindClan(int clanId) => _inner.FindClan(clanId);

    public ClanRecord? FindClanByName(string name) => _inner.FindClanByName(name);

    public ClanRecord? InsertClan(ClanRecord draft, CharacterClanState leader) =>
        _inner.InsertClan(draft, leader);

    public bool UpdateClan(ClanRecord clan, IReadOnlyList<CharacterClanState> characters) =>
        _inner.UpdateClan(clan, characters);

    public bool DeleteClan(int clanId, IReadOnlyList<CharacterClanState> characters) =>
        _inner.DeleteClan(clanId, characters);

    public bool TrySetCharacterClanState(CharacterClanState state) =>
        _inner.TrySetCharacterClanState(state);

    public IReadOnlyList<CastleSave> ListCastles() => _inner.ListCastles();

    public IReadOnlyList<SiegeClanSave> ListSiegeClans() => _inner.ListSiegeClans();

    public bool SaveSiege(
        IReadOnlyList<CastleSave> castles,
        IReadOnlyList<SiegeClanSave> clans,
        IReadOnlyDictionary<int, int> clanCastleIds) =>
        _inner.SaveSiege(castles, clans, clanCastleIds);
}
