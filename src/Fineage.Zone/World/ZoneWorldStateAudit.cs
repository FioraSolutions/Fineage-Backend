using Fineage.Zone.Persistence;

namespace Fineage.Zone.World;

/// <summary>#984: the startup check of the store against the data catalogs and the replayed ledger.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>
    /// Runs <see cref="StoredStateAudit"/> over the configured store and this world's replayed ledger. Call before the
    /// simulation starts: it reads every saved character at once and must see no in-flight economy commit.
    /// </summary>
    public StoredStateAuditReport AuditStoredState()
    {
        if (_accounts == null)
            throw new InvalidOperationException("stored-state audit needs an IAccountStore; this world has none");
        return StoredStateAudit.Run(_accounts, _ledger, _game, _henna);
    }

    /// <summary>Audit, log every finding, and with <c>FINEAGE_STATE_AUDIT=block</c> refuse to start on an error.</summary>
    public StoredStateAuditReport AuditStoredStateAtStartup(Func<string, string?>? environment = null)
    {
        bool block = StoredStateAudit.BlockOnError(environment);
        var report = AuditStoredState();
        StoredStateAudit.Apply(report, block);
        return report;
    }
}
