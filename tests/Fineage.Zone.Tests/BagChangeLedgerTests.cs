using Fineage.Game.Economy;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The before/after posting that quests and crafting share (step 11f): pieces move by instance id, the rest by
/// count, and the system counterpart takes the other side of every change.
/// </summary>
public sealed class BagChangeLedgerTests
{
    const int Sword = 100;
    const int Shield = 200;
    const int Adena = 57;

    [Fact]
    public void Lost_and_gained_pieces_and_counts_balance_against_the_counterpart()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 100);
        int sequence = 0;
        var move = new LedgerMove(ledger, () => new LedgerTxId(1, sequence++), LedgerTxKind.QuestReward, 0, 7, 42);
        var bag = LedgerAccount.Inventory(7);
        var quests = LedgerAccount.Of(SystemAccount.Quests);
        var before = new LedgerHoldings().AddPiece(Sword, 1).AddPiece(Sword, 2).Add(Adena, 10);
        var after = new LedgerHoldings().AddPiece(Sword, 2).AddPiece(Sword, 3).AddPiece(Shield, 4).Add(Adena, 4);

        move.Observe(bag, before);
        ZoneWorld.PostChange(move, bag, before, after, quests);
        Assert.True(move.Commit(account => account == bag ? after : new LedgerHoldings()).Ok);

        // A swapped sword nets to nothing for the counterpart.
        var tx = journal.Entries[^1].Transaction;
        Assert.Equal(
            [
                (bag, Sword, -1L, "1"),
                (bag, Sword, 1L, "3"),
                (bag, Shield, 1L, "4"), (quests, Shield, -1L, ""),
                (bag, Adena, -6L, ""), (quests, Adena, 6L, "")
            ],
            tx.Postings.Select(posting => (posting.Account, posting.ItemId, posting.Amount, string.Join(",", posting.InstanceIds))));
        Assert.Equal(bag, ledger.OwnerOf(3));
        Assert.Null(ledger.OwnerOf(1));
    }

    [Fact]
    public void A_player_counterpart_is_refused()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 100);
        var move = new LedgerMove(ledger, () => new LedgerTxId(1, 0), LedgerTxKind.QuestReward, 0, 7, 42);
        var empty = new LedgerHoldings();
        Assert.Throws<ArgumentException>(() => ZoneWorld.PostChange(
            move, LedgerAccount.Inventory(7), empty, empty, LedgerAccount.Inventory(8)));
    }
}
