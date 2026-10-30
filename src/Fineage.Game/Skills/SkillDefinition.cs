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

    public string Name { get; }

    public bool Passive { get; }

    public string OperateType { get; }

    public string TargetType { get; }

    public int CastRange { get; }

    public int HitTimeMs { get; }

    public int ReuseDelayMs { get; }

    /// <summary>Mobius <c>abnormalTime</c> in seconds. 0 when the skill does not apply an abnormal.</summary>
    public int AbnormalTimeSeconds { get; }

    public bool IsDebuff { get; }

    public bool AppliesAbnormal => AbnormalTimeSeconds > 0;

    public bool IsBuffSkill => AppliesAbnormal && !IsDebuff;

    public bool IsDebuffSkill => AppliesAbnormal && IsDebuff;

    /// <summary>
    /// Instant heal/utility (A1, no abnormal). Distinguished from nukes by <see cref="SkillLevelData.Power"/> == 0
    /// in our datapack (heal power was not imported; attack skills keep non-zero power).
    /// </summary>
    public bool IsInstantSupportSkill(int skillLevel)
    {
        if (Passive || IsDebuff || AppliesAbnormal)
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
