using System.Text;
using Npgsql;

namespace Fineage.Accounts;

/// <summary>
/// What the runner would do. Building a plan never writes to the database, so dry runs and deploy
/// preflights can use the same validation as a real apply.
/// </summary>
public sealed class MigrationPlan
{
    public MigrationPlan(
        IReadOnlyList<MigrationScript> applied,
        IReadOnlyList<MigrationScript> pending,
        IReadOnlyList<string> appliedWithoutChecksum)
    {
        Applied = applied;
        Pending = pending;
        AppliedWithoutChecksum = appliedWithoutChecksum;
    }

    /// <summary>Applied migrations that still have a file on disk.</summary>
    public IReadOnlyList<MigrationScript> Applied { get; }

    /// <summary>Migrations that would be applied, in file-name order.</summary>
    public IReadOnlyList<MigrationScript> Pending { get; }

    /// <summary>
    /// Applied rows recorded before checksum tracking existed. They are accepted for upgrade
    /// compatibility but never backfilled automatically; <c>migrate --adopt-checksums</c> is explicit.
    /// </summary>
    public IReadOnlyList<string> AppliedWithoutChecksum { get; }

    public bool IsUpToDate => Pending.Count == 0;
}

/// <summary>
/// The database operations the runner needs. Kept behind an interface so fresh-database, existing-database
/// and failure-retry behavior can be tested without a live PostgreSQL instance.
/// </summary>
public interface IMigrationDatabase
{
    bool HistoryTableExists();

    IReadOnlyDictionary<string, string?> ReadAppliedChecksums();

    /// <summary>Creates <c>schema_migrations</c> and adds the checksum column to older tables.</summary>
    void EnsureHistoryTable();

    /// <summary>Runs one migration and records its id + checksum atomically.</summary>
    void Apply(MigrationScript script);

    /// <summary>Records checksums for legacy rows that have none. Only called by the explicit adopt command.</summary>
    void AdoptChecksums(IReadOnlyDictionary<string, string> checksums);

    /// <summary>Serializes concurrent runners (PostgreSQL advisory lock; no-op for fakes).</summary>
    IDisposable AcquireLock();
}

/// <summary>
/// The single migration runner used by boot and the <c>migrate</c> CLI. Applies only pending files in
/// order, validates every already-applied checksum first, and never edits or deletes history.
/// </summary>
public sealed class MigrationRunner
{
    public MigrationRunner(MigrationCatalog catalog)
    {
        Catalog = catalog;
    }

    public MigrationCatalog Catalog { get; }

    public static MigrationRunner CreateDefault() =>
        new(MigrationCatalog.Load(MigrationCatalog.FindApplicationMigrationsDirectory()));

    /// <summary>Read-only plan; does not create the history table or take the advisory lock.</summary>
    public MigrationPlan Plan(IMigrationDatabase database) =>
        BuildPlan(database.ReadAppliedChecksums());

    public MigrationPlan BuildPlan(IReadOnlyDictionary<string, string?> applied)
    {
        var scriptsById = Catalog.Scripts.ToDictionary(script => script.Id, StringComparer.Ordinal);
        var appliedOnDisk = new List<MigrationScript>(applied.Count);
        var pending = new List<MigrationScript>();
        var unverified = new List<string>();

        foreach ((string id, string? recordedChecksum) in applied.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!scriptsById.TryGetValue(id, out MigrationScript? script))
                throw new MigrationException(
                    $"Database records applied migration '{id}', but '{Catalog.Directory}' has no such file. " +
                    "Forward-only migrations must remain on disk so every environment can be audited.");

            if (string.IsNullOrWhiteSpace(recordedChecksum))
            {
                unverified.Add(id);
            }
            else if (!string.Equals(recordedChecksum, script.Checksum, StringComparison.Ordinal))
            {
                throw new MigrationTamperException(id, recordedChecksum, script.Checksum, script.Path);
            }

            appliedOnDisk.Add(script);
        }

        foreach (MigrationScript script in Catalog.Scripts)
        {
            if (!applied.ContainsKey(script.Id))
                pending.Add(script);
        }

        return new MigrationPlan(appliedOnDisk, pending, unverified);
    }

    /// <summary>
    /// Applies pending migrations. Validation happens before anything is written, so a tampered or
    /// missing applied migration aborts the whole run. Each migration is transactional in PostgreSQL;
    /// a failure leaves it unrecorded and a later retry applies it again.
    /// </summary>
    public MigrationPlan Apply(IMigrationDatabase database)
    {
        using IDisposable _ = database.AcquireLock();
        // Validate applied history before even creating/altering schema_migrations: a tampered
        // or missing applied file must abort without touching the database.
        MigrationPlan plan = BuildPlan(database.ReadAppliedChecksums());
        database.EnsureHistoryTable();
        foreach (MigrationScript script in plan.Pending)
            database.Apply(script);
        return plan;
    }

    /// <summary>
    /// Explicitly records the current file checksums for rows applied before checksum tracking.
    /// This is not called by <see cref="Apply"/>: backfilling silently would hide an edited file.
    /// </summary>
    public int AdoptChecksums(IMigrationDatabase database)
    {
        using IDisposable _ = database.AcquireLock();
        MigrationPlan plan = BuildPlan(database.ReadAppliedChecksums());
        if (plan.AppliedWithoutChecksum.Count == 0)
            return 0;

        var scriptsById = Catalog.Scripts.ToDictionary(script => script.Id, StringComparer.Ordinal);
        var checksums = plan.AppliedWithoutChecksum.ToDictionary(
            id => id,
            id => scriptsById[id].Checksum,
            StringComparer.Ordinal);
        database.AdoptChecksums(checksums);
        return checksums.Count;
    }

    /// <summary>Warnings that should be printed but do not block boot (legacy rows without checksums).</summary>
    public IReadOnlyList<string> Warnings(MigrationPlan plan) =>
        plan.AppliedWithoutChecksum
            .Select(id =>
                $"migration '{id}' was applied before checksum tracking; run 'Fineage.Login migrate --adopt-checksums' " +
                "to enable tamper detection for it (this is never done automatically)")
            .ToList();
}

/// <summary>PostgreSQL-backed <see cref="IMigrationDatabase"/>.</summary>
public sealed class NpgsqlMigrationDatabase : IMigrationDatabase
{
    /// <summary>Stable advisory lock shared by Login and Zone so only one process migrates at a time.</summary>
    public const long AdvisoryLockKey = 87201401;

    readonly NpgsqlConnection _connection;

    public NpgsqlMigrationDatabase(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    public bool HistoryTableExists() => ExecuteBool("SELECT to_regclass('schema_migrations') IS NOT NULL");

    public IReadOnlyDictionary<string, string?> ReadAppliedChecksums()
    {
        if (!HistoryTableExists())
            return new Dictionary<string, string?>(StringComparer.Ordinal);

        bool hasChecksum = ExecuteBool(
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'schema_migrations'
                  AND column_name = 'checksum'
            )
            """);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = hasChecksum
            ? "SELECT id, checksum FROM schema_migrations ORDER BY id"
            : "SELECT id, NULL::text FROM schema_migrations ORDER BY id";
        var applied = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            applied[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        return applied;
    }

    public void EnsureHistoryTable()
    {
        using (var create = _connection.CreateCommand())
        {
            create.CommandText =
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    id text PRIMARY KEY,
                    applied_at timestamptz NOT NULL DEFAULT now(),
                    checksum text
                );
                """;
            create.ExecuteNonQuery();
        }

        // Upgrade compatibility for databases created by the pre-checksum runner.
        using (var alter = _connection.CreateCommand())
        {
            alter.CommandText = "ALTER TABLE schema_migrations ADD COLUMN IF NOT EXISTS checksum text";
            alter.ExecuteNonQuery();
        }
    }

    public void Apply(MigrationScript script)
    {
        using var tx = _connection.BeginTransaction();
        foreach (string statement in MigrationSql.SplitStatements(script.Sql))
        {
            using var apply = _connection.CreateCommand();
            apply.Transaction = tx;
            apply.CommandText = statement;
            apply.ExecuteNonQuery();
        }

        using (var mark = _connection.CreateCommand())
        {
            mark.Transaction = tx;
            mark.CommandText =
                """
                INSERT INTO schema_migrations (id, checksum) VALUES (@id, @checksum)
                ON CONFLICT (id) DO NOTHING
                """;
            mark.Parameters.AddWithValue("id", script.Id);
            mark.Parameters.AddWithValue("checksum", script.Checksum);
            mark.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void AdoptChecksums(IReadOnlyDictionary<string, string> checksums)
    {
        using var tx = _connection.BeginTransaction();
        foreach ((string id, string checksum) in checksums)
        {
            using var update = _connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE schema_migrations SET checksum = @checksum WHERE id = @id AND checksum IS NULL";
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("checksum", checksum);
            update.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public IDisposable AcquireLock()
    {
        using (var lockCmd = _connection.CreateCommand())
        {
            lockCmd.CommandText = $"SELECT pg_advisory_lock({AdvisoryLockKey})";
            lockCmd.ExecuteNonQuery();
        }

        return new AdvisoryLock(_connection);
    }

    bool ExecuteBool(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() is true;
    }

    sealed class AdvisoryLock : IDisposable
    {
        readonly NpgsqlConnection _connection;

        internal AdvisoryLock(NpgsqlConnection connection) => _connection = connection;

        public void Dispose()
        {
            using var unlock = _connection.CreateCommand();
            unlock.CommandText = $"SELECT pg_advisory_unlock({AdvisoryLockKey})";
            unlock.ExecuteNonQuery();
        }
    }
}

/// <summary>Statement splitter shared by the runner; keeps semicolons in comments, strings and dollar blocks intact.</summary>
internal static class MigrationSql
{
    public static IEnumerable<string> SplitStatements(string sql)
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

        string text = withoutComments.ToString();
        var current = new StringBuilder();
        bool inSingleQuote = false;
        string? dollarTag = null;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (dollarTag != null)
            {
                if (c == '$' && TryReadDollarDelimiter(text, i, out string closeTag, out int closeEnd)
                    && closeTag == dollarTag)
                {
                    current.Append(text, i, closeEnd - i + 1);
                    i = closeEnd;
                    dollarTag = null;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (inSingleQuote)
            {
                current.Append(c);
                if (c == '\'')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\'')
                    {
                        current.Append(text[++i]);
                    }
                    else
                    {
                        inSingleQuote = false;
                    }
                }

                continue;
            }

            if (c == '\'')
            {
                inSingleQuote = true;
                current.Append(c);
            }
            else if (c == '$' && TryReadDollarDelimiter(text, i, out string openTag, out int openEnd))
            {
                dollarTag = openTag;
                current.Append(text, i, openEnd - i + 1);
                i = openEnd;
            }
            else if (c == ';')
            {
                string statement = current.ToString().Trim();
                if (statement.Length > 0)
                    yield return statement;
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        string tail = current.ToString().Trim();
        if (tail.Length > 0)
            yield return tail;
    }

    static bool TryReadDollarDelimiter(string text, int start, out string tag, out int end)
    {
        tag = "";
        end = start;
        if (start >= text.Length || text[start] != '$')
            return false;

        int j = start + 1;
        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_'))
            j++;

        if (j >= text.Length || text[j] != '$')
            return false;

        tag = text.Substring(start + 1, j - start - 1);
        end = j;
        return true;
    }
}
