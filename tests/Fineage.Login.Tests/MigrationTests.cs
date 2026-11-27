using Fineage.Accounts;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>
/// CI prefix guard and catalog invariants. These run without PostgreSQL and cover the real shipped
/// migration set plus temporary sets for the duplicate/naming rules.
/// </summary>
public sealed class MigrationCatalogTests
{
    [Fact]
    public void Shipped_migrations_load_and_keep_the_legacy_018_and_020_collisions()
    {
        string directory = MigrationCatalog.FindApplicationMigrationsDirectory();
        MigrationCatalog catalog = MigrationCatalog.Load(directory);

        Assert.Contains(catalog.Scripts, script => script.Id == "018_character_freight.sql");
        Assert.Contains(catalog.Scripts, script => script.Id == "018_wedding_couples.sql");
        Assert.Contains(catalog.Scripts, script => script.Id == "020_account_language.sql");
        Assert.Contains(catalog.Scripts, script => script.Id == "020_augmentation_options.sql");
        Assert.Contains(catalog.Scripts, script => script.Id == "021_cursed_weapons.sql");
        Assert.All(catalog.Scripts, script => Assert.StartsWith("sha256:", script.Checksum, StringComparison.Ordinal));
    }

    [Fact]
    public void A_new_duplicate_prefix_is_rejected()
    {
        using var temp = new TempMigrationSet(
            ("022_first.sql", "CREATE TABLE first (id int);"),
            ("022_second.sql", "CREATE TABLE second (id int);"));

        MigrationException ex = Assert.Throws<MigrationException>(() => MigrationCatalog.Load(temp.Directory));
        Assert.Contains("022_first.sql", ex.Message, StringComparison.Ordinal);
        Assert.Contains("022_second.sql", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_allowlisted_legacy_duplicates_are_accepted()
    {
        using var temp = new TempMigrationSet(
            ("018_a.sql", "SELECT 1;"),
            ("018_b.sql", "SELECT 2;"),
            ("019_c.sql", "SELECT 3;"),
            ("020_d.sql", "SELECT 4;"),
            ("020_e.sql", "SELECT 5;"));

        MigrationCatalog catalog = MigrationCatalog.Load(temp.Directory);
        Assert.Equal(5, catalog.Scripts.Count);
    }

    [Theory]
    [InlineData("22_short_prefix.sql")]
    [InlineData("022-Upper.sql")]
    [InlineData("022_no_extension")]
    public void Invalid_migration_names_are_rejected(string name)
    {
        using var temp = new TempMigrationSet((name, "SELECT 1;"));
        Assert.Throws<MigrationException>(() => MigrationCatalog.Load(temp.Directory));
    }

    [Fact]
    public void Checksums_ignore_crlf_and_bom_differences()
    {
        Assert.Equal(
            MigrationScript.ComputeChecksum("SELECT 1;\n"),
            MigrationScript.ComputeChecksum("\uFEFFSELECT 1;\r\n"));
    }

    sealed class TempMigrationSet : IDisposable
    {
        public TempMigrationSet(params (string Name, string Sql)[] files)
        {
            Directory = Path.Combine(Path.GetTempPath(), "fineage-migrations-" + Guid.NewGuid().ToString("n"));
            System.IO.Directory.CreateDirectory(Directory);
            foreach ((string name, string sql) in files)
                File.WriteAllText(Path.Combine(Directory, name), sql);
        }

        public string Directory { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temp directory must not turn a migration assertion into a test failure.
            }
        }
    }
}

/// <summary>
/// Runner behavior without PostgreSQL: fresh database, already-migrated database, transactional
/// failure retry, tamper detection and explicit checksum adoption for pre-checksum rows.
/// </summary>
public sealed class MigrationRunnerTests
{
    [Fact]
    public void Plan_is_read_only_on_a_fresh_database()
    {
        using var temp = new TempMigrationSet(
            ("001_a.sql", "CREATE TABLE a (id int);"),
            ("002_b.sql", "CREATE TABLE b (id int);"));
        var runner = new MigrationRunner(MigrationCatalog.Load(temp.Directory));
        var database = new FakeMigrationDatabase();

        MigrationPlan plan = runner.Plan(database);

        Assert.Equal(["001_a.sql", "002_b.sql"], plan.Pending.Select(script => script.Id));
        Assert.Empty(plan.Applied);
        Assert.False(plan.IsUpToDate);
        Assert.Equal(0, database.EnsureCalls);
        Assert.Equal(0, database.LockCalls);
        Assert.Empty(database.Executed);
    }

    [Fact]
    public void Fresh_database_applies_every_migration_in_order_and_records_checksums()
    {
        using var temp = new TempMigrationSet(
            ("001_a.sql", "CREATE TABLE a (id int);"),
            ("002_b.sql", "CREATE TABLE b (id int);"),
            ("003_c.sql", "CREATE TABLE c (id int);"));
        MigrationCatalog catalog = MigrationCatalog.Load(temp.Directory);
        var runner = new MigrationRunner(catalog);
        var database = new FakeMigrationDatabase();

        MigrationPlan plan = runner.Apply(database);

        Assert.Equal(3, plan.Pending.Count);
        Assert.Equal(["001_a.sql", "002_b.sql", "003_c.sql"], database.Executed);
        Assert.Equal(catalog.Scripts.Count, database.Applied.Count);
        Assert.All(catalog.Scripts, script => Assert.Equal(script.Checksum, database.Applied[script.Id]));
        Assert.Equal(1, database.EnsureCalls);
        Assert.Equal(1, database.LockCalls);
    }

    [Fact]
    public void Existing_database_only_applies_pending_migrations()
    {
        using var temp = new TempMigrationSet(
            ("001_a.sql", "CREATE TABLE a (id int);"),
            ("002_b.sql", "CREATE TABLE b (id int);"));
        MigrationCatalog catalog = MigrationCatalog.Load(temp.Directory);
        var runner = new MigrationRunner(catalog);
        var database = new FakeMigrationDatabase();
        database.Applied["001_a.sql"] = catalog.Scripts[0].Checksum;

        MigrationPlan plan = runner.Apply(database);

        Assert.Equal(["002_b.sql"], plan.Pending.Select(script => script.Id));
        Assert.Equal(["002_b.sql"], database.Executed);
        Assert.Single(plan.Applied);
    }

    [Fact]
    public void Failed_migration_is_not_recorded_and_a_retry_applies_it()
    {
        using var temp = new TempMigrationSet(
            ("001_a.sql", "CREATE TABLE a (id int);"),
            ("002_b.sql", "CREATE TABLE b (id int);"),
            ("003_c.sql", "CREATE TABLE c (id int);"));
        var runner = new MigrationRunner(MigrationCatalog.Load(temp.Directory));
        var database = new FakeMigrationDatabase { FailOnId = "002_b.sql" };

        Assert.Throws<InvalidOperationException>(() => runner.Apply(database));
        Assert.Equal(["001_a.sql"], database.Executed);
        Assert.True(database.Applied.ContainsKey("001_a.sql"));
        Assert.False(database.Applied.ContainsKey("002_b.sql"));
        Assert.False(database.Applied.ContainsKey("003_c.sql"));

        database.FailOnId = null;
        runner.Apply(database);

        Assert.Equal(["001_a.sql", "002_b.sql", "003_c.sql"], database.Executed);
        Assert.Equal(3, database.Applied.Count);
    }

    [Fact]
    public void Tampered_applied_migration_aborts_before_any_write()
    {
        using var temp = new TempMigrationSet(
            ("001_a.sql", "CREATE TABLE a (id int);"),
            ("002_b.sql", "CREATE TABLE b (id int);"));
        var runner = new MigrationRunner(MigrationCatalog.Load(temp.Directory));
        var database = new FakeMigrationDatabase
        {
            Applied = { ["001_a.sql"] = "sha256:0000000000000000000000000000000000000000000000000000000000000000" }
        };

        MigrationTamperException ex = Assert.Throws<MigrationTamperException>(() => runner.Apply(database));

        Assert.Equal("001_a.sql", ex.MigrationId);
        Assert.Contains("forward-only", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, database.EnsureCalls);
        Assert.Empty(database.Executed);
    }

    [Fact]
    public void Applied_migration_missing_from_disk_is_an_explicit_error()
    {
        using var temp = new TempMigrationSet(("001_a.sql", "CREATE TABLE a (id int);"));
        var runner = new MigrationRunner(MigrationCatalog.Load(temp.Directory));
        var database = new FakeMigrationDatabase { Applied = { ["999_deleted.sql"] = null } };

        MigrationException ex = Assert.Throws<MigrationException>(() => runner.Plan(database));

        Assert.Contains("999_deleted.sql", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Forward-only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_row_without_checksum_warns_and_is_never_backfilled_implicitly()
    {
        using var temp = new TempMigrationSet(("001_a.sql", "CREATE TABLE a (id int);"));
        MigrationCatalog catalog = MigrationCatalog.Load(temp.Directory);
        var runner = new MigrationRunner(catalog);
        var database = new FakeMigrationDatabase { Applied = { ["001_a.sql"] = null } };

        MigrationPlan plan = runner.Plan(database);

        Assert.Single(plan.AppliedWithoutChecksum);
        string warning = Assert.Single(runner.Warnings(plan));
        Assert.Contains("--adopt-checksums", warning, StringComparison.Ordinal);
        Assert.Null(database.Applied["001_a.sql"]);

        runner.Apply(database);
        Assert.Null(database.Applied["001_a.sql"]);
        Assert.Empty(database.Executed);
    }

    [Fact]
    public void AdoptChecksums_is_explicit_and_idempotent()
    {
        using var temp = new TempMigrationSet(
            ("001_a.sql", "CREATE TABLE a (id int);"),
            ("002_b.sql", "CREATE TABLE b (id int);"));
        MigrationCatalog catalog = MigrationCatalog.Load(temp.Directory);
        var runner = new MigrationRunner(catalog);
        var database = new FakeMigrationDatabase
        {
            Applied = { ["001_a.sql"] = null, ["002_b.sql"] = null }
        };

        Assert.Equal(2, runner.AdoptChecksums(database));
        Assert.Equal(catalog.Scripts[0].Checksum, database.Applied["001_a.sql"]);
        Assert.Equal(catalog.Scripts[1].Checksum, database.Applied["002_b.sql"]);
        Assert.Equal(0, runner.AdoptChecksums(database));
        Assert.Equal(2, database.LockCalls);
    }

    sealed class FakeMigrationDatabase : IMigrationDatabase
    {
        public Dictionary<string, string?> Applied { get; } = new(StringComparer.Ordinal);

        public List<string> Executed { get; } = [];

        public int EnsureCalls { get; private set; }

        public int LockCalls { get; private set; }

        public string? FailOnId { get; set; }

        public bool HistoryTableExists() => Applied.Count > 0 || EnsureCalls > 0;

        public IReadOnlyDictionary<string, string?> ReadAppliedChecksums() =>
            new Dictionary<string, string?>(Applied, StringComparer.Ordinal);

        public void EnsureHistoryTable()
        {
            EnsureCalls++;
        }

        public void Apply(MigrationScript script)
        {
            if (script.Id == FailOnId)
                throw new InvalidOperationException("simulated migration failure: " + script.Id);

            Executed.Add(script.Id);
            Applied[script.Id] = script.Checksum;
        }

        public void AdoptChecksums(IReadOnlyDictionary<string, string> checksums)
        {
            foreach ((string id, string checksum) in checksums)
            {
                Assert.True(Applied.TryGetValue(id, out string? current) && current == null);
                Applied[id] = checksum;
            }
        }

        public IDisposable AcquireLock()
        {
            LockCalls++;
            return new NoopDisposable();
        }

        sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    sealed class TempMigrationSet : IDisposable
    {
        public TempMigrationSet(params (string Name, string Sql)[] files)
        {
            Directory = Path.Combine(Path.GetTempPath(), "fineage-migrations-" + Guid.NewGuid().ToString("n"));
            System.IO.Directory.CreateDirectory(Directory);
            foreach ((string name, string sql) in files)
                File.WriteAllText(Path.Combine(Directory, name), sql);
        }

        public string Directory { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // Temp cleanup is best-effort; it must not mask an assertion.
            }
        }
    }
}
