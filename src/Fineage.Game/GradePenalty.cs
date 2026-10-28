namespace Fineage.Game;

/// <summary>
/// Weapon and armor expertise penalties, each clamped to 0–4.
/// Computed the way Mobius Interlude <c>refreshExpertisePenalty</c> does when
/// <c>ExpertisePenalty</c> is true: the highest equipped crystal level above the
/// Expertise rank, separately for weapons and for everything else, minus that rank
/// and any penalty bonus. Arrows are ignored. Equipping is still allowed.
/// <para>
/// TODO: skill 4267 (Grade Penalty) is not applied to stats. At penalty levels 1–4
/// Mobius multiplies physical/magic attack speed, evasion, exp, run speed, and
/// HP/CP/MP regen by 0.22, 0.19, 0.16, and 0.13, and subtracts 20 accuracy.
/// This server has no stat sheet for those, so combat, movement, and regen are unchanged.
/// <see cref="SkillLevel"/> is the level that skill would be added at.
/// </para>
/// </summary>
public readonly record struct GradePenalty(int Weapon, int Armor)
{
    public const int SkillId = 4267;
    public const int MaxLevel = 4;

    /// <summary>Greater of the two penalties, or 0 when both are 0. This is skill 4267's level.</summary>
    public int SkillLevel => Weapon == 0 && Armor == 0 ? 0 : Math.Max(Weapon, Armor);

    public static GradePenalty Compute(IEnumerable<ItemInstance> items, int expertiseLevel, int bonus, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!enabled)
            return default;

        int weaponCrystal = 0;
        int armorCrystal = 0;
        foreach (var item in items)
        {
            if (!item.IsEquipped || item.Template.EtcType == EtcItemType.Arrow)
                continue;

            int crystal = (int)item.Template.Crystal;
            if (crystal <= expertiseLevel)
                continue;

            if (item.Template.Kind == ItemKind.Weapon)
            {
                if (crystal > weaponCrystal)
                    weaponCrystal = crystal;
            }
            else if (crystal > armorCrystal)
            {
                armorCrystal = crystal;
            }
        }

        return new GradePenalty(Clamp(weaponCrystal - expertiseLevel - bonus), Clamp(armorCrystal - expertiseLevel - bonus));
    }

    static int Clamp(int value) => Math.Clamp(value, 0, MaxLevel);
}
