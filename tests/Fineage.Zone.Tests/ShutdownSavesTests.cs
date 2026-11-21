using Fineage.Accounts;
using Fineage.Game.Economy;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-10: graceful shutdown persists every online player and drains the journal buffer.</summary>
public sealed class ShutdownSavesTests
{
    [Fact]
    public async Task Shutdown_path_saves_online_players_and_drains_journal()
    {
        var journal = new BufferingLedgerJournal();
        var bench = new WorldBench(ledgerJournal: journal);
        var first = await bench.EnterAsync(items: [new StoredItem(57, 50, -1)]);
        var second = await bench.EnterAsync(items: [new StoredItem(57, 25, -1)]);
        Assert.Equal(2, bench.World.OnlinePlayerCount);
        journal.StagePendingForTest();

        bench.World.ShutdownGracefully();
        bench.World.FlushLedgerOnShutdown();

        Assert.Equal(0, bench.World.OnlinePlayerCount);
        Assert.NotEmpty(first.Saved());
        Assert.NotEmpty(second.Saved());
        Assert.Equal(0, journal.PendingFlushCount);
    }

    sealed class BufferingLedgerJournal : MemoryLedgerJournal
    {
        int _unflushed;

        public override void Append(LedgerEntry entry)
        {
            base.Append(entry);
            _unflushed++;
        }

        public override void Flush() => _unflushed = 0;

        public override int PendingFlushCount => _unflushed;

        internal void StagePendingForTest() => _unflushed++;
    }
}