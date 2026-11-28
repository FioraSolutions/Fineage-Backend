using Fineage.Accounts;
using Fineage.Agent.Identity;
using Xunit;

namespace Fineage.Agent.Identity.Tests;

public sealed class AgentIdentityMigrationTests
{
    [Fact]
    public void Catalog_includes_024_agent_identity_as_the_next_unique_prefix()
    {
        string directory = MigrationCatalog.FindApplicationMigrationsDirectory();
        MigrationCatalog catalog = MigrationCatalog.Load(directory);
        MigrationScript script = Assert.Single(catalog.Scripts, item => item.Prefix == "024");
        Assert.Equal("024_agent_identity.sql", script.Id);
        Assert.Contains("agent_tenants", script.Sql, StringComparison.Ordinal);
        Assert.Contains("agent_principals", script.Sql, StringComparison.Ordinal);
        Assert.Contains("agent_agents", script.Sql, StringComparison.Ordinal);
        Assert.Contains("agent_character_bindings", script.Sql, StringComparison.Ordinal);
        Assert.Contains("agent_credentials", script.Sql, StringComparison.Ordinal);
        Assert.Contains("secret_hash", script.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("secret text", script.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS", script.Sql, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", script.Checksum, StringComparison.Ordinal);
    }
}
