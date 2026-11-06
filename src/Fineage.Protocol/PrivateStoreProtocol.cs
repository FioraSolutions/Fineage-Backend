namespace Fineage.Protocol;

/// <summary>Player private store (Mobius <c>PrivateStoreType</c>). Sell slice first (#275).</summary>
public static class PrivateStoreProtocol
{
    /// <summary>Mobius INTERACTION_DISTANCE for opening / buying from a store.</summary>
    public const int Distance = 150;

    /// <summary>Config.MAX_PVTSTORESELL_SLOTS_OTHER (non-dwarf).</summary>
    public const int SellSlotsOther = 3;

    /// <summary>Config.MAX_PVTSTORESELL_SLOTS_DWARF.</summary>
    public const int SellSlotsDwarf = 4;

    public const int DwarfRace = 4;

    public const byte TypeNone = 0;
    public const byte TypeSell = 1;

    public const string Busy = "store_busy";
    public const string Combat = "store_combat";
    public const string Item = "store_item";
    public const string Range = "store_range";
    public const string Limit = "store_limit";
    public const string Adena = "store_adena";
    public const string Closed = "store_closed";
    public const string Weight = "store_weight";
    public const string Slots = "store_slots";

    public static int SellSlotLimit(int race) =>
        race == DwarfRace ? SellSlotsDwarf : SellSlotsOther;
}
