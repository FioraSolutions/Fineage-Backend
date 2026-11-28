using Fineage.Accounts;
using Xunit;

namespace Fineage.Agent.Operations.Tests;

public sealed class AgentOperationMigrationTests
{
    [Fact]
    public void Catalog_includes_025_agent_operations_as_the_next_unique_prefix()
    {
        string directory = MigrationCatalog.FindApplicationMigrationsDirectory();
        MigrationCatalog catalog = MigrationCatalog.Load(directory);
        MigrationScript script = Assert.Single(catalog.Scripts, item => item.Prefix == "025");
        Assert.Equal("025_agent_operations.sql", script.Id);
        Assert.Contains("agent_operations", script.Sql, StringComparison.Ordinal);
        Assert.Contains("idempotency_key", script.Sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (tenant_id, principal_id, idempotency_key)", script.Sql, StringComparison.Ordinal);
        Assert.Contains("ledger_commit_id", script.Sql, StringComparison.Ordinal);
        Assert.Contains("deadline", script.Sql, StringComparison.Ordinal);
        Assert.Contains("'Accepted'", script.Sql, StringComparison.Ordinal);
        Assert.Contains("'Expired'", script.Sql, StringComparison.Ordinal);
        Assert.Contains("'Unknown'", script.Sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS", script.Sql, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", script.Checksum, StringComparison.Ordinal);
        Assert.Contains(catalog.Scripts, item => item.Prefix == "024");
    }
}
