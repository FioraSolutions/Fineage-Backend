using System.Net;
using System.Net.Http;
using System.Text.Json;
using Fineage.Accounts;
using Fineage.Protocol.Health;
using Npgsql;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>#989: Login liveness vs readiness.</summary>
public sealed class LoginHealthTests
{
    [Fact]
    public async Task Readiness_is_503_and_status_not_ok_while_the_database_check_fails()
    {
        bool databaseUp = false;
        var board = new HealthBoard();
        board.AddReadyProbe("database", () => new HealthCheck("database", databaseUp, databaseUp ? "reachable" : "no connection within 3s"));
        await using var host = await LoginHttpHost.StartAsync(
            new LoginService(MemoryAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret)),
            IPAddress.Loopback,
            0,
            zoneStatus: UnavailableZoneStatus.Instance,
            health: board);
        using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl + "/") };

        using (var live = await client.GetAsync("/health/live"))
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using (var ready = await client.GetAsync("/health/ready"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            using var doc = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            var database = doc.RootElement.GetProperty("checks").EnumerateArray().Single(check => check.GetProperty("name").GetString() == "database");
            Assert.Contains("no connection", database.GetProperty("detail").GetString());
        }

        using (var status = await client.GetAsync("/api/status"))
        {
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("login").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("database").GetBoolean());
        }

        databaseUp = true;
        using (var ready = await client.GetAsync("/health/ready"))
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public void Database_probe_names_an_unreachable_database()
    {
        // Port 1 on loopback refuses at once: the probe must fail with the reason, never pass or hang.
        using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=x;Password=x;Database=x;Timeout=2");
        var check = PostgresAccountStore.CheckDatabase(dataSource, MigrationRunner.CreateDefault(), TimeSpan.FromSeconds(3));
        Assert.False(check.Ok);
        Assert.Equal("database", check.Name);
        Assert.False(string.IsNullOrWhiteSpace(check.Detail));
    }

    [Fact]
    public void Memory_store_readiness_has_no_external_database()
    {
        var report = Cli.LoginHealth(MemoryAccountStore.InMemory()).Readiness();
        Assert.True(report.Ok);
        Assert.Contains(report.Checks, check => check.Name == "database" && check.Ok);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Migrated_postgres_is_ready()
    {
        string url = AccountStoreFactory.ResolveConnectionString();
        try
        {
            using var probe = new NpgsqlConnection(url);
            probe.Open();
        }
        catch
        {
            throw Xunit.Sdk.SkipException.ForSkip("PostgreSQL is not reachable. Start `docker compose up -d postgres` or set FINEAGE_DATABASE_URL.");
        }

        string schema = "health_" + Guid.NewGuid().ToString("n")[..8];
        using (var conn = new NpgsqlConnection(url))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE SCHEMA {schema}";
            cmd.ExecuteNonQuery();
        }

        try
        {
            string scoped = new NpgsqlConnectionStringBuilder(url) { SearchPath = schema }.ConnectionString;
            using var store = PostgresAccountStore.Open(scoped);
            var check = store.CheckDatabase(TimeSpan.FromSeconds(5));
            Assert.True(check.Ok, check.Detail);
            Assert.True(Cli.LoginHealth(store).Readiness().Ok);
        }
        finally
        {
            using var conn = new NpgsqlConnection(url);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP SCHEMA {schema} CASCADE";
            cmd.ExecuteNonQuery();
        }
    }
}
