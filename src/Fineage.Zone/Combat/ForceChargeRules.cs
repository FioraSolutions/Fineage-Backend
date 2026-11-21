namespace Fineage.Zone.Combat;

/// <summary>
/// Mobius FocusEnergy (<c>increaseCharges(1, cap)</c>): cap from effect <c>charge</c>
/// (<c>#maxCharges</c> → skill level 1–7, or a literal).
/// </summary>
public static class ForceChargeRules
{
    public const int MaxStack = 7;
    public const int SysMsgForceIncreased = 323;
    public const int SysMsgForceMaxCapacity = 324;

    public static bool TryFindFocusEnergy(SkillEffectCatalog.Effect[] rows, out SkillEffectCatalog.Effect effect)
    {
        foreach (var row in rows)
        {
            if (row.Name.Equals("FocusEnergy", StringComparison.OrdinalIgnoreCase))
            {
                effect = row;
                return true;
            }
        }

        effect = default;
        return false;
    }

    public static bool TryResolveCap(SkillEffectCatalog.Effect effect, int skillId, int skillLevel, out int cap)
    {
        cap = 0;
        if (!effect.Extra.TryGetValue("charge", out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        if (raw.StartsWith('#') || string.Equals(raw, "maxCharges", StringComparison.OrdinalIgnoreCase))
        {
            string key = raw.TrimStart('#');
            if (key.Equals("maxCharges", StringComparison.OrdinalIgnoreCase))
            {
                cap = Math.Clamp(skillLevel, 1, MaxStack);
                return true;
            }

            return false;
        }

        if (!int.TryParse(raw, out int literal) || literal <= 0)
            return false;

        cap = Math.Clamp(literal, 1, MaxStack);
        return true;
    }
}