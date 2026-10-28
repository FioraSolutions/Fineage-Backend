namespace Fineage.Game.Skills;

/// <summary>Per-level row transcribed from Mobius skill XML tables.</summary>
public sealed class SkillLevelData
{
    public SkillLevelData(int level, int mpConsume, int power, int abnormalLevel = 1)
    {
        Level = level;
        MpConsume = mpConsume;
        Power = power;
        AbnormalLevel = Math.Max(1, abnormalLevel);
    }

    public int Level { get; }

    public int MpConsume { get; }

    public int Power { get; }

    /// <summary>Mobius <c>abnormalLevel</c> for wire <see cref="Fineage.Protocol.AbnormalEffectMsg.Value"/>.</summary>
    public int AbnormalLevel { get; }
}
