using Fineage.Accounts;
using Npgsql;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>
/// Real PostgreSQL coverage for the checksum history and pre-checksum upgrade path. Runs only when
/// a database is reachable; the fast migration suite uses fakes and stays database-free.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MigrationRunnerPostgresTests
{
    [Fact]
    public void Plan_is_read_only_checksums_are_recorded_and_legacy_rows_are_adopted_explicitly()
    {
        string? baseUrl = ConnectionString();
        if (baseUrl == null)
            return;

        string schema = "migrations_" + Guid.NewGuid().ToString("n")[..8];
        Execute(baseUrl, $"CREATE SCHEMA {schema}");
        string url = new NpgsqlConnectionStringBuilder(baseUrl) { SearchPath = schema }.ConnectionString;
        string directory = Path.Combine(Path.GetTempPath(), "fineage-pg-migrations-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "001_first.sql"), "CREATE TABLE first (id int);");
        File.WriteAllText(Path.Combine(directory, "002_second.sql"), "CREATE TABLE second (id int);");
        try
        {
            MigrationCatalog catalog = MigrationCatalog.Load(directory);
            var runner = new MigrationRunner(catalog);
            using var conn = new NpgsqlConnection(url);
            conn.Open();
            var database = new NpgsqlMigrationDatabase(conn);

            MigrationPlan plan = runner.Plan(database);
            Assert.Equal(2, plan.Pending.Count);
            Assert.False(database.HistoryTableExists(), "a plan must not create schema_migrations");

            runner.Apply(database);
            Assert.Equal(2, CountRows(conn, "SELECT count(*) FROM schema_migrations"));
            Assert.Equal(catalog.Scripts[0].Checksum, ScalarText(conn,
                "SELECT checksum FROM schema_migrations WHERE id = '001_first.sql'"));

            // Simulate a database migrated by the pre-checksum runner.
            Execute(url, "UPDATE schema_migrations SET checksum = NULL WHERE id = '001_first.sql'");
            MigrationPlan legacy = runner.Plan(database);
            Assert.Equal(["001_first.sql"], legacy.AppliedWithoutChecksum);
            runner.Apply(database);
            Assert.Null(ScalarText(conn, "SELECT checksum FROM schema_migrations WHERE id = '001_first.sql'"));

            Assert.Equal(1, runner.AdoptChecksums(database));
            Assert.Equal(catalog.Scripts[0].Checksum, ScalarText(conn,
                "SELECT checksum FROM schema_migrations WHERE id = '001_first.sql'"));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup.
            }

            Execute(baseUrl, $"DROP SCHEMA {schema} CASCADE");
        }
    }

    static string? ConnectionString()
    {
        string? url = Environment.GetEnvironmentVariable("FINEAGE_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(url))
            url = AccountStoreFactory.DefaultPostgresUrl;
        try
        {
            using var conn = new NpgsqlConnection(url);
            conn.Open();
            return url;
        }
        catch
        {
            return null;
        }
    }

    static void Execute(string url, string sql)
    {
        using var conn = new NpgsqlConnection(url);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static int CountRows(NpgsqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static string? ScalarText(NpgsqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }
}
