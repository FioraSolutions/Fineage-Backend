using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-6: ledger alignment adjustments are logged, metered, and optional strict.</summary>
public sealed class AdjustmentLedgerTests
{
    const int Adena = 57;

    [Fact]
    public void Alignment_triggers_adjustment_hook_and_metric()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 100);
        int notices = 0;
        // Adjusted is process-wide and other tests raise it in parallel; only this test's synchronous Observe counts.
        int thread = Environment.CurrentManagedThreadId;
        void Handler(LedgerAdjustmentNotice notice)
        {
            if (Environment.CurrentManagedThreadId != thread)
                return;
            notices++;
            Assert.Equal(LedgerTxKind.ShopBuy, notice.PendingMoveKind);
            Assert.Contains(notice.Postings, p => p.ItemId == Adena && p.Amount != 0);
            FineageTelemetry.LedgerAdjustments.Add(
                1,
                new KeyValuePair<string, object?>("move_kind", notice.PendingMoveKind.ToString()));
        }

        LedgerAdjustments.Adjusted += Handler;
        try
        {
            using var metrics = new MetricsCollector();
            var holdings = new LedgerHoldings().Add(Adena, 10);
            var move = new LedgerMove(ledger, () => new LedgerTxId(1, 0), LedgerTxKind.ShopBuy, 0, 7, 30_001);
            move.Observe(LedgerAccount.Inventory(7), holdings);

            Assert.Equal(1, notices);
            var snapshot = metrics.Snapshot();
            Assert.True(snapshot.LedgerAdjustments.GetValueOrDefault(nameof(LedgerTxKind.ShopBuy)) >= 1);
        }
        finally
        {
            LedgerAdjustments.Adjusted -= Handler;
        }
    }

    [Fact]
    public void Strict_mode_refuses_alignment_adjustment()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 100);
        var holdings = new LedgerHoldings().Add(Adena, 5);
        LedgerAdjustments.Strict = true;
        try
        {
            var move = new LedgerMove(ledger, () => new LedgerTxId(1, 0), LedgerTxKind.ShopBuy, 0, 9, Adena);
            Assert.Throws<LedgerReconciliationException>(() => move.Observe(LedgerAccount.Inventory(9), holdings));
        }
        finally
        {
            LedgerAdjustments.Strict = false;
        }
    }

    [Fact]
    public void Strict_mode_is_scoped_to_the_flow_that_set_it()
    {
        LedgerAdjustments.Strict = true;
        try
        {
            bool otherStrict = true;
            // A parallel test runs on its own flow: start without flowing this one's execution context.
            var other = new Thread(() => otherStrict = LedgerAdjustments.Strict);
            other.UnsafeStart();
            other.Join();
            Assert.False(otherStrict);
            Assert.True(LedgerAdjustments.Strict);
        }
        finally
        {
            LedgerAdjustments.Strict = false;
        }
    }
}
