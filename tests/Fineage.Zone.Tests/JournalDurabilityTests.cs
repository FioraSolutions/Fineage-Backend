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
        bench.World.FlushLedgerOnShutdown();
        Assert.Equal(0, journal.PendingFlushCount);
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

        public IReadOnlyList<LedgerEntry> Flushed { get; private set; } = [];

        public override void Append(LedgerEntry entry)
        {
            base.Append(entry);
            _pending.Add(entry);
        }

        public override void Flush()
        {
            Flushed = [.. _pending];
            _pending.Clear();
        }

        public override int PendingFlushCount => _pending.Count;
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
                throw new InvalidOperationException("simulated durable write failure");
            }

            _unflushed = 0;
        }
    }
}