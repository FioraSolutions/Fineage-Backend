using Fineage.Protocol;

namespace Fineage.Zone.SevenSigns;

public static class SevenSignsRules
{
    public static int RoundHalfUp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;
        if (value >= 0)
            return (int)Math.Floor(value + 0.5);
        return (int)Math.Ceiling(value - 0.5);
    }

    public static int ContributionPoints(int blue, int green, int red)
    {
        long score = (long)blue * SevenSignsProtocol.BlueSealStoneValue
            + (long)green * SevenSignsProtocol.GreenSealStoneValue
            + (long)red * SevenSignsProtocol.RedSealStoneValue;
        if (score is < 0 or > int.MaxValue)
            return -1;
        return (int)score;
    }

    public static int MemberPercent(int choosing, int members)
    {
        int denom = members == 0 ? 1 : members;
        return RoundHalfUp((double)choosing / denom * 100.0);
    }

    /// <summary>
    /// Cabal score is the seal-stone share of <paramref name="scale"/> (500 on Interlude) plus festival points.
    /// </summary>
    public static int CabalScore(long stonePoints, long totalStonePoints, int festivalScore, int scale)
    {
        double ratio = totalStonePoints == 0 ? 0 : (double)stonePoints / totalStonePoints;
        int portion = RoundHalfUp(ratio * scale);
        long total = (long)portion + festivalScore;
        if (total > int.MaxValue)
            return int.MaxValue;
        if (total < int.MinValue)
            return int.MinValue;
        return (int)total;
    }

    public static SevenSignsCabal Highest(int duskScore, int dawnScore)
    {
        if (duskScore == dawnScore)
            return SevenSignsCabal.None;
        return duskScore > dawnScore ? SevenSignsCabal.Dusk : SevenSignsCabal.Dawn;
    }

    /// <summary>
    /// Seal owner after competition. A new owner needs <paramref name="claimPercent"/> (35).
    /// The side that already holds the seal keeps it at <paramref name="retainPercent"/> (10) when the challenger misses 35.
    /// A tie keeps the previous owner at the retain threshold.
    /// </summary>
    public static SevenSignsCabal ResolveOwner(
        SevenSignsCabal previous,
        SevenSignsCabal winner,
        int dawnPercent,
        int duskPercent,
        int claimPercent,
        int retainPercent)
    {
        if (winner != SevenSignsCabal.None &&
            previous != winner &&
            Claims(winner, dawnPercent, duskPercent, claimPercent))
            return winner;

        if (winner != SevenSignsCabal.None &&
            previous == winner &&
            Retains(winner, dawnPercent, duskPercent, retainPercent))
            return winner;

        if (winner != SevenSignsCabal.None &&
            previous != SevenSignsCabal.None &&
            previous != winner &&
            !Claims(winner, dawnPercent, duskPercent, claimPercent) &&
            Retains(previous, dawnPercent, duskPercent, retainPercent))
            return previous;

        if (winner == SevenSignsCabal.None &&
            previous != SevenSignsCabal.None &&
            Retains(previous, dawnPercent, duskPercent, retainPercent))
            return previous;

        return SevenSignsCabal.None;
    }

    public static int SkyState(SevenSignsPeriod period, SevenSignsCabal winner)
    {
        if (period != SevenSignsPeriod.SealValidation)
            return SevenSignsProtocol.SkyNormal;
        return winner switch
        {
            SevenSignsCabal.Dusk => SevenSignsProtocol.SkyDusk,
            SevenSignsCabal.Dawn => SevenSignsProtocol.SkyDawn,
            _ => SevenSignsProtocol.SkyNormal
        };
    }

    static bool Claims(SevenSignsCabal side, int dawnPercent, int duskPercent, int claimPercent) =>
        side switch
        {
            SevenSignsCabal.Dawn => dawnPercent >= claimPercent,
            SevenSignsCabal.Dusk => duskPercent >= claimPercent,
            _ => false
        };

    static bool Retains(SevenSignsCabal side, int dawnPercent, int duskPercent, int retainPercent) =>
        side switch
        {
            SevenSignsCabal.Dawn => dawnPercent >= retainPercent,
            SevenSignsCabal.Dusk => duskPercent >= retainPercent,
            _ => false
        };
}

/// <summary>Avarice, gnosis, and strife outcomes as data. Nothing here changes combat, spawns, or skills.</summary>
public sealed class SevenSignsEffects
{
    public bool ResultsOrValidation { get; init; }

    public bool SealValidation { get; init; }

    public SevenSignsCabal AvariceOwner { get; init; }

    public SevenSignsCabal GnosisOwner { get; init; }

    public SevenSignsCabal StrifeOwner { get; init; }

    public SevenSignsCabal WinningCabal { get; init; }

    public bool MammonMerchant { get; init; }

    public bool MammonBlacksmith { get; init; }

    public bool Preachers { get; init; }

    public bool Lilith { get; init; }

    public bool Anakim { get; init; }

    public double StrifeGatePhysical { get; init; } = 1;

    public double StrifeGateMagic { get; init; } = 1;

    /// <summary>Dusk cannot summon while Dawn holds Strife during seal validation.</summary>
    public bool DuskSummonBlocked { get; init; }

    public SevenSignsStrifeRole StrifeRole(SevenSignsCabal cabal)
    {
        if (!SealValidation || StrifeOwner == SevenSignsCabal.None || cabal == SevenSignsCabal.None)
            return SevenSignsStrifeRole.None;
        return cabal == StrifeOwner ? SevenSignsStrifeRole.Victor : SevenSignsStrifeRole.Vanquished;
    }

    public bool BlocksSummon(SevenSignsCabal cabal) =>
        DuskSummonBlocked && cabal == SevenSignsCabal.Dusk;
}
