using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-9: shutdown drain and flush-failure metrics.</summary>
public sealed class JournalDurabilityTests
{
    [Fact]
    public async Task Shutdown_flush_drains_buffered_journal_entries()
    {
        var journal = new RecordingLedgerJournal();
        var bench = new WorldBench(ledgerJournal: journal);
        _ = await bench.EnterAsync(items: [new StoredItem(57, 50, -1)]);
        journal.StagePendingForTest();
        bench.World.FlushLedgerOnShutdown();
        Assert.Equal(0, journal.PendingFlushCount);
        Assert.NotEmpty(journal.Flushed);
    }

    [Fact]
    public async Task Failed_flush_increments_metric_then_succeeds()
    {
        var journal = new FlakyLedgerJournal(failuresBeforeSuccess: 1);
        var bench = new WorldBench(ledgerJournal: journal);
        using var metrics = new MetricsCollector();
        await bench.EnterAsync(items: [new StoredItem(57, 10, -1)]);
        bench.World.TryFlushLedger();
        Assert.True(metrics.Snapshot().LedgerFlushFailures >= 1);
        Assert.Equal(0, journal.PendingFlushCount);
    }

    sealed class RecordingLedgerJournal : MemoryLedgerJournal
    {
        readonly List<LedgerEntry> _pending = [];
        readonly List<LedgerEntry> _flushed = [];

        public IReadOnlyList<LedgerEntry> Flushed => _flushed;

        public override void Append(LedgerEntry entry)
        {
            base.Append(entry);
            _pending.Add(entry);
        }

        public override void Flush()
        {
            _flushed.AddRange(_pending);
            _pending.Clear();
        }

        public override int PendingFlushCount => _pending.Count;

        /// <summary>Simulates buffered journal rows not yet written durably (enter alone may not ledger).</summary>
        internal void StagePendingForTest() => _pending.Add(MakeStagedEntry(_pending.Count + 1));
    }

    static LedgerEntry MakeStagedEntry(long position) =>
        new(position, new LedgerTransaction(new LedgerTxId(0, 0), LedgerTxKind.Adjustment, 0, 0, 0, []), LedgerChain.Genesis);

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
                throw new InvalidOperationException("simulated durable write failure");
            }

            _unflushed = 0;
        }
    }
}