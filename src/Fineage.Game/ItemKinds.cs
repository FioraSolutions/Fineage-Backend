namespace Fineage.Game;

public enum ItemKind
{
    Weapon,
    Armor,
    Etc
}

/// <summary>
/// Where an item sits when equipped. <see cref="LrHand"/> is a two-handed weapon:
/// it uses the right-hand paperdoll slot and clears the left hand. <see cref="FullArmor"/>
/// uses the chest slot and clears the legs. Legs cannot be equipped while full armor is worn.
/// </summary>
public enum BodySlot
{
    None,
    Underwear,
    LeftEar,
    RightEar,
    BothEars,
    Neck,
    LeftFinger,
    RightFinger,
    BothFingers,
    Head,
    RightHand,
    LeftHand,
    Gloves,
    Chest,
    Legs,
    Feet,
    Back,
    LrHand,
    FullArmor,
    Hair,
    AllDress,
    Hair2,
    HairAll
}

public enum WeaponType
{
    None,
    Sword,
    Blunt,
    Dagger,
    Bow,
    Pole,
    Dual,
    Fist,
    FishingRod,
    Etc
}

public enum ArmorType
{
    None,
    Light,
    Heavy,
    Magic
}

/// <summary>Etc-item kinds that may share a hand with a two-handed weapon.</summary>
public enum EtcItemType
{
    None,
    Arrow,
    Lure
}
