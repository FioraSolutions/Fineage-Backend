using Fineage.Accounts;
using Fineage.Game.Economy;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-8: stale ground journal balances expire on boot instead of adjusting away.</summary>
public sealed class GroundBootExpireTests
{
    const int Adena = 57;
    const int Harvester = 5125;

    [Fact]
    public async Task Restart_expires_orphan_ground_journal_accounts()
    {
        var journal = new MemoryLedgerJournal();
        var bench = new WorldBench(ledgerJournal: journal);
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 20, -1)]);
        int groundId = bench.World.PlaceGroundForTest(player.Player, Adena, 7);
        bench.World.RemoveGroundForTest(groundId);
        Assert.Equal(0, bench.Ledger.Entries.Count(e => e.Transaction.Kind == LedgerTxKind.Expire));
        Assert.False(bench.World.LedgerForTests.HoldingsOf(LedgerAccount.Ground(groundId)).IsEmpty);

        var restarted = new WorldBench(ledgerJournal: journal);
        var expire = Assert.Single(restarted.Ledger.Entries, e => e.Transaction.Kind == LedgerTxKind.Expire);
        Assert.Equal(groundId, expire.Transaction.Reference);
        Assert.True(restarted.World.LedgerForTests.HoldingsOf(LedgerAccount.Ground(groundId)).IsEmpty);
        Assert.Equal(0, bench.Ledger.Entries.Count(e => e.Transaction.Kind == LedgerTxKind.Adjustment));
    }

    [Fact]
    public async Task Restart_expires_orphan_ground_piece_journal_account()
    {
        var journal = new MemoryLedgerJournal();
        var bench = new WorldBench(ledgerJournal: journal);
        var player = await bench.EnterAsync();
        int groundId = bench.World.PlaceGroundForTest(player.Player, Harvester);
        bench.World.RemoveGroundForTest(groundId);

        var restarted = new WorldBench(ledgerJournal: journal);
        var expire = Assert.Single(restarted.Ledger.Entries, e => e.Transaction.Kind == LedgerTxKind.Expire);
        Assert.Equal(groundId, expire.Transaction.Reference);
        Assert.True(restarted.World.LedgerForTests.HoldingsOf(LedgerAccount.Ground(groundId)).IsEmpty);
    }
}