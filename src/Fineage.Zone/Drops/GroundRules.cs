using Fineage.Game.Services;

namespace Fineage.Zone.Drops;

/// <summary>
/// Pure Mobius ground-loot checks (drop protection window + inventory slot capacity).
/// World still owns party membership and the bag mutation under the gate.
/// </summary>
public static class GroundRules
{
    /// <summary>
    /// Mobius <c>DropProtection.tryPickUp</c>: unprotected, expired, owner, or same party may loot.
    /// </summary>
    public static bool CanPickupProtected(
        int groundOwnerId,
        long protectUntilMs,
        long nowMs,
        int pickerObjectId,
        bool samePartyAsOwner) =>
        groundOwnerId == 0
        || nowMs >= protectUntilMs
        || groundOwnerId == pickerObjectId
        || samePartyAsOwner;

    /// <summary>
    /// Mobius <c>PlayerInventory.validateCapacity(Item)</c>: herbs never take a slot; stackables already
    /// carried take none; everything else needs <see cref="WarehouseRules.SlotsFor"/>.
    /// </summary>
    public static bool HasRoomForGroundItem(
        bool isHerb,
        int inventoryItemCount,
        int race,
        bool stackable,
        bool alreadyCarried,
        int count)
    {
        if (isHerb)
            return true;
        int slots = WarehouseRules.SlotsFor(stackable, alreadyCarried, count);
        return inventoryItemCount + slots <= WarehouseRules.InventorySlots(race);
    }
}
