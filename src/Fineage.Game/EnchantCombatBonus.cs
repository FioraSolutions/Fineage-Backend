namespace Fineage.Game;

/// <summary>
/// Mobius <c>FuncEnchant</c> combat-stat deltas for equipped gear.
/// Armor/jewel P.Def and M.Def: <c>enchant + 3 * overenchant</c> (levels above +3).
/// Weapon P.Atk / M.Atk: grade × body-slot × weapon-type table; overenchant doubles the per-level bonus.
/// </summary>
public static class EnchantCombatBonus
{
    /// <summary>P.Def / M.Def addend for an enchanted armor or jewel piece.</summary>
    public static int Defence(int enchantLevel)
    {
        if (enchantLevel <= 0)
            return 0;
        Split(enchantLevel, out int enchant, out int over);
        return enchant + 3 * over;
    }

    /// <summary>M.Atk addend for an enchanted weapon.</summary>
    public static int MagicAttack(CrystalGrade grade, int enchantLevel)
    {
        if (enchantLevel <= 0)
            return 0;
        Split(enchantLevel, out int enchant, out int over);
        return grade switch
        {
            CrystalGrade.S => 4 * enchant + 8 * over,
            CrystalGrade.A or CrystalGrade.B or CrystalGrade.C => 3 * enchant + 6 * over,
            _ => 2 * enchant + 4 * over // D / None
        };
    }

    /// <summary>P.Atk addend for an enchanted weapon.</summary>
    public static int PhysicalAttack(ItemTemplate weapon, int enchantLevel)
    {
        if (enchantLevel <= 0 || weapon.Kind != ItemKind.Weapon)
            return 0;
        Split(enchantLevel, out int enchant, out int over);
        bool twoHand = weapon.BodySlot == BodySlot.LrHand;
        bool bow = weapon.WeaponType == WeaponType.Bow;
        return weapon.Crystal switch
        {
            CrystalGrade.S => twoHand
                ? (bow ? 10 * enchant + 20 * over : 6 * enchant + 12 * over)
                : 5 * enchant + 10 * over,
            CrystalGrade.A => twoHand
                ? (bow ? 8 * enchant + 16 * over : 5 * enchant + 10 * over)
                : 4 * enchant + 8 * over,
            CrystalGrade.B or CrystalGrade.C => twoHand
                ? (bow ? 6 * enchant + 12 * over : 4 * enchant + 8 * over)
                : 3 * enchant + 6 * over,
            _ => bow
                ? 4 * enchant + 8 * over
                : 2 * enchant + 4 * over // D / None
        };
    }

    static void Split(int enchantLevel, out int enchant, out int over)
    {
        if (enchantLevel > 3)
        {
            over = enchantLevel - 3;
            enchant = 3;
        }
        else
        {
            over = 0;
            enchant = enchantLevel;
        }
    }
}
