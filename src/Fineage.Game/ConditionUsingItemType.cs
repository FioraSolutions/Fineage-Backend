namespace Fineage.Game;

/// <summary>
/// Mobius <c>ConditionUsingItemType</c> for skill <c>&lt;using kind&gt;</c> gates (passives, active skills, set skills).
/// Weapon kinds test the right-hand weapon type; armor kinds (LIGHT/HEAVY/MAGIC) need the chest armor type and,
/// unless the chest is full-body, matching legs; SHIELD needs a left-hand shield.
/// </summary>
public static class ConditionUsingItemType
{
    public static bool Matches(PlayerInventory? inventory, ReadOnlySpan<string> kinds)
    {
        if (inventory == null || kinds.IsEmpty)
            return false;
        return Matches(
            inventory.GetEquipped(PaperdollSlot.RHand)?.Template,
            inventory.GetEquipped(PaperdollSlot.Chest)?.Template,
            inventory.GetEquipped(PaperdollSlot.Legs)?.Template,
            inventory.GetEquipped(PaperdollSlot.LHand)?.Template,
            kinds);
    }

    public static bool Matches(
        ItemTemplate? weapon,
        ItemTemplate? chest,
        ItemTemplate? legs,
        ItemTemplate? lhand,
        ReadOnlySpan<string> kinds)
    {
        foreach (var kind in kinds)
        {
            switch (kind)
            {
                case "LIGHT" or "HEAVY" or "MAGIC":
                    var want = kind == "LIGHT" ? ArmorType.Light : kind == "HEAVY" ? ArmorType.Heavy : ArmorType.Magic;
                    if (chest != null && chest.ArmorType == want
                        && (chest.BodySlot == BodySlot.FullArmor || (legs != null && legs.ArmorType == want)))
                        return true;
                    break;
                case "SHIELD":
                    if (lhand != null && lhand.Kind == ItemKind.Armor && lhand.ShieldDef > 0)
                        return true;
                    break;
                default:
                    if (weapon == null || weapon.Kind != ItemKind.Weapon)
                        break;
                    var wt = weapon.WeaponType;
                    if (kind switch
                        {
                            "SWORD" => wt == WeaponType.Sword,
                            "BLUNT" => wt == WeaponType.Blunt,
                            "DAGGER" or "DUALDAGGER" => wt == WeaponType.Dagger,
                            "BOW" => wt == WeaponType.Bow,
                            "POLE" => wt == WeaponType.Pole,
                            "DUAL" => wt == WeaponType.Dual,
                            "FIST" or "DUALFIST" => wt == WeaponType.Fist,
                            "FISHINGROD" => wt == WeaponType.FishingRod,
                            "ETC" => wt == WeaponType.Etc,
                            _ => false
                        })
                        return true;
                    break;
            }
        }
        return false;
    }
}
