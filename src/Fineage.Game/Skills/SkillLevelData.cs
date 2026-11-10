namespace Fineage.Game.Skills;

/// <summary>Per-level row transcribed from Mobius skill XML tables.</summary>
public sealed class SkillLevelData
{
    public SkillLevelData(int level, int mpConsume, int power, int abnormalLevel = 1, int magicLevel = 0)
    {
        Level = level;
        MpConsume = mpConsume;
        Power = power;
        AbnormalLevel = Math.Max(1, abnormalLevel);
        MagicLevel = Math.Max(0, magicLevel);
    }

    public int Level { get; }

    public int MpConsume { get; }

    public int Power { get; }

    /// <summary>Mobius <c>abnormalLevel</c> for wire <see cref="Fineage.Protocol.AbnormalEffectMsg.Value"/>.</summary>
    public int AbnormalLevel { get; }

    /// <summary>Mobius <c>magicLevel</c> for calcSkillSuccess / Lethal. 0 = unknown.</summary>
    public int MagicLevel { get; }
}
