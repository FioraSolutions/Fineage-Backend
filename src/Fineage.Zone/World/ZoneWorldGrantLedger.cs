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
        var kit = _game.StarterKits.ForClass(player.ClassId);
        if (kit.IsEmpty)
            return;
        player.Inventory.AddStarterKit(kit);
    }

    void ApplyStarterKitLedger(ZoneEntity player)
    {
        if (player.Inventory == null)
            return;
        var kit = _game.StarterKits.ForClass(player.ClassId);
        if (kit.IsEmpty)
            return;
        // The items are already in the bag. Post that grant from an empty bag to the current one.
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var after = LedgerInventoryChange.HoldingsFrom(player.Inventory);
        var before = new LedgerHoldings();
        if (after.SameAs(before))
            return;
        var move = BeginLedgerMove(LedgerTxKind.StarterKit, player.ObjectId, player.ClassId);
        move.Observe(bag, before);
        LedgerInventoryChange.PostChange(move, bag, before, after, StarterKitSource);
        move.Commit(LedgerHoldingsOf);
        if (player.Session != null)
        {
            player.Inventory.DiscardChanges();
            SaveInventory(player.Session, player.Inventory);
        }
    }
}