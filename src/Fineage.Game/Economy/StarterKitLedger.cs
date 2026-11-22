namespace Fineage.Game.Economy;

/// <summary>Posts <see cref="LedgerTxKind.StarterKit"/> when a new character receives its Mobius kit at create or first enter.</summary>
public static class StarterKitLedger
{
    /// <summary>Same idempotency horizon as zone sim ledger (one hour of wall time).</summary>
    public const long IdempotencyWallMilliseconds = 60 * 60 * 1000;

    public static LedgerResult RecordCreateGrant(
        EconomyLedger ledger,
        LedgerTxSequence ids,
        int characterId,
        int classId,
        PlayerInventory inventory,
        long wallUnixMs)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(inventory);
        var after = LedgerInventoryChange.HoldingsFrom(inventory);
        if (after.IsEmpty)
            throw new ArgumentException("Starter kit grant requires a non-empty inventory.", nameof(inventory));
        var bag = LedgerAccount.Inventory(characterId);
        var source = LedgerAccount.Of(SystemAccount.StarterKit);
        long tick = wallUnixMs;
        var move = new LedgerMove(ledger, () => ids.Next(tick), LedgerTxKind.StarterKit, wallUnixMs, characterId, classId);
        move.Observe(bag, new LedgerHoldings());
        LedgerInventoryChange.PostChange(move, bag, new LedgerHoldings(), after, source);
        return move.Commit(_ => after);
    }
}