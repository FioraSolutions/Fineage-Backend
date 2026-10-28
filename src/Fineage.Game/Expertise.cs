namespace Fineage.Game;

/// <summary>
/// Expertise skill 239. Mobius Interlude grants it with auto-get on login and level-up
/// (skill trees, <c>autoGet="true"</c>), and <c>DecreaseSkillOnDelevel</c> defaults to true
/// with no level slack for this skill. The rank a character actually has is therefore
/// the highest auto-get rank whose learn level they have reached:
/// D at 20, C at 40, B at 52, A at 61, S at 76. Rank 0 is no grade.
/// </summary>
public static class Expertise
{
    public const int SkillId = 239;

    public static int ForLevel(int level)
    {
        if (level >= 76)
            return (int)CrystalGrade.S;
        if (level >= 61)
            return (int)CrystalGrade.A;
        if (level >= 52)
            return (int)CrystalGrade.B;
        if (level >= 40)
            return (int)CrystalGrade.C;
        if (level >= 20)
            return (int)CrystalGrade.D;
        return (int)CrystalGrade.None;
    }
}
