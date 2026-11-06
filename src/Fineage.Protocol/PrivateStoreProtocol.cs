namespace Fineage.Protocol;

/// <summary>Player private store (Mobius <c>PrivateStoreType</c>). Sell #275, buy #277, package #279.</summary>
public static class PrivateStoreProtocol
{
    /// <summary>Mobius INTERACTION_DISTANCE for opening / buying from a store.</summary>
    public const int Distance = 150;

    /// <summary>Config.MAX_PVTSTORESELL_SLOTS_OTHER (non-dwarf).</summary>
    public const int SellSlotsOther = 3;

    /// <summary>Config.MAX_PVTSTORESELL_SLOTS_DWARF.</summary>
    public const int SellSlotsDwarf = 4;

    /// <summary>Config.MAX_PVTSTOREBUY_SLOTS_OTHER.</summary>
    public const int BuySlotsOther = 4;

    /// <summary>Config.MAX_PVTSTOREBUY_SLOTS_DWARF.</summary>
    public const int BuySlotsDwarf = 5;

    public const int DwarfRace = 4;

    public const byte TypeNone = 0;
    public const byte TypeSell = 1;
    public const byte TypeBuy = 2;
    /// <summary>Mobius <c>PACKAGE_SELL</c>: visitor must buy every listed row in one request.</summary>
    public const byte TypePackageSell = 3;

    public const string Busy = "store_busy";
    public const string Combat = "store_combat";
    public const string Item = "store_item";
    public const string Range = "store_range";
    public const string Limit = "store_limit";
    public const string Adena = "store_adena";
    public const string Closed = "store_closed";
    public const string Weight = "store_weight";
    public const string Slots = "store_slots";
    public const string Package = "store_package";

    public static bool IsSellStore(byte type) =>
        type is TypeSell or TypePackageSell;

    public static int SellSlotLimit(int race) =>
        race == DwarfRace ? SellSlotsDwarf : SellSlotsOther;

    public static int BuySlotLimit(int race) =>
        race == DwarfRace ? BuySlotsDwarf : BuySlotsOther;
}
