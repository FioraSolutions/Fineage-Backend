using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>P1-5f: consumption posts against <see cref="SystemAccount.Consumed"/>.</summary>
public sealed partial class ZoneWorld
{
    static readonly LedgerAccount Consumed = LedgerAccount.Of(SystemAccount.Consumed);

    /// <summary>Consumes stackable items and records the bag delta against <see cref="SystemAccount.Consumed"/>.</summary>
    bool RecordConsume(
        ZoneEntity player,
        int reference,
        int itemId,
        int count,
        List<InventoryUpdateEntryMsg> changes,
        Action? afterConsume = null)
    {
        if (count <= 0)
            return true;
        if (player.Inventory == null)
            return false;
        bool ok = false;
        RecordBagChange(
            player,
            LedgerTxKind.Consume,
            Consumed,
            () =>
            {
                ok = Take(player.Inventory, itemId, count, changes);
                if (ok)
                    afterConsume?.Invoke();
                return reference;
            },
            _ => reference);
        return ok;
    }

    /// <summary>Consumes one non-stackable row (or one unit from a stack) by instance.</summary>
    bool RecordConsumeOne(ZoneEntity player, ItemInstance item, int reference, out bool removedEntirely)
    {
        removedEntirely = false;
        if (player.Inventory == null)
            return false;
        bool ok = false;
        bool removed = false;
        RecordBagChange(
            player,
            LedgerTxKind.Consume,
            Consumed,
            () =>
            {
                if (!player.Inventory.TryReduce(item, 1, out removed))
                    return reference;
                ok = true;
                return reference;
            },
            _ => reference);
        removedEntirely = removed;
        return ok;
    }
}