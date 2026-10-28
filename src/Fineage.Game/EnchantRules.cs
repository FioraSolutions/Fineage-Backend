namespace Fineage.Game;

/// <summary>
/// Enchant roll and crystallize counts used by the zone.
/// Checked against L2J Mobius CT 0 Interlude <c>RequestEnchantItem</c>,
/// <c>EnchantScroll</c>, <c>AbstractEnchantItem</c>, <c>EnchantItemGroups.xml</c>,
/// <c>EnchantItemData.xml</c>, <c>ItemTemplate.getCrystalCount</c>, and <c>CrystalType</c>.
/// This is not a port of those classes.
/// </summary>
public static class EnchantRules
{
    public const int AbsoluteMax = EnchantScroll.DefaultMaxEnchant;

    /// <summary>Mobius <c>Player.ini</c> <c>EnchantBlackList</c>. Apprentice, traveler, and Pailaka weapons.</summary>
    public static readonly int[] Blacklist =
    [
        7816, 7817, 7818, 7819, 7820, 7821, 7822, 7823, 7824, 7825, 7826, 7827, 7828, 7829, 7830, 7831,
        13293, 13294, 13296
    ];

    static readonly HashSet<int> BlacklistSet = [.. Blacklist];

    /// <summary>Crystal item ids from Mobius <c>CrystalType</c>.</summary>
    public static int CrystalItemId(CrystalGrade grade) => grade switch
    {
        CrystalGrade.D => 1458,
        CrystalGrade.C => 1459,
        CrystalGrade.B => 1460,
        CrystalGrade.A => 1461,
        CrystalGrade.S => 1462,
        _ => 0
    };

    public static bool IsJewelry(ItemTemplate item) => item.BodySlot is
        BodySlot.Neck or
        BodySlot.LeftEar or BodySlot.RightEar or BodySlot.BothEars or
        BodySlot.LeftFinger or BodySlot.RightFinger or BodySlot.BothFingers;

    public static int SafeLevel(ItemTemplate item) =>
        item.BodySlot == BodySlot.FullArmor ? EnchantRates.SafeFullArmor : EnchantRates.SafeEnchant;

    public static bool IsEnchantable(ItemTemplate item) =>
        item.Enchantable
        && item.Kind is ItemKind.Weapon or ItemKind.Armor
        && !BlacklistSet.Contains(item.Id);

    /// <summary>
    /// Why this scroll cannot be applied. Null when the roll may proceed.
    /// Order matches <c>AbstractEnchantItem.isValid</c>: enchantable, item type, max, grade.
    /// </summary>
    public static EnchantDeny? Reject(ItemTemplate target, int enchantLevel, EnchantScroll scroll)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scroll);
        if (!IsEnchantable(target))
            return EnchantDeny.NotEnchantable;
        if (scroll.Weapon != (target.Kind == ItemKind.Weapon))
            return EnchantDeny.SlotMismatch;
        if (scroll.MaxEnchant != 0 && enchantLevel >= scroll.MaxEnchant)
            return EnchantDeny.MaxEnchant;
        if (scroll.Grade != target.Crystal)
            return EnchantDeny.GradeMismatch;
        return null;
    }

    /// <summary>Group chance before the scroll bonus. 100 inside the safe steps, 0 past <see cref="EnchantRates.RatedUntil"/>.</summary>
    public static double BaseChance(ItemTemplate item, int enchantLevel, EnchantRates rates)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(rates);
        if (enchantLevel < SafeLevel(item))
            return 100;
        if (enchantLevel <= EnchantRates.RatedUntil)
            return rates.For(item);
        return 0;
    }

    /// <summary>Base chance plus <see cref="EnchantScroll.BonusRate"/>, capped at 100. Mobius <c>calculateSuccess</c>.</summary>
    public static double FinalChance(ItemTemplate item, int enchantLevel, EnchantScroll scroll, EnchantRates rates)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        return Math.Min(BaseChance(item, enchantLevel, rates) + scroll.BonusRate, 100);
    }

    /// <summary>
    /// One roll. Success when <c>100 * sample &lt; finalChance</c>.
    /// <see cref="EnchantRoll.BaseChance"/> is what Mobius checks before it actually increments.
    /// </summary>
    public static EnchantRoll Roll(ItemTemplate item, int enchantLevel, EnchantScroll scroll, EnchantRates rates, IEnchantRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        double baseChance = BaseChance(item, enchantLevel, rates);
        double finalChance = Math.Min(baseChance + scroll.BonusRate, 100);
        double sample = 100 * random.NextUnitInterval();
        return new EnchantRoll(sample < finalChance, baseChance, finalChance);
    }

    /// <summary>
    /// Crystals returned when a normal scroll fails.
    /// Mobius: <c>getCrystalCount(enchant) - ((base + 1) / 2)</c>, at least 1 when the item is crystallizable.
    /// </summary>
    public static int CrystallizedCount(ItemTemplate item, int enchantLevel)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!IsCrystallizable(item))
            return 0;
        int count = CrystalCountAt(item, enchantLevel) - ((item.CrystalCount + 1) / 2);
        return count < 1 ? 1 : count;
    }

    public static bool IsCrystallizable(ItemTemplate item) =>
        item.Crystal != CrystalGrade.None && item.CrystalCount > 0;

    /// <summary>Mobius <c>ItemTemplate.getCrystalCount(int)</c>. Jewelry uses the armor bonus.</summary>
    public static int CrystalCountAt(ItemTemplate item, int enchantLevel)
    {
        ArgumentNullException.ThrowIfNull(item);
        int bonus = item.Kind == ItemKind.Weapon
            ? WeaponBonus(item.Crystal)
            : item.Kind == ItemKind.Armor
                ? ArmorBonus(item.Crystal)
                : 0;
        if (bonus == 0 || item.Kind is not (ItemKind.Weapon or ItemKind.Armor))
            return item.CrystalCount;

        if (enchantLevel > 3)
        {
            int steps = item.Kind == ItemKind.Weapon
                ? (2 * enchantLevel) - 3
                : (3 * enchantLevel) - 6;
            return item.CrystalCount + (bonus * steps);
        }

        if (enchantLevel <= 0)
            return item.CrystalCount;
        return item.CrystalCount + (bonus * enchantLevel);
    }

    /// <summary>Mobius <c>CrystalType</c> armor enchant bonus.</summary>
    public static int ArmorBonus(CrystalGrade grade) => grade switch
    {
        CrystalGrade.D => 11,
        CrystalGrade.C => 6,
        CrystalGrade.B => 11,
        CrystalGrade.A => 20,
        CrystalGrade.S => 25,
        _ => 0
    };

    /// <summary>Mobius <c>CrystalType</c> weapon enchant bonus.</summary>
    public static int WeaponBonus(CrystalGrade grade) => grade switch
    {
        CrystalGrade.D => 90,
        CrystalGrade.C => 45,
        CrystalGrade.B => 67,
        CrystalGrade.A => 145,
        CrystalGrade.S => 250,
        _ => 0
    };
}

/// <summary>One enchant sample. <see cref="Success"/> is the roll. <see cref="BaseChance"/> gates the +1.</summary>
public readonly record struct EnchantRoll(bool Success, double BaseChance, double FinalChance);

/// <summary>Scroll and target cannot be rolled. The zone maps these onto <c>EnchantProtocol</c> codes.</summary>
public enum EnchantDeny
{
    NotEnchantable,
    SlotMismatch,
    MaxEnchant,
    GradeMismatch
}
