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
