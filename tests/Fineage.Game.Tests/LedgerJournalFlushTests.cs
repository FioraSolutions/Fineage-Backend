using Fineage.Game.Economy;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>P1-9: failed durable flushes re-queue entries and record a metric.</summary>
public sealed class LedgerJournalFlushTests
{
    [Fact]
    public void Flush_failure_then_success_keeps_entries_and_records_metric()
    {
        var journal = new FlakyLedgerJournal(failuresBeforeSuccess: 1);
        var ledger = new EconomyLedger(journal, 100);
        LedgerAccount bag = LedgerAccount.Inventory(1);
        LedgerAccount shop = LedgerAccount.Of(SystemAccount.NpcShop);
        Assert.True(ledger.Submit(new LedgerTransaction(
            new LedgerTxId(10, 0),
            LedgerTxKind.ShopBuy,
            1,
            0,
            100,
            [new(shop, 57, -10), new(bag, 57, 10)])).Ok);
        Assert.Equal(1, journal.PendingFlushCount);

        Assert.Throws<InvalidOperationException>(() => ledger.Flush());
        Assert.Equal(1, journal.PendingFlushCount);

        ledger.Flush();
        Assert.Equal(0, journal.PendingFlushCount);
        Assert.Single(journal.Flushed);
        Assert.Equal(10, ledger.HoldingsOf(bag).CountOf(57));
    }

    sealed class FlakyLedgerJournal(int failuresBeforeSuccess) : ILedgerJournal
    {
        readonly List<LedgerEntry> _buffered = [];
        int _failuresLeft = failuresBeforeSuccess;

        readonly List<LedgerEntry> _flushed = [];

        public IReadOnlyList<LedgerEntry> Flushed => _flushed;

        public IReadOnlyList<LedgerEntry> ReadAll() => _flushed;

        public void Append(LedgerEntry entry) => _buffered.Add(entry);

        public int PendingFlushCount => _buffered.Count;

        public void Flush()
        {
            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw new InvalidOperationException("simulated durable write failure");
            }

            _flushed.AddRange(_buffered);
            _buffered.Clear();
        }
    }
}