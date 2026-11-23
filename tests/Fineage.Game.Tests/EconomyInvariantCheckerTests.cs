using Fineage.Game.Economy;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class EconomyInvariantCheckerTests
{
    const int Alice = 1001;
    const int Sword = 2369;

    static readonly LedgerAccount AliceBag = LedgerAccount.Inventory(Alice);
    static readonly LedgerAccount Shop = LedgerAccount.Of(SystemAccount.NpcShop);

    [Fact]
    public void Valid_ledger_passes_invariants()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 100);
        Assert.True(ledger.Submit(Tx(1, 0, Post(Shop, Sword, -1), Post(AliceBag, Sword, 1, 501))).Ok);
        EconomyInvariantChecker.AssertHold(ledger);
    }

    static LedgerTransaction Tx(long tick, int seq, params LedgerPosting[] postings) =>
        new(new LedgerTxId(tick, seq), LedgerTxKind.Adjustment, 0, 0, 0, postings);

    static LedgerPosting Post(LedgerAccount account, int item, long amount, params int[] instances) =>
        new(account, item, amount, instances);
}