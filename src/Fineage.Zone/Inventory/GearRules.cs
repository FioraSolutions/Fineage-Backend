using Fineage.Protocol;

namespace Fineage.Zone.Inventory;

/// <summary>
/// Pure inventory gear / grant prechecks. World owns equip mutation, paperdoll fanout, and persistence.
/// </summary>
public static class GearRules
{
    public static string? UseOwnedError(bool owned) =>
        owned ? null : InventoryProtocol.NotOwned;

    public static string? UnequipSlotError(int paperdollSlot, int maxSlotInclusive) =>
        paperdollSlot < 0 || paperdollSlot > maxSlotInclusive ? InventoryProtocol.BadSlot : null;

    public static string? UnequipEmptyError(bool slotOccupied) =>
        slotOccupied ? null : InventoryProtocol.Empty;

    public static string? GrantError(int itemId, int count, int maxCount, bool catalogKnown) =>
        itemId <= 0 || count <= 0 || count > maxCount || !catalogKnown ? "bad_request" : null;
}
