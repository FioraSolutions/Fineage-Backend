namespace Fineage.Game.Skills;

/// <summary>One Interlude skill template (all levels).</summary>
public sealed class SkillDefinition
{
    readonly SkillLevelData[] _levels;

    public SkillDefinition(
        int skillId,
        string name,
        bool passive,
        string operateType,
        string targetType,
        int castRange,
        int hitTimeMs,
        int reuseDelayMs,
        int abnormalTimeSeconds,
        bool isDebuff,
        SkillLevelData[] levels)
    {
        SkillId = skillId;
        Name = name;
        Passive = passive;
        OperateType = operateType;
        TargetType = targetType;
        CastRange = castRange;
        HitTimeMs = hitTimeMs;
        ReuseDelayMs = reuseDelayMs;
        AbnormalTimeSeconds = Math.Max(0, abnormalTimeSeconds);
        IsDebuff = isDebuff;
        _levels = levels;
    }

    public int SkillId { get; }

    /// <summary>Mobius isMagic: 0 physical, 1 magic, 2 static.</summary>
    public int MagicType { get; init; }

    /// <summary>Mobius effectRange (escape range at launch); 0 = use <see cref="CastRange"/>.</summary>
    public int EffectRange { get; init; }

    /// <summary>Mobius <c>affectRange</c> for AURA / AREA / FRONT_AREA. 0 = use the family default.</summary>
    public int AffectRange { get; init; }

    public string Name { get; }

    public bool Passive { get; }

    public string OperateType { get; }

    public string TargetType { get; }

    public int CastRange { get; }

    public int HitTimeMs { get; }

    public int ReuseDelayMs { get; }

    /// <summary>Template reuse, or the per-level table when this rank lists its own delay.</summary>
    public int ReuseDelayMsAt(int level) =>
        TryLevelData(level, out var data) && data.ReuseDelayMs is int ms ? Math.Max(0, ms) : ReuseDelayMs;


    /// <summary>Mobius <c>abnormalTime</c> in seconds. 0 when the skill does not apply an abnormal.</summary>
    public int AbnormalTimeSeconds { get; }

    public bool IsDebuff { get; }

    /// <summary>Mobius <c>abnormalType</c> slot (POISON, TURN_FLEE, BLEEDING, …). Empty when unset.</summary>
    public string AbnormalType { get; init; } = "";

    /// <summary>Mobius <c>abnormalVisualEffect</c> (STEALTH, DOT_POISON, FLESH_STONE, …). Empty when unset.</summary>
    public string AbnormalVisualEffect { get; init; } = "";

    /// <summary>Mobius <c>trait</c> (POISON, HOLD, DERANGEMENT, …). Empty when unset.</summary>
    public string Trait { get; init; } = "";

    /// <summary>
    /// Mobius <c>activateRate</c> for <c>calcSkillSuccess</c>.
    /// −1 = always land (no roll); 0 = missing / treat as always land for non-debuffs.
    /// </summary>
    public int ActivateRate { get; init; }

    /// <summary>Mobius <c>lvlBonusRate</c> in calcSkillSuccess (default 1).</summary>
    public int LvlBonusRate { get; init; } = 1;

    /// <summary>Mobius <c>basicProperty</c> (CON, MEN, …). Empty = none → always land.</summary>
    public string BasicProperty { get; init; } = "";

    /// <summary>
    /// Mobius <c>effectPoint</c> hate the hit itself writes on an attackable victim, per skill level (a scalar row is
    /// repeated). <c>onMagicHitTimer</c> runs <c>addDamageHate(this, 0, −effectPoint)</c> when it is negative and
    /// <c>reduceHate(this, effectPoint)</c> when positive, before the skill's <c>&lt;for&gt;</c> payloads. Empty = flat 0.
    /// </summary>
    public IReadOnlyList<int> EffectPoints { get; init; } = [];

    /// <summary>The cast-level entry of <see cref="EffectPoints"/> (0 when the row has none).</summary>
    public int EffectPointAt(int skillLevel)
    {
        if (EffectPoints.Count == 0)
            return 0;
        int index = Math.Clamp(Math.Max(1, skillLevel), 1, EffectPoints.Count) - 1;
        return EffectPoints[index];
    }

    /// <summary>
    /// Mobius <c>&lt;cond msgId="113"&gt;&lt;using kind="DAGGER,DUALDAGGER"/&gt;&lt;/cond&gt;</c>: the weapon kinds the
    /// cast needs. Empty = no gate. Applies to every skill Mobius parses the condition for, derangement utilities
    /// (Bluff 358) included — not only damage nukes.
    /// </summary>
    public string[] UsingKinds { get; init; } = [];

    public bool AppliesAbnormal => AbnormalTimeSeconds > 0;

    public bool IsBuffSkill => AppliesAbnormal && !IsDebuff;

    public bool IsDebuffSkill => AppliesAbnormal && IsDebuff;

    /// <summary>
    /// Instant heal/utility (A1, no abnormal). Distinguished from nukes by <see cref="SkillLevelData.Power"/> == 0
    /// in our datapack (heal power was not imported; attack skills keep non-zero power).
    /// </summary>
    /// <summary>
    /// Mobius <c>IsInstantSupportSkill</c> guard: a skill whose <c>effectPoint</c> is negative is <c>isBad()</c> in
    /// Mobius, i.e. offensive, no matter what its targetType says. Trick (11) / Switch (12) / Confusion (2) /
    /// Forget (1156) are A1 ONE-target skills with power 0 and a negative effectPoint; they must never be treated as
    /// a friendly heal.
    /// </summary>
    public bool IsInstantSupportSkill(int skillLevel)
    {
        if (Passive || IsDebuff || AppliesAbnormal)
            return false;
        if (EffectPointAt(skillLevel) < 0)
            return false;
        if (!IsFriendlyTargetType(TargetType))
            return false;
        if (!TryLevelData(skillLevel, out var data))
            return false;
        return data.Power <= 0;
    }

    public static bool IsFriendlyTargetType(string? targetType)
    {
        if (string.IsNullOrWhiteSpace(targetType))
            return false;
        return targetType.Equals("SELF", StringComparison.OrdinalIgnoreCase)
            || targetType.Equals("ONE", StringComparison.OrdinalIgnoreCase)
            || targetType.Equals("PARTY", StringComparison.OrdinalIgnoreCase)
            || targetType.Equals("SERVITOR", StringComparison.OrdinalIgnoreCase)
            || targetType.Equals("PET", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Mobius <c>&lt;player elementSeeds|elementSeed|active_effect_id_lvl&gt;</c> gates (all must pass).</summary>
    public IReadOnlyList<SkillPlayerCondition> PlayerConditions { get; init; } = [];

    /// <summary>Mobius <c>channelingTickInterval</c> in milliseconds (CA1 ticks). 0 = not a channeled skill.</summary>
    public int ChannelingTickMs { get; init; }

    /// <summary>Mobius <c>mpPerChanneling</c> spent on each CA1 tick after the initial consume.</summary>
    public int MpPerChanneling { get; init; }

    /// <summary>Mobius <c>channelingSkillId</c> (CA5 Spell/Battle Force applies this skill to the target).</summary>
    public int ChannelingSkillId { get; init; }

    /// <summary>CA1 ground-damage channels (Volcano / Gehenna). CA5 force channels use <see cref="ChannelingSkillId"/>.</summary>
    public bool IsGroundDamageChannel =>
        OperateType.StartsWith("CA1", StringComparison.OrdinalIgnoreCase) ||
        (ChannelingTickMs > 0 && TargetType.Equals("GROUND", StringComparison.OrdinalIgnoreCase));

    public int MaxLevel => _levels.Length;

    public SkillLevelData LevelData(int level)
    {
        if (level < 1 || level > _levels.Length)
            throw new ArgumentOutOfRangeException(nameof(level));
        return _levels[level - 1];
    }

    public bool TryLevelData(int level, out SkillLevelData data)
    {
        if (level < 1 || level > _levels.Length)
        {
            data = null!;
            return false;
        }

        data = _levels[level - 1];
        return true;
    }
}
