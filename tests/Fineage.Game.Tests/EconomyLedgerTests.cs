using Fineage.Game.Economy;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class EconomyLedgerTests
{
    const int Adena = 57;
    const int Potion = 1060;
    const int Sword = 2369;
    const int Alice = 1001;
    const int Bob = 1002;

    static readonly LedgerAccount AliceBag = LedgerAccount.Inventory(Alice);
    static readonly LedgerAccount BobBag = LedgerAccount.Inventory(Bob);
    static readonly LedgerAccount Shop = LedgerAccount.Of(SystemAccount.NpcShop);
    static readonly LedgerAccount Untracked = LedgerAccount.Of(SystemAccount.Untracked);

    [Fact]
    public void A_balanced_move_commits_moves_balances_and_chains_the_journal()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 100);
        Assert.True(ledger.Submit(Tx(1, 0, Post(Shop, Adena, -1000), Post(AliceBag, Adena, 1000))).Ok);

        var buy = ledger.Submit(Tx(2, 0,
            Post(AliceBag, Adena, -40), Post(Shop, Adena, 40),
            Post(Shop, Potion, -2), Post(AliceBag, Potion, 2)));

        Assert.Equal(new LedgerResult(new LedgerTxId(2, 0), LedgerOutcome.Committed, LedgerRejection.None, 2), buy);
        Assert.Equal(960, ledger.HoldingsOf(AliceBag).CountOf(Adena));
        Assert.Equal(2, ledger.HoldingsOf(AliceBag).CountOf(Potion));
        Assert.Equal(-960, ledger.HoldingsOf(Shop).CountOf(Adena));
        Assert.Equal(2, journal.Entries.Count);
        Assert.Null(LedgerChain.FirstBroken(journal.Entries));
        Assert.Equal(LedgerChain.Hash(journal.Entries[0].Hash, journal.Entries[1].Transaction), ledger.Head);
    }

    [Fact]
    public void Moves_that_do_not_add_up_are_refused_and_change_nothing()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 100);
        ledger.Submit(Tx(1, 0, Post(Shop, Adena, -100), Post(AliceBag, Adena, 100)));

        Assert.Equal(LedgerRejection.Empty, ledger.Submit(Tx(2, 0)).Rejection);
        Assert.Equal(LedgerRejection.Unbalanced,
            ledger.Submit(Tx(2, 1, Post(AliceBag, Adena, -50), Post(Shop, Adena, 40))).Rejection);
        Assert.Equal(LedgerRejection.Overdraft,
            ledger.Submit(Tx(2, 2, Post(AliceBag, Adena, -101), Post(Shop, Adena, 101))).Rejection);
        Assert.Equal(LedgerRejection.BadPosting,
            ledger.Submit(Tx(2, 3, Post(AliceBag, 0, -1), Post(Shop, 0, 1))).Rejection);
        Assert.Equal(LedgerRejection.BadPosting,
            ledger.Submit(Tx(2, 4, Post(Shop, Sword, -1, 7), Post(AliceBag, Sword, 1, 7))).Rejection);

        Assert.Single(journal.Entries);
        Assert.Equal(100, ledger.HoldingsOf(AliceBag).CountOf(Adena));
    }

    [Fact]
    public void Check_validates_without_appending_or_changing_balances()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 100);
        Assert.True(ledger.Submit(Tx(1, 0, Post(Shop, Sword, -1), Post(AliceBag, Sword, 1, 501))).Ok);
        int entries = journal.Entries.Count;
        var before = ledger.HoldingsOf(AliceBag);

        Assert.Equal(LedgerRejection.InstanceTaken,
            ledger.Check(Tx(2, 0, Post(Shop, Sword, -1), Post(BobBag, Sword, 1, 501))));
        Assert.Equal(entries, journal.Entries.Count);
        Assert.True(ledger.HoldingsOf(AliceBag).SameAs(before));
    }

    [Fact]
    public void Pieces_have_one_owner_and_move_by_instance_id()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 100);
        Assert.True(ledger.Submit(Tx(1, 0, Post(Shop, Sword, -1), Post(AliceBag, Sword, 1, 501))).Ok);
        Assert.Equal(AliceBag, ledger.OwnerOf(501));

        // Minting an id someone holds, taking one they do not, and moving a piece by count alone are all refused.
        Assert.Equal(LedgerRejection.InstanceTaken,
            ledger.Submit(Tx(2, 0, Post(Shop, Sword, -1), Post(BobBag, Sword, 1, 501))).Rejection);
        Assert.Equal(LedgerRejection.InstanceNotOwned,
            ledger.Submit(Tx(2, 1, Post(BobBag, Sword, -1, 501), Post(AliceBag, Sword, 1, 501))).Rejection);
        Assert.Equal(LedgerRejection.InstanceMismatch,
            ledger.Submit(Tx(2, 2, Post(AliceBag, Sword, -1), Post(Shop, Sword, 1))).Rejection);
        Assert.Equal(LedgerRejection.InstanceMismatch,
            ledger.Submit(Tx(2, 3, Post(Shop, Sword, -2), Post(AliceBag, Sword, 2, 502))).Rejection);

        Assert.True(ledger.Submit(Tx(3, 0, Post(AliceBag, Sword, -1, 501), Post(BobBag, Sword, 1, 501))).Ok);
        Assert.Equal(BobBag, ledger.OwnerOf(501));
        Assert.Empty(ledger.HoldingsOf(AliceBag).Instances);

        Assert.True(ledger.Submit(Tx(4, 0, Post(BobBag, Sword, -1, 501), Post(Shop, Sword, 1))).Ok);
        Assert.Null(ledger.OwnerOf(501));
    }

    [Fact]
    public void A_repeated_id_returns_the_stored_result_and_a_different_move_under_it_conflicts()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 100);
        var grant = Tx(5, 0, Post(Shop, Adena, -10), Post(AliceBag, Adena, 10));
        Assert.Equal(LedgerOutcome.Committed, ledger.Submit(grant).Outcome);

        var again = ledger.Submit(grant with { Postings = grant.Postings.ToList() });
        Assert.Equal(new LedgerResult(grant.Id, LedgerOutcome.Duplicate, LedgerRejection.None, 1), again);
        Assert.Equal(10, ledger.HoldingsOf(AliceBag).CountOf(Adena));

        Assert.Equal(LedgerRejection.Conflict,
            ledger.Submit(Tx(5, 0, Post(Shop, Adena, -11), Post(AliceBag, Adena, 11))).Rejection);
        Assert.Single(journal.Entries);
    }

    [Fact]
    public void Ids_older_than_the_idempotency_window_are_stale_and_leave_the_index()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 10);
        var early = Tx(100, 0, Post(Shop, Adena, -1), Post(AliceBag, Adena, 1));
        ledger.Submit(early);
        ledger.Submit(Tx(111, 0, Post(Shop, Adena, -1), Post(AliceBag, Adena, 1)));

        Assert.Null(ledger.Find(early.Id));
        Assert.Equal(LedgerRejection.Stale, ledger.Submit(early).Rejection);
        Assert.True(ledger.Submit(Tx(101, 0, Post(Shop, Adena, -1), Post(AliceBag, Adena, 1))).Ok);
        Assert.Equal(3, ledger.HoldingsOf(AliceBag).CountOf(Adena));
    }

    [Fact]
    public void Starting_replays_the_journal_and_refuses_an_edited_one()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 100);
        ledger.Submit(Tx(1, 0, Post(Shop, Adena, -500), Post(AliceBag, Adena, 500)));
        ledger.Submit(Tx(2, 0, Post(Shop, Sword, -1), Post(AliceBag, Sword, 1, 501)));
        ledger.Submit(Tx(3, 0, Post(AliceBag, Adena, -200), Post(BobBag, Adena, 200),
            Post(AliceBag, Sword, -1, 501), Post(BobBag, Sword, 1, 501)));

        var replayed = new EconomyLedger(journal, 100);
        Assert.Equal(ledger.Head, replayed.Head);
        Assert.Equal(3, replayed.Position);
        Assert.True(replayed.HoldingsOf(AliceBag).SameAs(ledger.HoldingsOf(AliceBag)));
        Assert.True(replayed.HoldingsOf(BobBag).SameAs(ledger.HoldingsOf(BobBag)));
        Assert.Equal(BobBag, replayed.OwnerOf(501));
        Assert.Equal(LedgerOutcome.Duplicate, replayed.Submit(journal.Entries[2].Transaction).Outcome);

        var edited = journal.Entries.ToList();
        var second = edited[1];
        edited[1] = second with
        {
            Transaction = second.Transaction with
            {
                Postings = [Post(Shop, Sword, -1), Post(BobBag, Sword, 1, 501)]
            }
        };
        var error = Assert.Throws<InvalidOperationException>(() => new EconomyLedger(new Replay(edited), 100));
        Assert.Contains("broken at position 2", error.Message);
    }

    [Fact]
    public void A_move_first_brings_an_account_in_line_with_its_state_through_an_untracked_adjustment()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 100);
        var ids = new LedgerTxSequence();

        var bag = new LedgerHoldings().Add(Adena, 1000).AddPiece(Sword, 501);
        var sell = new LedgerMove(ledger, () => ids.Next(7), LedgerTxKind.ShopSell, 70_000, Alice, 30001);
        sell.Observe(AliceBag, bag);
        sell.Post(AliceBag, Sword, -1, [501]);
        sell.Post(Shop, Sword, 1);
        sell.Post(Shop, Adena, -500);
        sell.Post(AliceBag, Adena, 500);
        var result = sell.Commit(_ => new LedgerHoldings().Add(Adena, 1500));

        Assert.Equal(new LedgerTxId(7, 1), result.Id);
        var opening = journal.Entries[0].Transaction;
        Assert.Equal(LedgerTxKind.Adjustment, opening.Kind);
        Assert.Equal(new LedgerTxId(7, 0), opening.Id);
        Assert.Equal(
            Plain(
                Post(AliceBag, Sword, 1, 501),
                Post(AliceBag, Adena, 1000),
                Post(Untracked, Adena, -1000),
                Post(Untracked, Sword, -1)),
            opening.Postings.Select(Plain));
        Assert.Equal(
            Plain(Post(AliceBag, Sword, -1, 501), Post(Shop, Sword, 1), Post(Shop, Adena, -500), Post(AliceBag, Adena, 500)),
            journal.Entries[1].Transaction.Postings.Select(Plain));

        // The bag changed outside the ledger (a quest paid 30 adena, the sword came back): the next move records that.
        var buy = new LedgerMove(ledger, () => ids.Next(8), LedgerTxKind.ShopBuy, 80_000, Alice, 30001);
        buy.Observe(AliceBag, new LedgerHoldings().Add(Adena, 1530).AddPiece(Sword, 501));
        Assert.Equal(
            Plain(Post(AliceBag, Sword, 1, 501), Post(AliceBag, Adena, 30), Post(Untracked, Adena, -30), Post(Untracked, Sword, -1)),
            journal.Entries[2].Transaction.Postings.Select(Plain));
    }

    [Fact]
    public void A_piece_that_changed_hands_outside_the_ledger_leaves_its_old_owner_through_the_adjustment()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 100);
        var ids = new LedgerTxSequence();
        ledger.Submit(Tx(1, 0, Post(Shop, Sword, -1), Post(AliceBag, Sword, 1, 501)));

        var move = new LedgerMove(ledger, () => ids.Next(2), LedgerTxKind.ShopSell, 0, Bob, 0);
        move.Observe(BobBag, new LedgerHoldings().AddPiece(Sword, 501));

        Assert.Equal(
            Plain(Post(AliceBag, Sword, -1, 501), Post(BobBag, Sword, 1, 501)),
            journal.Entries[1].Transaction.Postings.Select(Plain));
        Assert.Equal(BobBag, ledger.OwnerOf(501));
        Assert.Equal(0, ledger.HoldingsOf(AliceBag).CountOf(Sword));
    }

    [Fact]
    public void A_move_whose_postings_disagree_with_the_state_throws_instead_of_being_corrected()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 100);
        var ids = new LedgerTxSequence();

        var move = new LedgerMove(ledger, () => ids.Next(1), LedgerTxKind.ShopBuy, 0, Alice, 0);
        move.Observe(AliceBag, new LedgerHoldings().Add(Adena, 100));
        move.Post(AliceBag, Adena, -40);
        move.Post(Shop, Adena, 40);
        move.Post(Shop, Potion, -1);
        move.Post(AliceBag, Potion, 1);
        var error = Assert.Throws<LedgerReconciliationException>(
            () => move.Commit(_ => new LedgerHoldings().Add(Adena, 60).Add(Potion, 2)));
        Assert.Contains("character:1001/inventory holds {57x60, 1060x2}", error.Message);

        var unobserved = new LedgerMove(ledger, () => ids.Next(2), LedgerTxKind.ShopBuy, 0, Bob, 0);
        unobserved.Post(BobBag, Adena, 1);
        unobserved.Post(Shop, Adena, -1);
        Assert.Throws<InvalidOperationException>(() => unobserved.Commit(_ => new LedgerHoldings()));

        var overdraft = new LedgerMove(ledger, () => ids.Next(3), LedgerTxKind.ShopBuy, 0, Bob, 0);
        overdraft.Observe(BobBag, new LedgerHoldings());
        overdraft.Post(BobBag, Adena, -1);
        overdraft.Post(Shop, Adena, 1);
        Assert.Contains("Overdraft",
            Assert.Throws<LedgerReconciliationException>(() => overdraft.Commit(_ => new LedgerHoldings())).Message);
    }

    [Fact]
    public void Ids_count_within_a_tick_and_never_go_back()
    {
        var ids = new LedgerTxSequence();
        Assert.Equal(new LedgerTxId(10, 0), ids.Next(10));
        Assert.Equal(new LedgerTxId(10, 1), ids.Next(10));
        Assert.Equal(new LedgerTxId(12, 0), ids.Next(12));
        Assert.Equal(new LedgerTxId(12, 1), ids.Next(11));
    }

    [Fact]
    public void An_emptied_account_is_forgotten_and_reads_as_empty()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 100);
        var stack = LedgerAccount.Ground(700001);
        var source = LedgerAccount.Of(SystemAccount.DropSource);
        Assert.True(ledger.Submit(Tx(1, 0, Post(source, Sword, -1), Post(stack, Sword, 1, 700001))).Ok);
        Assert.Equal(2, ledger.AccountCount);

        Assert.True(ledger.Submit(Tx(2, 0, Post(stack, Sword, -1, 700001), Post(AliceBag, Sword, 1, 700001))).Ok);

        Assert.Equal(2, ledger.AccountCount);
        Assert.True(ledger.HoldingsOf(stack).IsEmpty);
        Assert.Equal(AliceBag, ledger.OwnerOf(700001));
        Assert.Equal("world:700001/ground", stack.ToString());
    }

    static LedgerTransaction Tx(long tick, int sequence, params LedgerPosting[] postings) =>
        new(new LedgerTxId(tick, sequence), LedgerTxKind.ShopBuy, Alice, 30001, tick * 200, postings);

    static LedgerPosting Post(LedgerAccount account, int itemId, long amount, params int[] instances) =>
        new(account, itemId, amount, instances);

    static (LedgerAccount, int, long, string) Plain(LedgerPosting posting) =>
        (posting.Account, posting.ItemId, posting.Amount, string.Join(",", posting.InstanceIds));

    static IEnumerable<(LedgerAccount, int, long, string)> Plain(params LedgerPosting[] postings) =>
        postings.Select(Plain);

    sealed class Replay(IReadOnlyList<LedgerEntry> entries) : ILedgerJournal
    {
        public IReadOnlyList<LedgerEntry> ReadAll() => entries;

        public void Append(LedgerEntry entry) => throw new InvalidOperationException("replay only");

        public void Flush()
        {
        }
    }
}
