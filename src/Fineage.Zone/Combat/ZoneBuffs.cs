namespace Fineage.Zone.Combat;

/// <summary>Sample abnormal ids for dual-client playtests until skill data lands.</summary>
public static class ZoneBuffs
{
    /// <summary>Wind Walk skill/abnormal id (Mobius 1204) — used by persist/playtest fixtures.</summary>
    public const int SampleWindWalk = 1204;

    /// <summary>
    /// Chat <c>//mbuff</c> — mage-oriented self-buff pack (max skill level).
    /// Includes SWS songs / BD dances that already have combat funcs in skill-stat-effects.json
    /// (physical-only songs/dances stay out of this pack).
    /// </summary>
    public static readonly int[] MageSelfBuffs =
    [
        1204, // Wind Walk
        1040, // Shield
        1035, // Mental Shield
        1036, // Magic Barrier
        1045, // Blessed Body
        1048, // Blessed Soul
        1059, // Empower
        1078, // Concentration
        1085, // Acumen
        1259, // Resist Shock
        1303, // Wild Magic
        1356, // Prophecy of Fire
        // SWS songs (combat funcs present)
        268,  // Song of Wind — runSpd +20
        267,  // Song of Warding — mDef ×1.3
        264,  // Song of Earth — pDef ×1.25
        266,  // Song of Water — rEvas +3
        // BD dances (combat funcs present)
        273,  // Dance of the Mystic — mAtk ×1.2
        276,  // Dance of Concentration — mAtkSpd ×1.3
        365,  // Siren's Dance — mCritRate ×2
    ];

    /// <summary>
    /// <c>//mbuff</c> / <c>//fbuff</c> hold time. Real casts keep <c>skills.json</c> abnormalTime
    /// (songs stay 120s when an SWS sings them).
    /// </summary>
    public const int AdminPackDurationSeconds = 3600;

    /// <summary>Chat <c>//fbuff</c> — fighter-oriented self-buff pack (max skill level).</summary>
    public static readonly int[] FighterSelfBuffs =
    [
        1204, // Wind Walk
        1040, // Shield
        1068, // Might
        1086, // Haste
        1077, // Focus
        1087, // Agility
        1240, // Guidance
        1242, // Death Whisper
        1243, // Bless Shield
        1045, // Blessed Body
        1388, // Greater Might
        1389, // Greater Shield
        268,  // Song of Wind — runSpd +20 (admin pack lasts an hour; a real SWS cast stays 120s)
        1062, // Berserker Spirit
    ];
}
