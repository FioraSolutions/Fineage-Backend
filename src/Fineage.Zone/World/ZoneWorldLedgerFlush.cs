using System.Diagnostics;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>P1-9/P1-10: durable journal flush with failure logging and shutdown drain.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>Journal first, then inventory and warehouse snapshots (<see cref="FlushDirtyInventories"/>).</summary>
    public void FlushLedgerOnShutdown()
    {
        if (_dirtyInventories.Count > 0 || _dirtyWarehouses.Count > 0 || _dirtyFreight.Count > 0)
            FlushDirtyInventories();
        else
            TryFlushLedger();
    }

    /// <returns>False when the auditable economy ledger flush failed and durable inventory must not proceed.</returns>
    internal bool TryFlushLedger()
    {
        int pending = _ledger.PendingFlushCount;
        if (pending == 0)
            return true;
        long started = Stopwatch.GetTimestamp();
        try
        {
            _ledger.Flush();
            return true;
        }
        catch (Exception ex)
        {
            ZoneLog.Warn(
                $"auditable economy ledger flush failed pending={pending}: {ex.GetType().Name}: {ex.Message}");
            FineageTelemetry.LedgerFlushFailures.Add(1, new KeyValuePair<string, object?>("pending", pending));
            return false;
        }
        finally
        {
            _syncIo.Record("ILedgerJournal.Flush", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}