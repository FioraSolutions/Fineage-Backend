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
    EquipRequirement? Equip = null,
    int CrystalCount = 0,
    bool Enchantable = false,
    EnchantScroll? Scroll = null,
    int ReferencePrice = 0,
    int Weight = 0,
    bool Sellable = true,
    int PAtk = 0,
    int MAtk = 0,
    int PAtkSpd = 0,
    int CritRate = 0,
    int AttackRange = 0,
    int RandomDamage = 0,
    int MDef = 0,
    int Soulshots = 0,
    int ShieldDef = 0,
    int ShieldRate = 0,
    int ShieldEvasionPenalty = 0,
    bool IsSoulshot = false,
    CrystalGrade ShotGrade = CrystalGrade.None)
{
    public bool Equippable => BodySlot != global::Fineage.Game.BodySlot.None;

    /// <summary>Mobius reference price. Sell price is this value divided by 2.</summary>
    public bool CanSell => Sellable && ReferencePrice / 2 >= 1;
}
