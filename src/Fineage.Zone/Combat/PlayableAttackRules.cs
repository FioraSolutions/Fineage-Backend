namespace Fineage.Zone.Combat;

/// <summary>
/// Mobius <c>PlayableAI</c> gates on attack and on a bad cast against another player.
/// <c>ZoneId.PVP</c> is not imported, so the blessing check applies everywhere
/// (Mobius skips it only inside a PvP zone).
/// </summary>
public static class PlayableAttackRules
{
    /// <summary>Skill 5182 Blessing of Protection (<c>EffectFlag.PROTECTION_BLESSING</c>).</summary>
    public const int BlessingSkillId = 5182;

    public const int BlessingLevelGap = 10;
    public const int CursedWeaponLevel = 20;

    public static bool Refused(
        bool bothPlayers,
        int attackerLevel,
        int targetLevel,
        int attackerKarma,
        int targetKarma,
        bool attackerBlessed,
        bool targetBlessed,
        bool attackerCursed,
        bool targetCursed,
        bool badSkill)
    {
        if (!bothPlayers)
            return false;

        bool blessing =
            (targetBlessed && attackerLevel - targetLevel >= BlessingLevelGap && attackerKarma > 0)
            || (attackerBlessed && targetKarma > 0 && targetLevel - attackerLevel >= BlessingLevelGap);
        if (blessing)
            return true;

        if (badSkill)
            return targetCursed && (attackerLevel <= CursedWeaponLevel || targetLevel <= CursedWeaponLevel);

        if (targetCursed && attackerLevel <= CursedWeaponLevel)
            return true;
        return attackerCursed && targetLevel <= CursedWeaponLevel;
    }
}
