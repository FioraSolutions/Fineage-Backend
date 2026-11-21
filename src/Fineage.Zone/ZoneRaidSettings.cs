namespace Fineage.Zone;

/// <summary>
/// Mobius <c>NPC.ini</c> raid curse (<c>DisableRaidCurse</c>) and the +8 level gap in
/// <c>Creature.reduceCurrentHp</c> / <c>callSkill</c>.
/// </summary>
public static class ZoneRaidSettings
{
    public const string DisableRaidCurseVariable = "FINEAGE_RAID_DISABLE_CURSE";
    public const string LevelGapVariable = "FINEAGE_RAID_CURSE_LEVEL_GAP";
    public const string PhysicalSkillIdVariable = "FINEAGE_RAID_CURSE_PHYSICAL_SKILL_ID";
    public const string MagicSkillIdVariable = "FINEAGE_RAID_CURSE_MAGIC_SKILL_ID";

    /// <summary>Mobius <c>DisableRaidCurse = False</c> → curse enabled.</summary>
    public const bool DefaultDisableRaidCurse = false;

    /// <summary>Mobius: <c>getLevel() &gt; getLevel() + 8</c>.</summary>
    public const int DefaultLevelGap = 8;

    /// <summary>Mobius <c>CommonSkill.RAID_CURSE2</c> (melee and physical skills).</summary>
    public const int DefaultPhysicalSkillId = 4515;

    /// <summary>Mobius <c>CommonSkill.RAID_CURSE</c> (magic skills).</summary>
    public const int DefaultMagicSkillId = 4215;

    public static bool DisableRaidCurse =>
        ZoneNpcSettings.ParseBool(Environment.GetEnvironmentVariable(DisableRaidCurseVariable), DefaultDisableRaidCurse);

    public static int LevelGap =>
        ZoneNpcSettings.ParsePositiveInt(Environment.GetEnvironmentVariable(LevelGapVariable), DefaultLevelGap);

    public static int PhysicalSkillId =>
        ParseSkillId(Environment.GetEnvironmentVariable(PhysicalSkillIdVariable), DefaultPhysicalSkillId);

    public static int MagicSkillId =>
        ParseSkillId(Environment.GetEnvironmentVariable(MagicSkillIdVariable), DefaultMagicSkillId);

    static int ParseSkillId(string? text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int value)
            || value <= 0)
            return fallback;
        return value;
    }
}