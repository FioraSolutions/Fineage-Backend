using Fineage.Accounts;
using Fineage.Protocol.Health;

namespace Fineage.Zone.Health;

/// <summary>
/// Zone readiness after boot: PostgreSQL/migrations/ledger, required catalogs, spawns, sidecars, handoff
/// secret, simulation, and the public Net listener. Every field starts false and is set only by the
/// corresponding startup stage; there is no default-ready state.
/// </summary>
public sealed class ZoneReadinessState
{
    readonly PostgresAccountStore _accounts;
    readonly Func<bool> _netListenerReady;
    volatile bool _catalogsLoaded;
    volatile bool _sidecarsLoaded;
    volatile bool _handoffReady;
    volatile bool _worldLoaded;
    volatile bool _simulationStarted;
    volatile int _spawnCount;

    public ZoneReadinessState(PostgresAccountStore accounts, Func<bool> netListenerReady)
    {
        _accounts = accounts;
        _netListenerReady = netListenerReady;
    }

    public void MarkCatalogsLoaded() => _catalogsLoaded = true;

    public void MarkSidecarsLoaded() => _sidecarsLoaded = true;

    public void MarkHandoffReady() => _handoffReady = true;

    public void MarkWorldLoaded(int spawnCount)
    {
        _spawnCount = spawnCount;
        _worldLoaded = true;
    }

    public void MarkSimulationStarted() => _simulationStarted = true;

    public async ValueTask<ReadinessReport> ReadAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<ReadinessCheck>();
        DatabaseReadiness db = await _accounts.CheckReadinessAsync(cancellationToken).ConfigureAwait(false);
        checks.Add(new ReadinessCheck("database", db.Reachable, db.Error));
        checks.Add(new ReadinessCheck("migrations", db.MigrationsCurrent, db.MigrationError));
        checks.Add(new ReadinessCheck("ledger", db.LedgerReady, db.LedgerError));
        checks.Add(new ReadinessCheck("catalogs", _catalogsLoaded,
            _catalogsLoaded ? null : "required data catalogs were not validated"));
        checks.Add(new ReadinessCheck("world", _worldLoaded,
            _worldLoaded ? null : "world was not loaded"));
        int spawns = _spawnCount;
        checks.Add(new ReadinessCheck("spawns", spawns > 0,
            spawns > 0 ? $"{spawns} NPC spawns loaded" : "world loaded 0 NPC spawns; spawn catalogs or maps are missing"));
        checks.Add(new ReadinessCheck("sidecars", _sidecarsLoaded,
            _sidecarsLoaded ? null : "world state sidecars were not opened"));
        checks.Add(new ReadinessCheck("handoff", _handoffReady,
            _handoffReady ? null : "handoff secret was not validated at startup"));
        checks.Add(new ReadinessCheck("simulation", _simulationStarted,
            _simulationStarted ? null : "zone simulation was not started"));
        bool listener = _netListenerReady();
        checks.Add(new ReadinessCheck("net_listener", listener,
            listener ? null : "zone Net listener is not bound"));
        return ReadinessReport.From(checks);
    }
}
