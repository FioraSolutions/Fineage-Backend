using System.Text.Json;
using Npgsql;

namespace Fineage.Login;

/// <summary>
/// PostgreSQL-backed <see cref="IAccountStore"/>. Authoritative document lives in
/// <c>store_snapshot</c>; mutations reuse <see cref="JsonAccountStore"/> in-process logic
/// and flush the snapshot (plus light projections) after each write.
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
        if (string.IsNullOrWhiteSpace(existing))
            store.PersistSnapshot(inner.ExportSnapshot());
        return store;
    }

    /// <summary>One-shot: validate accounts.json and replace the Postgres snapshot (restart processes after).</summary>
    public static void ImportJsonFileToDatabase(string connectionString, string jsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        string json = File.ReadAllText(jsonPath);
        string snapshot = JsonAccountStore.FromSnapshot(json).ExportSnapshot();
        var dataSource = NpgsqlDataSource.Create(connectionString);
        ApplyMigrations(dataSource);
        using var store = new PostgresAccountStore(dataSource, JsonAccountStore.InMemory());
        store.PersistSnapshot(snapshot);
    }

    public void Dispose() => _dataSource.Dispose();

    static void ApplyMigrations(NpgsqlDataSource dataSource)
    {
        string migrationsDir = FindMigrationsDirectory();
        using var conn = dataSource.OpenConnection();
        // Serialize Login+Zone boot so CREATE TABLE races do not trip pg_type uniqueness.
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
            string trimmed = part.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
                continue;
            // Keep lines that are only comments out; allow statements that contain comment lines.
            var meaningful = new List<string>();
            foreach (string line in trimmed.Split('\n'))
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

        // Source-tree fallback when running via `dotnet run` from repo.
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
        // Validate versioned document.
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

            // Best-effort projections for SQL tooling (authoritative doc remains snapshot).
            try
            {
                ProjectTables(conn, tx, snapshotJson);
            }
            catch
            {
                // Projection failure must not lose the snapshot write.
            }

            tx.Commit();
        }
    }

    static void ProjectTables(NpgsqlConnection conn, NpgsqlTransaction tx, string snapshotJson)
    {
        using var wipeAccounts = conn.CreateCommand();
        wipeAccounts.Transaction = tx;
        wipeAccounts.CommandText = "TRUNCATE clans; TRUNCATE accounts CASCADE;";
        wipeAccounts.ExecuteNonQuery();

        using var doc = JsonDocument.Parse(snapshotJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("accounts", out var accounts))
            return;

        foreach (var account in accounts.EnumerateArray())
        {
            string name = account.GetProperty("name").GetString() ?? "";
            if (string.IsNullOrEmpty(name))
                continue;
            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText =
                    """
                    INSERT INTO accounts (name, email, password_hash, password_salt, iterations)
                    VALUES (@name, @email, @hash, @salt, @iter)
                    """;
                ins.Parameters.AddWithValue("name", name);
                object email = account.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String
                    ? e.GetString()!
                    : DBNull.Value;
                if (email is string es && string.IsNullOrWhiteSpace(es))
                    email = DBNull.Value;
                ins.Parameters.AddWithValue("email", email);
                ins.Parameters.AddWithValue("hash", account.GetProperty("passwordHash").GetString() ?? "");
                ins.Parameters.AddWithValue("salt", account.GetProperty("passwordSalt").GetString() ?? "");
                ins.Parameters.AddWithValue("iter", account.GetProperty("iterations").GetInt32());
                ins.ExecuteNonQuery();
            }

            if (!account.TryGetProperty("characters", out var characters))
                continue;
            foreach (var ch in characters.EnumerateArray())
            {
                using var insChar = conn.CreateCommand();
                insChar.Transaction = tx;
                insChar.CommandText =
                    """
                    INSERT INTO characters (id, account, name, class_id, race, sex, level, payload, updated_at)
                    VALUES (@id, @account, @name, @classId, @race, @sex, @level, @payload::jsonb, now())
                    """;
                insChar.Parameters.AddWithValue("id", ch.GetProperty("id").GetInt32());
                insChar.Parameters.AddWithValue("account", name);
                insChar.Parameters.AddWithValue("name", ch.GetProperty("name").GetString() ?? "");
                insChar.Parameters.AddWithValue("classId", ch.GetProperty("classId").GetInt32());
                insChar.Parameters.AddWithValue("race", ch.TryGetProperty("race", out var race) ? race.GetInt32() : 0);
                insChar.Parameters.AddWithValue("sex", ch.TryGetProperty("sex", out var sex) ? sex.GetInt32() : 0);
                insChar.Parameters.AddWithValue("level", ch.TryGetProperty("level", out var level) ? level.GetInt32() : 1);
                insChar.Parameters.AddWithValue("payload", ch.GetRawText());
                insChar.ExecuteNonQuery();
            }
        }

        if (!root.TryGetProperty("clans", out var clans))
            return;
        foreach (var clan in clans.EnumerateArray())
        {
            using var insClan = conn.CreateCommand();
            insClan.Transaction = tx;
            insClan.CommandText =
                """
                INSERT INTO clans (id, name, payload)
                VALUES (@id, @name, @payload::jsonb)
                """;
            insClan.Parameters.AddWithValue("id", clan.GetProperty("id").GetInt32());
            insClan.Parameters.AddWithValue("name", clan.GetProperty("name").GetString() ?? "");
            insClan.Parameters.AddWithValue("payload", clan.GetRawText());
            insClan.ExecuteNonQuery();
        }
    }

    // --- IAccountStore forward ---

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
        _inner.TryReplaceItems(account, characterId, items);

    public bool TryReplaceWarehouse(string account, int characterId, IReadOnlyList<StoredItem> items) =>
        _inner.TryReplaceWarehouse(account, characterId, items);

    public bool TrySavePresence(string account, int characterId, int x, int y, int z, DateTimeOffset? lastAccess) =>
        _inner.TrySavePresence(account, characterId, x, y, z, lastAccess);

    public bool TrySaveVitals(string account, int characterId, int hp, int mp) =>
        _inner.TrySaveVitals(account, characterId, hp, mp);

    public bool TrySaveProgress(string account, int characterId, int level, long exp, int sp, int maxHp, int hp, int maxMp, int mp) =>
        _inner.TrySaveProgress(account, characterId, level, exp, sp, maxHp, hp, maxMp, mp);

    public bool TryReplaceQuests(string account, int characterId, IReadOnlyList<Fineage.Game.Quests.QuestSaveRow> quests) =>
        _inner.TryReplaceQuests(account, characterId, quests);

    public bool TrySetLastAccess(string account, int characterId, DateTimeOffset when) =>
        _inner.TrySetLastAccess(account, characterId, when);

    public bool TrySetDeleteAt(string account, int characterId, DateTimeOffset deleteAt) =>
        _inner.TrySetDeleteAt(account, characterId, deleteAt);

    public bool TryClearDeletion(string account, int characterId) =>
        _inner.TryClearDeletion(account, characterId);

    public bool TryHardDelete(string account, int characterId) =>
        _inner.TryHardDelete(account, characterId);

    public CharacterRecord? FindById(string account, int characterId) =>
        _inner.FindById(account, characterId);

    public CharacterRecord? FindByName(string account, string name) =>
        _inner.FindByName(account, name);

    public IReadOnlyList<CharacterRecord> List(string account, DateTimeOffset now) =>
        _inner.List(account, now);

    public IReadOnlyList<CharacterRecord> List(string account) =>
        _inner.List(account);

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
