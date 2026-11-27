namespace Fineage.Game;

/// <summary>
/// Mobius <c>Inventory.getPaperdollIndex</c>: body-part → paperdoll slot for equip and try-on preview.
/// </summary>
public static class PaperdollIndex
{
    /// <summary>
    /// Slot used when trying on or equipping <paramref name="body"/>. Null when the part is not wearable.
    /// Matches Mobius CT0 Interlude <c>Inventory.getPaperdollIndex</c>.
    /// </summary>
    public static PaperdollSlot? FromBodySlot(BodySlot body) => body switch
    {
        BodySlot.Underwear => PaperdollSlot.Under,
        BodySlot.RightEar or BodySlot.BothEars => PaperdollSlot.Rear,
        BodySlot.LeftEar => PaperdollSlot.Lear,
        BodySlot.Neck => PaperdollSlot.Neck,
        BodySlot.RightFinger or BodySlot.BothFingers => PaperdollSlot.RFinger,
        BodySlot.LeftFinger => PaperdollSlot.LFinger,
        BodySlot.Head => PaperdollSlot.Head,
        BodySlot.RightHand or BodySlot.LrHand => PaperdollSlot.RHand,
        BodySlot.LeftHand => PaperdollSlot.LHand,
        BodySlot.Gloves => PaperdollSlot.Gloves,
        BodySlot.Chest or BodySlot.FullArmor or BodySlot.AllDress => PaperdollSlot.Chest,
        BodySlot.Legs => PaperdollSlot.Legs,
        BodySlot.Feet => PaperdollSlot.Feet,
        BodySlot.Back => PaperdollSlot.Cloak,
        BodySlot.Hair or BodySlot.HairAll => PaperdollSlot.Hair,
        BodySlot.Hair2 => PaperdollSlot.Face,
        _ => null
    };

    /// <summary>Mobius <c>PAPERDOLL_TOTALSLOTS</c>.</summary>
    public const int TotalSlots = 17;
}
