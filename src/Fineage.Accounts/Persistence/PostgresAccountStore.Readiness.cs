using Npgsql;

namespace Fineage.Accounts;

public sealed partial class PostgresAccountStore
{
    static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Real readiness probe: opens a connection, executes a query, validates the migration history,
    /// and checks that the ledger table exists. Any failure returns a concrete error; there is no
    /// in-memory fallback because Login and Zone require PostgreSQL in production.
    /// </summary>
    public async ValueTask<DatabaseReadiness> CheckReadinessAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReadinessTimeout);
        try
        {
            await using NpgsqlConnection conn = await _dataSource.OpenConnectionAsync(timeout.Token).ConfigureAwait(false);
            await using (NpgsqlCommand ping = conn.CreateCommand())
            {
                ping.CommandText = "SELECT 1";
                ping.CommandTimeout = (int)ReadinessTimeout.TotalSeconds;
                await ping.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false);
            }

            MigrationRunner runner = MigrationRunner.CreateDefault();
            MigrationPlan plan = runner.Plan(new NpgsqlMigrationDatabase(conn));
            string? migrationError = plan.Pending.Count == 0
                ? null
                : $"database has {plan.Pending.Count} pending migration(s): {string.Join(", ", plan.Pending.Select(script => script.Id))}";

            bool ledgerReady;
            string? ledgerError = null;
            try
            {
                await using NpgsqlCommand ledger = conn.CreateCommand();
                ledger.CommandText = "SELECT to_regclass('public.economy_ledger') IS NOT NULL";
                ledger.CommandTimeout = (int)ReadinessTimeout.TotalSeconds;
                ledgerReady = await ledger.ExecuteScalarAsync(timeout.Token) is true;
                if (!ledgerReady)
                    ledgerError = "economy_ledger table is missing";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ledgerReady = false;
                ledgerError = $"economy_ledger probe failed: {ex.Message}";
            }

            return new DatabaseReadiness(
                Reachable: true,
                MigrationsCurrent: plan.Pending.Count == 0,
                AppliedMigrations: plan.Applied.Count,
                PendingMigrations: plan.Pending.Select(script => script.Id).ToArray(),
                LedgerReady: ledgerReady,
                Error: null,
                MigrationError: migrationError,
                LedgerError: ledgerError);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unreachable($"PostgreSQL readiness probe timed out after {ReadinessTimeout.TotalSeconds:0}s");
        }
        catch (Exception ex)
        {
            return Unreachable(ex.Message);
        }
    }

    static DatabaseReadiness Unreachable(string error) =>
        new(
            Reachable: false,
            MigrationsCurrent: false,
            AppliedMigrations: 0,
            PendingMigrations: [],
            LedgerReady: false,
            Error: error,
            MigrationError: "cannot verify migrations while PostgreSQL is unreachable",
            LedgerError: "cannot verify the economy ledger while PostgreSQL is unreachable");
}
