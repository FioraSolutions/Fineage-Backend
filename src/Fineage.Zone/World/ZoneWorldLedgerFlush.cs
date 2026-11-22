namespace Fineage.Zone.World;

/// <summary>P1-9/P1-10: durable journal flush with failure logging and shutdown drain.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>One economy transaction for the pending ledger and every dirty bag (<see cref="FlushDirtyInventories"/>).</summary>
    public void FlushLedgerOnShutdown() => FlushDirtyInventories();

    /// <returns>False when the auditable economy commit failed. The same transaction writes the bags.</returns>
    internal bool TryFlushLedger() => FlushDirtyInventories();
}