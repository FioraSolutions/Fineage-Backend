using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

/// <summary>P1-5c helpers: pet bag moves, pet feeding, and inventory-to-inventory postings.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>
    /// Builds ledger holdings for a player bag, treating non-stackable multi-count rows as bulk counts (Mobius
    /// stacks blessed scrolls and similar until they are consumed one by one).
    /// </summary>
    static LedgerHoldings PlayerBagHoldings(PlayerInventory inventory)
    {
        var holdings = new LedgerHoldings();
        foreach (var item in inventory.Items)
        {
            if (item.Template.Stackable || item.Count > 1)
                holdings.Add(item.ItemId, item.Count);
            else
                holdings.AddPiece(item.ItemId, item.ObjectId);
        }

        return holdings;
    }

    /// <summary>Like <see cref="RecordBagChange{T}"/> but posts non-stackable stacks as counts, not instance ids.</summary>
    T RecordPlayerBagChange<T>(
        ZoneEntity player, LedgerTxKind kind, LedgerAccount counterpart, Func<T> change, Func<T, int> reference)
    {
        if (player.Inventory == null)
            return change();
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var before = PlayerBagHoldings(player.Inventory);
        var result = change();
        var after = PlayerBagHoldings(player.Inventory);
        if (after.SameAs(before))
            return result;
        var move = BeginLedgerMove(kind, player.ObjectId, reference(result));
        move.Observe(bag, before);
        PostChange(move, bag, before, after, counterpart);
        move.Commit(account => account == bag ? PlayerBagHoldings(player.Inventory!) : LedgerHoldingsOf(account));
        return result;
    }

    /// <summary>Posts one row between two non-system inventory accounts (owner bag or pet bag).</summary>
    static void PostInventoryRow(LedgerMove move, LedgerAccount from, LedgerAccount to, ItemInstance item, int count)
    {
        IReadOnlyList<int> pieces = item.Template.Stackable ? [] : [item.ObjectId];
        move.Post(from, item.ItemId, -count, pieces);
        move.Post(to, item.ItemId, count, pieces);
    }

    /// <summary>Records a change that touches only the pet's bag, against <see cref="SystemAccount.Destroyed"/>.</summary>
    void RecordPetBagChange(ZoneEntity owner, PetState pet, LedgerTxKind kind, int reference, Action change)
    {
        var account = LedgerAccount.Pet(pet.ControlObjectId);
        var before = HoldingsOf(pet.Inventory);
        var move = BeginLedgerMove(kind, owner.ObjectId, reference);
        move.Observe(account, LedgerHoldingsOf(account));
        change();
        var after = HoldingsOf(pet.Inventory);
        if (after.SameAs(before))
            return;
        PostChange(move, account, before, after, LedgerAccount.Of(SystemAccount.Destroyed));
        move.Commit(LedgerHoldingsOf);
    }

    /// <summary>
    /// Moves stackable or piece items between owner and pet after a ledger check, like trade settlement.
    /// </summary>
    bool CommitPetOwnerTransfer(
        ZoneEntity owner,
        PetState pet,
        PlayerInventory from,
        PlayerInventory to,
        ItemInstance item,
        int count,
        Action transfer)
    {
        var ownerAcct = LedgerAccount.Inventory(owner.ObjectId);
        var petAcct = LedgerAccount.Pet(pet.ControlObjectId);
        var fromAcct = ReferenceEquals(from, owner.Inventory) ? ownerAcct : petAcct;
        var toAcct = ReferenceEquals(to, owner.Inventory) ? ownerAcct : petAcct;
        var move = BeginLedgerMove(LedgerTxKind.PetItemTransfer, owner.ObjectId, pet.ControlObjectId);
        move.Observe(fromAcct, LedgerHoldingsOf(fromAcct));
        move.Observe(toAcct, LedgerHoldingsOf(toAcct));
        PostInventoryRow(move, fromAcct, toAcct, item, count);
        if (move.CheckCommit() != LedgerRejection.None)
            return false;
        transfer();
        move.Commit(LedgerHoldingsOf);
        return true;
    }
}