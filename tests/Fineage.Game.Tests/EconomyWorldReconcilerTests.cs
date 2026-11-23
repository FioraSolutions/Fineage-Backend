using Fineage.Game.Economy;
using Fineage.Game.Services;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Ledger-only slice of world reconciliation (full harness lives in Zone.Tests).</summary>
public sealed class EconomyWorldReconcilerLedgerTests
{
    [Fact]
    public void Ledger_only_matches_invariant_checker()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 3_600_000);
        var bag = LedgerAccount.Inventory(1001);
        ledger.Submit(new LedgerTransaction(
            new LedgerTxId(1, 0),
            LedgerTxKind.Adjustment,
            0,
            0,
            0,
            [
                new LedgerPosting(bag, WarehouseRules.AdenaItemId, 10),
                new LedgerPosting(LedgerAccount.Of(SystemAccount.Untracked), WarehouseRules.AdenaItemId, -10),
            ]));
        EconomyInvariantChecker.AssertHold(ledger);
    }
}