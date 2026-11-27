using Fineage.Protocol.Health;

namespace Fineage.Accounts;

/// <summary>#989: the database readiness probe.</summary>
public sealed partial class PostgresAccountStore
{
    readonly Lazy<MigrationRunner> _healthRunner = new(MigrationRunner.CreateDefault);

    /// <summary>
    /// Opens a connection within <paramref name="timeout"/>, reads <c>schema_migrations</c> and validates it against
    /// this build's migration files (the same plan as <c>migrate --plan</c>, read-only). Ok only when the database
    /// answers and nothing is pending; a tampered or unknown applied migration is a failed check with its message.
    /// </summary>
    public HealthCheck CheckDatabase(TimeSpan timeout) => CheckDatabase(_dataSource, _healthRunner.Value, timeout);

    /// <summary>The same probe over any data source (tests point it at a dead port).</summary>
    public static HealthCheck CheckDatabase(Npgsql.NpgsqlDataSource dataSource, MigrationRunner runner, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(runner);
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            using var conn = dataSource.OpenConnectionAsync(cts.Token).AsTask().GetAwaiter().GetResult();
            var plan = runner.Plan(new NpgsqlMigrationDatabase(conn));
            return plan.IsUpToDate
                ? new HealthCheck("database", true, $"reachable, {plan.Applied.Count} migrations applied")
                : new HealthCheck("database", false,
                    $"reachable but {plan.Pending.Count} migration(s) pending: {string.Join(", ", plan.Pending.Select(script => script.Id))}");
        }
        catch (OperationCanceledException)
        {
            return new HealthCheck("database", false, $"no connection within {timeout.TotalSeconds:0.#}s");
        }
        catch (Exception ex)
        {
            return new HealthCheck("database", false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
