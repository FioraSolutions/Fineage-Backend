using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;

namespace Fineage.Zone.World;

/// <summary>P1-5e: starter kits and GM grants post against dedicated system accounts.</summary>
public sealed partial class ZoneWorld
{
    static readonly LedgerAccount StarterKitSource = LedgerAccount.Of(SystemAccount.StarterKit);
    static readonly LedgerAccount GmGrantSource = LedgerAccount.Of(SystemAccount.GmGrants);

    static bool NeedsStarterInventory(CharacterRecord? stored) => stored is null or { Items: null };

    /// <summary>
    /// Puts the class kit on the detached player so the off-tick self frames show the equipped weapon.
    /// The ledger post stays on the simulation thread (<see cref="ApplyStarterKitLedger"/>).
    /// </summary>
    void GrantStarterKitBody(ZoneEntity player)
    {
        if (player.Inventory == null || player.Inventory.Items.Count > 0)
            return;
        if (!_game.StarterKits.TryForClass(player.ClassId, out var kit) || kit.IsEmpty)
            return;
        player.Inventory.AddStarterKit(kit);
    }

    /// <summary>
    /// Posts a <see cref="LedgerTxKind.StarterKit"/> grant for a bag the journal does not yet know.
    /// Login writes the create kit into the character row; it is not a ledger writer (the zone is),
    /// so a brand-new character arrives with items and an empty journal account. The first enter
    /// records the grant so a later pickup or drop does not align those items as Untracked adjustments.
    /// </summary>
    void ApplyStarterKitLedger(ZoneEntity player)
    {
        if (player.Inventory == null || player.Inventory.Items.Count == 0)
            return;
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var after = LedgerInventoryChange.HoldingsFrom(player.Inventory);
        var before = _ledger.HoldingsOf(bag);
        if (after.SameAs(before))
            return;
        // A non-empty mismatch is not a create grant; the next Observe will surface it.
        if (!before.IsEmpty)
            return;
        var move = BeginLedgerMove(LedgerTxKind.StarterKit, player.ObjectId, player.ClassId);
        move.Observe(bag, before);
        LedgerInventoryChange.PostChange(move, bag, before, after, StarterKitSource);
        move.Commit(LedgerHoldingsOf);
        // The bag is already in the character row (Login create, or a test fixture).
        // This method only journals the grant; it must not persist inventory on the
        // enter path, which would commit economy I/O on the simulation thread.
    }
}
