namespace Fineage.Game.Services;

/// <summary>
/// Private warehouse limits. Mobius <c>Player.ini</c> defaults: warehouse 120 dwarf / 100 other,
/// inventory 100 dwarf / 80 other, deposit fee 30 adena per requested line
/// (<c>SendWareHouseDepositList</c> uses <c>_items.size() * 30</c>).
/// Max load is <c>floor(CON bonus * 69000 * AltWeightLimit)</c> with <c>AltWeightLimit</c> 1.
/// A stackable item that is already stored does not take another slot. A new stack does.
/// Non-stackable lines take one slot per count.
/// </summary>
public static class WarehouseRules
{
    public const int RaceDwarf = 4;
    public const int AdenaItemId = 57;
    public const int DepositFeePerLine = 30;
    public const int WarehouseSlotsDwarf = 120;
    public const int WarehouseSlotsOther = 100;
    public const int InventorySlotsDwarf = 100;
    public const int InventorySlotsOther = 80;
    public const int BaseMaxLoad = 69000;
    public const int MaxLines = 200;

    public static int WarehouseSlots(int race) =>
        race == RaceDwarf ? WarehouseSlotsDwarf : WarehouseSlotsOther;

    public static int InventorySlots(int race) =>
        race == RaceDwarf ? InventorySlotsDwarf : InventorySlotsOther;

    public static int MaxLoad(double conBonus)
    {
        if (conBonus < 0)
            throw new ArgumentOutOfRangeException(nameof(conBonus));
        return (int)Math.Floor(conBonus * BaseMaxLoad);
    }

    public static int SlotsFor(bool stackable, bool alreadyStored, int count)
    {
        if (count < 1)
            return 0;
        if (!stackable)
            return count;
        return alreadyStored ? 0 : 1;
    }

    public static int DepositFee(int lines)
    {
        if (lines < 0)
            throw new ArgumentOutOfRangeException(nameof(lines));
        return checked(lines * DepositFeePerLine);
    }

    /// <summary>
    /// Adena left after the deposited adena is set aside, before the fee.
    /// Mobius subtracts deposited adena from the balance and then requires the fee.
    /// </summary>
    public static bool FeeCovered(int adena, int depositedAdena, int fee) =>
        adena - depositedAdena >= fee && depositedAdena >= 0 && fee >= 0;
}
