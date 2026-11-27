using Fineage.Accounts;
using Fineage.Protocol.Health;

namespace Fineage.Login;

/// <summary>Where Login's public readiness endpoints get their checks.</summary>
public interface ILoginReadinessSource
{
    ValueTask<ReadinessReport> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Used only when a caller forgot to wire a probe. It is deliberately not ready.</summary>
public sealed class UnconfiguredLoginReadiness : ILoginReadinessSource
{
    public static UnconfiguredLoginReadiness Instance { get; } = new();

    UnconfiguredLoginReadiness()
    {
    }

    public ValueTask<ReadinessReport> ReadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(ReadinessReport.NotConfigured("login"));
}

/// <summary>
/// Login readiness: PostgreSQL reachable, migration history current, the ledger table present, the
/// Net login listener bound, and the handoff secret validated. The HTTP listener itself is implied by
/// the request being answered, but the Net listener is not.
/// </summary>
public sealed class LoginReadinessProbe : ILoginReadinessSource
{
    readonly IAccountStore _accounts;
    readonly Func<bool> _netListenerReady;
    readonly bool _handoffReady;

    public LoginReadinessProbe(IAccountStore accounts, Func<bool> netListenerReady, bool handoffReady)
    {
        _accounts = accounts;
        _netListenerReady = netListenerReady;
        _handoffReady = handoffReady;
    }

    public async ValueTask<ReadinessReport> ReadAsync(CancellationToken cancellationToken)
    {
        var checks = new List<ReadinessCheck>();
        if (_accounts is PostgresAccountStore postgres)
        {
            DatabaseReadiness db = await postgres.CheckReadinessAsync(cancellationToken).ConfigureAwait(false);
            checks.Add(new ReadinessCheck("database", db.Reachable, db.Error));
            checks.Add(new ReadinessCheck("migrations", db.MigrationsCurrent, db.MigrationError));
            checks.Add(new ReadinessCheck("ledger", db.LedgerReady, db.LedgerError));
        }
        else
        {
            string error = $"account store is {_accounts.GetType().Name}, not PostgreSQL";
            checks.Add(new ReadinessCheck("database", false, error));
            checks.Add(new ReadinessCheck("migrations", false, error));
            checks.Add(new ReadinessCheck("ledger", false, error));
        }

        bool listener = _netListenerReady();
        checks.Add(new ReadinessCheck("net_listener", listener, listener ? null : "login Net listener is not bound"));
        checks.Add(new ReadinessCheck(
            "handoff",
            _handoffReady,
            _handoffReady ? null : "handoff secret was not validated at startup"));
        return ReadinessReport.From(checks);
    }
}
