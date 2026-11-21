using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>P1-9/P1-10: durable journal flush with failure logging and shutdown drain.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>Journal first, then inventory and warehouse snapshots (<see cref="FlushDirtyInventories"/>).</summary>
    public void FlushLedgerOnShutdown()
    {
        if (_dirtyInventories.Count > 0 || _dirtyWarehouses.Count > 0)
            FlushDirtyInventories();
        else
            TryFlushLedger();
    }

    internal void TryFlushLedger()
    {
        int pending = _ledger.PendingFlushCount;
        try
        {
            _ledger.Flush();
        }
        catch (Exception ex)
        {
            ZoneLog.Warn($"ledger flush failed pending={pending}: {ex.Message}");
            FineageTelemetry.LedgerFlushFailures.Add(1, new KeyValuePair<string, object?>("pending", pending));
        }
    }
}