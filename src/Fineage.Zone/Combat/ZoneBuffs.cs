namespace Fineage.Zone.Combat;

/// <summary>Sample abnormal ids for dual-client playtests until skill data lands.</summary>
public static class ZoneBuffs
{
    /// <summary>Wind Walk skill/abnormal id (Mobius 1204).</summary>
    public const int SampleWindWalk = 1204;

    public const int SampleEffectValue = 1;
    /// <summary>Fallback when playtest effect id is not in <c>data/skills.json</c>.</summary>
    public const int SampleDurationSeconds = 1200;

    /// <summary>Chat <c>//mbuff</c> — mage-oriented self-buff pack (max skill level).</summary>
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
        276,  // Dance of Concentration (cast speed)
        1356, // Prophecy of Fire
    ];

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
    ];
}
