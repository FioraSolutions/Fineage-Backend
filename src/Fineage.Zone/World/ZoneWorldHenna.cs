using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    internal enum HennaWearResult
    {
        Ok,
        UnknownDye,
        WrongClass,
        NoFreeSlot,
        MissingItems,
        MissingFee,
        NoInventory
    }

    internal enum HennaCancelResult
    {
        Ok,
        EmptySlot,
        UnknownDye,
        MissingFee,
        NoInventory
    }

    /// <summary>
    /// Recompute cached henna STR/CON/… from the three dye slots (Mobius <c>Player.recalcHennaStats</c>).
    /// Call after load or after a dye wear/cancel.
    /// </summary>
    internal void RefreshHenna(ZoneEntity player)
    {
        Span<int> dyes = stackalloc int[HennaCatalog.SlotCount];
        dyes[0] = player.HennaDye1;
        dyes[1] = player.HennaDye2;
        dyes[2] = player.HennaDye3;
        var sum = _henna.Sum(dyes);
        player.HennaStr = sum.Str;
        player.HennaCon = sum.Con;
        player.HennaDex = sum.Dex;
        player.HennaInt = sum.Int;
        player.HennaMen = sum.Men;
        player.HennaWit = sum.Wit;
    }

    /// <summary>
    /// Wear a dye into an empty slot (0..2). Returns false when the slot is taken, the dye is unknown,
    /// or the class cannot use it. Persists when an account store is attached.
    /// </summary>
    internal bool TryWearHenna(ZoneEntity player, int dyeId, out int slot)
    {
        slot = -1;
        if (!_henna.TryGet(dyeId, out var dye))
            return false;
        if (dye.ClassIds.Count > 0 && !dye.ClassIds.Contains(player.ClassId))
            return false;
        if (player.HennaDye1 == 0) slot = 0;
        else if (player.HennaDye2 == 0) slot = 1;
        else if (player.HennaDye3 == 0) slot = 2;
        else return false;
        SetHennaSlot(player, slot, dyeId);
        return true;
    }

    /// <summary>
    /// Mobius Symbol Maker Draw: consume wearCount of the dye item, then seat it.
    /// Restores the stack if no free slot remains after consume.
    /// </summary>
    internal HennaWearResult TryWearHennaPaying(
        ZoneEntity player,
        int dyeId,
        out int slot,
        out HennaDefinition? dye,
        out int have)
    {
        slot = -1;
        dye = null;
        have = 0;
        if (!_henna.TryGet(dyeId, out var def))
            return HennaWearResult.UnknownDye;
        dye = def;
        if (def.ClassIds.Count > 0 && !def.ClassIds.Contains(player.ClassId))
            return HennaWearResult.WrongClass;
        if (player.Inventory == null)
            return HennaWearResult.NoInventory;

        have = player.Inventory.CountOf(def.ItemId);
        if (have < def.WearCount)
            return HennaWearResult.MissingItems;

        if (def.WearFee > 0 && player.Inventory.CountOf(WarehouseRules.AdenaItemId) < def.WearFee)
            return HennaWearResult.MissingFee;

        HennaWearResult result = HennaWearResult.Ok;
        int wornSlot = -1;
        RecordBagChange(
            player,
            LedgerTxKind.HennaDraw,
            Fees,
            () =>
            {
                if (!player.Inventory.TryConsume(def.ItemId, def.WearCount, out _, out _))
                    throw new InvalidOperationException(
                        $"henna consume failed object={player.ObjectId} item={def.ItemId} count={def.WearCount}");
                if (def.WearFee > 0)
                {
                    var changes = new List<InventoryUpdateEntryMsg>();
                    if (!Take(player.Inventory, WarehouseRules.AdenaItemId, def.WearFee, changes))
                        throw new InvalidOperationException($"henna wear fee failed object={player.ObjectId}");
                    if (player.Session != null && changes.Count > 0)
                    {
                        FanInventory(player.Session, Collapse(changes));
                        SaveInventory(player.Session, player.Inventory);
                    }
                }

                if (!TryWearHenna(player, dyeId, out wornSlot))
                {
                    player.Inventory.Add(def.ItemId, def.WearCount);
                    if (def.WearFee > 0 && player.Session != null)
                        GiveItem(player.Session, player.Inventory, WarehouseRules.AdenaItemId, def.WearFee);
                    result = HennaWearResult.NoFreeSlot;
                    return true;
                }

                return true;
            },
            _ => dyeId);
        if (result == HennaWearResult.Ok)
            slot = wornSlot;
        return result;
    }

    /// <summary>Clear one henna slot and refresh cached deltas.</summary>
    internal bool TryCancelHenna(ZoneEntity player, int slot)
    {
        if (slot is < 0 or >= HennaCatalog.SlotCount)
            return false;
        SetHennaSlot(player, slot, 0);
        return true;
    }

    /// <summary>Mobius SymbolMaker remove: adena cancel fee, then return dye items.</summary>
    internal HennaCancelResult TryCancelHennaPaying(ZoneEntity player, int slot)
    {
        if (slot is < 0 or >= HennaCatalog.SlotCount)
            return HennaCancelResult.EmptySlot;
        int dyeId = slot switch
        {
            0 => player.HennaDye1,
            1 => player.HennaDye2,
            2 => player.HennaDye3,
            _ => 0
        };
        if (dyeId <= 0)
            return HennaCancelResult.EmptySlot;
        if (player.Inventory == null)
            return HennaCancelResult.NoInventory;
        if (!_henna.TryGet(dyeId, out var def))
            return HennaCancelResult.UnknownDye;
        if (def.CancelFee > 0 && player.Inventory.CountOf(WarehouseRules.AdenaItemId) < def.CancelFee)
            return HennaCancelResult.MissingFee;

        HennaCancelResult result = HennaCancelResult.Ok;
        RecordBagChange(
            player,
            LedgerTxKind.HennaRemove,
            Fees,
            () =>
            {
                if (def.CancelFee > 0)
                {
                    var changes = new List<InventoryUpdateEntryMsg>();
                    if (!Take(player.Inventory, WarehouseRules.AdenaItemId, def.CancelFee, changes))
                        throw new InvalidOperationException($"henna cancel fee failed object={player.ObjectId}");
                    if (player.Session != null && changes.Count > 0)
                    {
                        FanInventory(player.Session, Collapse(changes));
                        SaveInventory(player.Session, player.Inventory);
                    }
                }

                if (!TryCancelHenna(player, slot))
                {
                    result = HennaCancelResult.EmptySlot;
                    return false;
                }

                if (def.CancelCount > 0 && player.Session != null)
                    GiveItem(player.Session, player.Inventory, def.ItemId, def.CancelCount);
                return true;
            },
            _ => dyeId);
        return result;
    }

    internal void SetHennaSlot(ZoneEntity player, int slot, int dyeId)
    {
        switch (slot)
        {
            case 0: player.HennaDye1 = dyeId; break;
            case 1: player.HennaDye2 = dyeId; break;
            case 2: player.HennaDye3 = dyeId; break;
            default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "Henna slot must be 0..2.");
        }
        RefreshHenna(player);
        if (player.Session != null && _persist.Enabled)
            _persist.SaveHenna(player.Session.Account, player.ObjectId, player.HennaDye1, player.HennaDye2, player.HennaDye3);
        RefreshRunSpeed(player);
    }

    void LoadHenna(ZoneEntity player, CharacterRecord? stored)
    {
        if (stored == null)
            return;
        player.HennaDye1 = Math.Max(0, stored.HennaDye1);
        player.HennaDye2 = Math.Max(0, stored.HennaDye2);
        player.HennaDye3 = Math.Max(0, stored.HennaDye3);
        RefreshHenna(player);
    }
}
