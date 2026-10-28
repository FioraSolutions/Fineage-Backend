namespace Fineage.Game;

/// <summary>
/// Static item definition. Ids, names, and the stats used here were checked against
/// L2J Mobius Interlude and written in this shape; they are not Mobius data files.
/// </summary>
public sealed record ItemTemplate(
    int Id,
    string Name,
    ItemKind Kind,
    BodySlot BodySlot,
    string Icon,
    WeaponType WeaponType = WeaponType.None,
    ArmorType ArmorType = ArmorType.None,
    EtcItemType EtcType = EtcItemType.None,
    bool MagicWeapon = false,
    bool Stackable = false,
    int PDef = 0,
    CrystalGrade Crystal = CrystalGrade.None,
    EquipRequirement? Equip = null)
{
    public bool Equippable => BodySlot != global::Fineage.Game.BodySlot.None;
}
