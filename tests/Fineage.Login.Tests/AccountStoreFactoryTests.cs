using Fineage.Login;
using Xunit;
using Fineage.Accounts;

namespace Fineage.Login.Tests;

/// <summary>
/// Guards the PostgreSQL-only invariant. The file-backed JSON store is gone, so a regression that
/// reintroduces <c>--store</c> or a silent in-memory fallback must fail here instead of at a playtest.
/// </summary>
public class AccountStoreFactoryTests
{
    [Fact]
    public void ResolveConnectionString_PrefersArgument_ThenEnv_ThenDefault()
    {
        string? previous = Environment.GetEnvironmentVariable(AccountStoreFactory.DatabaseUrlEnv);
        try
        {
            Environment.SetEnvironmentVariable(AccountStoreFactory.DatabaseUrlEnv, "Host=env;Database=envdb");
            Assert.Equal("Host=explicit;Database=x", AccountStoreFactory.ResolveConnectionString("Host=explicit;Database=x"));
            Assert.Equal("Host=env;Database=envdb", AccountStoreFactory.ResolveConnectionString());
            Assert.Equal("Host=env;Database=envdb", AccountStoreFactory.ResolveConnectionString("  "));

            Environment.SetEnvironmentVariable(AccountStoreFactory.DatabaseUrlEnv, null);
            Assert.Equal(AccountStoreFactory.DefaultPostgresUrl, AccountStoreFactory.ResolveConnectionString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(AccountStoreFactory.DatabaseUrlEnv, previous);
        }
    }

    [Fact]
    public void UnreachableDatabase_FailsFastWithAnActionableMessage()
    {
        // Port 1 is never a Postgres listener; the failure must name Postgres and the env var.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AccountStoreFactory.Open("Host=127.0.0.1;Port=1;Database=fineage;Username=fineage;Password=secretpw;Timeout=1"));

        Assert.Contains("PostgreSQL", ex.Message, StringComparison.Ordinal);
        Assert.Contains(AccountStoreFactory.DatabaseUrlEnv, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secretpw", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_no_longer_accepts_store_and_reports_usage()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        // --store used to select a JSON file. It is no longer an option, so it is treated as a command.
        int code = await Cli.RunAsync(["--store", "data/accounts.json"], output, error);

        Assert.Equal(1, code);
        Assert.DoesNotContain("--store", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("FINEAGE_DATABASE_URL", error.ToString(), StringComparison.Ordinal);
    }
}
