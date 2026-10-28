namespace Fineage.Game;

/// <summary>
/// Post-safe success chances, in percent.
/// L2J Mobius CT 0 Interlude no longer stores these in <c>Character.ini</c>.
/// The same defaults live in <c>EnchantItemGroups.xml</c>: 66 after the safe steps,
/// for weapons, armor, and jewelry (jewelry is inside <c>ARMOR_GROUP</c> there).
/// The names match the older <c>EnchantChanceWeapon</c> / <c>EnchantChanceArmor</c> /
/// <c>EnchantChanceJewelry</c> keys. Blessed scrolls use the same table.
/// A crystal scroll's datapack <c>bonusRate</c> is added on top and capped at 100.
/// </summary>
public sealed record EnchantRates(int Weapon = 66, int Armor = 66, int Jewelry = 66)
{
    /// <summary>Current enchant 0–2 is safe (the item reaches +3). Full armor uses <see cref="SafeFullArmor"/>.</summary>
    public const int SafeEnchant = 3;

    /// <summary>Full armor (Mobius body part <c>onepiece</c>) is safe through current +3, so it reaches +4.</summary>
    public const int SafeFullArmor = 4;

    /// <summary>Last current enchant that still uses the configured chance. Above this the base chance is 0.</summary>
    public const int RatedUntil = 15;

    public static EnchantRates Default { get; } = new();

    public int For(ItemTemplate item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (EnchantRules.IsJewelry(item))
            return Jewelry;
        if (item.Kind == ItemKind.Weapon)
            return Weapon;
        return Armor;
    }

    public void Validate()
    {
        Check(nameof(Weapon), Weapon);
        Check(nameof(Armor), Armor);
        Check(nameof(Jewelry), Jewelry);
    }

    static void Check(string name, int value)
    {
        if (value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(name, value, "Enchant chance must be 0..100.");
    }
}
