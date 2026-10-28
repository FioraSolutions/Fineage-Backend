namespace Fineage.Game.Skills;

/// <summary>Per-level row transcribed from Mobius skill XML tables.</summary>
public sealed class SkillLevelData
{
    public SkillLevelData(int level, int mpConsume, int power)
    {
        Level = level;
        MpConsume = mpConsume;
        Power = power;
    }

    public int Level { get; }

    public int MpConsume { get; }

    public int Power { get; }
}
