using Fineage.Game.Economy;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>P1-6: surface ledger alignment adjustments in logs and metrics.</summary>
public sealed partial class ZoneWorld
{
    static ZoneWorld() => WireLedgerAdjustmentTelemetry();

    static void WireLedgerAdjustmentTelemetry()
    {
        LedgerAdjustments.Adjusted += notice =>
        {
            foreach (var posting in notice.Postings)
            {
                if (posting.Amount == 0)
                    continue;
                ZoneLog.Warn(
                    $"ledger adjustment move={notice.PendingMoveKind} account={notice.Account} item={posting.ItemId} amount={posting.Amount}");
                FineageTelemetry.LedgerAdjustments.Add(
                    1,
                    new KeyValuePair<string, object?>("move_kind", notice.PendingMoveKind.ToString()));
            }
        };
    }
}