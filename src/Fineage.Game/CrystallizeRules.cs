namespace Fineage.Game;

/// <summary>
/// Inventory crystallize (RequestCrystallizeItem). Enchant-fail crystals stay on
/// <see cref="EnchantRules.CrystallizedCount"/>; this path uses the full crystal count of each destroyed piece.
/// </summary>
public static class CrystallizeRules
{
    /// <summary>Mobius common skill Crystallize.</summary>
    public const int SkillId = 248;

    /// <summary>
    /// Skill level required for a grade. The grade's numeric level is that minimum
    /// (D 1 … S 5). None is not crystallizable.
    /// </summary>
    public static int RequiredSkillLevel(CrystalGrade grade) => (int)grade;

    public static bool SkillAllows(CrystalGrade grade, int skillLevel) =>
        grade != CrystalGrade.None && skillLevel >= RequiredSkillLevel(grade);

    /// <summary>Hero-only gear (equip condition <c>hero: true</c>) is not crystallized.</summary>
    public static bool IsHeroOnly(ItemTemplate item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return RequiresHero(item.Equip);
    }

    static bool RequiresHero(EquipRequirement? requirement) => requirement switch
    {
        EquipRequirement.HeroIs { Expected: true } => true,
        EquipRequirement.AllOf all => all.Parts.Any(RequiresHero),
        EquipRequirement.AnyOf any => any.Parts.Any(RequiresHero),
        EquipRequirement.Negate negated => RequiresHero(negated.Inner),
        _ => false
    };

    /// <summary>Crystals for <paramref name="count"/> destroyed pieces at this enchant.</summary>
    public static int Yield(ItemTemplate item, int enchantLevel, int count)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (count < 1 || !EnchantRules.IsCrystallizable(item))
            return 0;
        return checked(EnchantRules.CrystalCountAt(item, enchantLevel) * count);
    }
}
