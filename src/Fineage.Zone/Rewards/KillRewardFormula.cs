namespace Fineage.Zone.Rewards;

/// <summary>
/// Mobius <c>Attackable.calculateExpAndSp</c> (<c>Attackable.java:1347</c>) and the party pool from
/// <c>Party.distributeXpAndSp</c> (<c>Party.java:712</c>) — exp/SP are a share of the monster reward, not a lump
/// sum for the last hit.
/// </summary>
public static class KillRewardFormula
{
    /// <summary>Mobius <c>Party.BONUS_EXP_SP</c>: party bonus indexed by <c>members - 1</c>.</summary>
    static readonly double[] BonusExpSp = [1.0, 1.10, 1.20, 1.30, 1.40, 1.50, 2.0, 2.10, 2.20];

    /// <summary>Mobius <c>Character.ini PartyXpCutoffMethod = level</c> + <c>PartyXpCutoffLevel = 20</c>.</summary>
    public const int PartyXpCutoffLevel = 20;

    public static double PartyBonus(int memberCount)
    {
        int i = memberCount - 1;
        if (i < 1)
            return 1;
        if (i >= BonusExpSp.Length)
            i = BonusExpSp.Length - 1;
        return BonusExpSp[i];
    }

    /// <summary>
    /// High-level (85+) penalty on grey-ish mobs: Mobius switches on <c>levelDiff</c> from -3 (0.97) to -10 (0.03).
    /// </summary>
    public static double HighLevelPenalty(int levelDiff) => levelDiff switch
    {
        -3 => 0.97,
        -4 => 0.67,
        -5 => 0.42,
        -6 => 0.25,
        -7 => 0.15,
        -8 => 0.09,
        -9 => 0.05,
        -10 => 0.03,
        _ => 1
    };

    /// <summary>
    /// Mobius <c>calculateExpAndSp(charLevel, damage, totalDamage)</c>: reward scaled by the damage share, zero outside
    /// <c>±MonsterExpMaxLevelDifference</c>, scaled down when the killer is 85+ and the mob is 3+ levels lower.
    /// Java does the share in integer arithmetic, so the truncation is kept.
    /// </summary>
    public static (double Exp, double Sp) CalculateExpAndSp(
        int charLevel, int npcLevel, long rewardExp, int rewardSp, long damage, long totalDamage, int maxLevelDifference)
    {
        int levelDiff = charLevel - npcLevel;
        if (totalDamage <= 0 || levelDiff <= -maxLevelDifference || levelDiff >= maxLevelDifference)
            return (0, 0);

        double exp = Math.Max(0, (double)(rewardExp * damage / totalDamage));
        double sp = Math.Max(0, (double)(rewardSp * damage / totalDamage));
        if (charLevel > 84 && levelDiff <= -3)
        {
            double mul = HighLevelPenalty(levelDiff);
            exp *= mul;
            sp *= mul;
        }

        return (exp, sp);
    }

    /// <summary>Java <c>Math.round</c> (half up) for the exp/SP the client is told about.</summary>
    public static long Round(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
}
