using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Fault-injection harness for economy durability (architecture review §7).</summary>
public sealed class EconomyFaultInjectionTests
{
    const int Adena = WarehouseRules.AdenaItemId;

    [Fact]
    public async Task Commit_failure_restores_dirty_and_preserves_store()
    {
        var journal = new FlakyLedgerJournal(failuresBeforeSuccess: 1);
        var bench = new WorldBench(ledgerJournal: journal);
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 5, -1)]);
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var shop = LedgerAccount.Of(SystemAccount.NpcShop);
        Assert.True(bench.World.LedgerForTests.Submit(new LedgerTransaction(
            new LedgerTxId(1, 0),
            LedgerTxKind.Adjustment,
            0,
            0,
            0,
            [new LedgerPosting(shop, Adena, -1), new LedgerPosting(bag, Adena, 1)])).Ok);
        bench.World.StageDeferredInventoryForTest(player.Session, player.Inventory!);
        Assert.False(bench.World.FlushDirtyInventoriesForTests());
        Assert.True(bench.World.HasDeferredInventoryForTests(player.ObjectId));
        Assert.Equal(5, player.Saved().Sum(row => row.ItemId == Adena ? row.Count : 0));
        EconomyInvariantChecker.AssertHold(bench.World.LedgerForTests);
    }

    [Fact]
    public async Task After_successful_commit_world_reconciler_is_clean()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 100, -1)]);
        Assert.Null(await bench.World.TryGiveItemAsync(player.Session, Adena, 50, CancellationToken.None));
        Assert.True(bench.World.FlushDirtyInventoriesForTests());
        EconomyInvariantChecker.AssertHold(bench.World.LedgerForTests);
    }

    [Fact]
    public void Duplicate_ledger_tx_id_is_idempotent_not_double_applied()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 3_600_000);
        var bag = LedgerAccount.Inventory(42);
        var untracked = LedgerAccount.Of(SystemAccount.Untracked);
        var tx = new LedgerTransaction(
            new LedgerTxId(10, 0),
            LedgerTxKind.Adjustment,
            0,
            0,
            0,
            [
                new LedgerPosting(untracked, Adena, -5),
                new LedgerPosting(bag, Adena, 5),
            ]);
        Assert.Equal(LedgerOutcome.Committed, ledger.Submit(tx).Outcome);
        Assert.Equal(5, ledger.HoldingsOf(bag).CountOf(Adena));
        var again = ledger.Submit(tx);
        Assert.True(again.Ok);
        Assert.Equal(LedgerOutcome.Duplicate, again.Outcome);
        Assert.Equal(5, ledger.HoldingsOf(bag).CountOf(Adena));
    }

    sealed class FlakyLedgerJournal(int failuresBeforeSuccess) : MemoryLedgerJournal
    {
        int _failuresLeft = failuresBeforeSuccess;
        int _unflushed;

        public override void Append(LedgerEntry entry)
        {
            base.Append(entry);
            _unflushed++;
        }

        public override int PendingFlushCount => _unflushed;

        public override void Flush()
        {
            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw new InvalidOperationException("simulated auditable economy ledger flush failure");
            }

            _unflushed = 0;
        }
    }
}