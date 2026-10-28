using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

/// <summary>
/// Registration checks and the point transfer in Mobius <c>OlympiadGame.validateWinner</c>.
/// Classed divides by 3. Non-classed divides by 5. Both are capped by <c>OlympiadMaxPoints</c>.
/// </summary>
public static class OlympiadRules
{
    public readonly record struct Score(int Points, int Done, int Won, int Lost, int Drawn);

    public readonly record struct Transfer(
        Score One,
        Score Two,
        int DeltaOne,
        int DeltaTwo,
        int Outcome,
        int WinnerObjectId,
        int RewardCount);

    public static int PointDiff(int pointsOne, int pointsTwo, bool classed, int maxPoints)
    {
        int divisor = classed ? 3 : 5;
        return Math.Min(Math.Min(pointsOne, pointsTwo) / divisor, maxPoints);
    }

    public static int DefaultLoss(int points, int maxPoints) => Math.Min(points / 3, maxPoints);

    public static int TieLoss(int points, int maxPoints) => Math.Min(points / 5, maxPoints);

    /// <summary>
    /// Load and weekly cap: <c>competitions * max + weekly * 4</c>.
    /// A fresh noble is stored at the start-point value and only hits this cap on load or the weekly add.
    /// </summary>
    public static int ClampStored(int points, int competitionsDone, int maxPoints, int weeklyPoints) =>
        Math.Clamp(points, 0, competitionsDone * maxPoints + weeklyPoints * 4);

    public static int AddWeekly(int points, int competitionsDone, int maxPoints, int weeklyPoints) =>
        ClampStored(points + weeklyPoints, competitionsDone, maxPoints, weeklyPoints);

    public static bool InventoryBlocks(int slots, int limit) => limit * 0.8d <= slots;

    public static string? RegisterBlock(OlympiadRegisterCheck check)
    {
        if (!check.Enabled)
            return OlympiadProtocol.Disabled;
        if (!check.InCompetition)
            return OlympiadProtocol.Period;
        if (!check.Noble)
            return OlympiadProtocol.Noble;
        if (check.ClassId != check.BaseClassId)
            return OlympiadProtocol.Subclass;
        if (check.CursedWeapon)
            return OlympiadProtocol.Cursed;
        if (InventoryBlocks(check.InventorySlots, check.InventoryLimit))
            return OlympiadProtocol.Inventory;
        if (check.MillisToCompEnd < check.RegistrationCloseMillis)
            return OlympiadProtocol.Late;
        if (check.OnClassedList)
            return OlympiadProtocol.Classed;
        if (check.InCompetitionRegistration)
            return OlympiadProtocol.Registered;
        int floor = check.WantsClassed ? check.ClassedFloor : check.NonClassedFloor;
        if (check.Points < floor)
            return OlympiadProtocol.Points;
        return null;
    }

    public static Transfer Win(Score one, Score two, bool oneWins, bool classed, int maxPoints, int rewardCount, int winnerObjectId)
    {
        int diff = PointDiff(one.Points, two.Points, classed, maxPoints);
        if (oneWins)
        {
            return new Transfer(
                one with { Points = one.Points + diff, Done = one.Done + 1, Won = one.Won + 1 },
                two with { Points = two.Points - diff, Done = two.Done + 1, Lost = two.Lost + 1 },
                diff,
                -diff,
                OlympiadProtocol.OutcomeWinOne,
                winnerObjectId,
                rewardCount);
        }

        return new Transfer(
            one with { Points = one.Points - diff, Done = one.Done + 1, Lost = one.Lost + 1 },
            two with { Points = two.Points + diff, Done = two.Done + 1, Won = two.Won + 1 },
            -diff,
            diff,
            OlympiadProtocol.OutcomeWinTwo,
            winnerObjectId,
            rewardCount);
    }

    /// <summary>Disconnect after the arena port. The winner gains points. The gate pass is not given.</summary>
    public static Transfer Crash(Score one, Score two, bool oneLeft, bool twoLeft, bool classed, int maxPoints, int oneId, int twoId)
    {
        int diff = PointDiff(one.Points, two.Points, classed, maxPoints);
        if (oneLeft && twoLeft)
        {
            return new Transfer(
                one with { Points = one.Points - diff, Done = one.Done + 1, Lost = one.Lost + 1 },
                two with { Points = two.Points - diff, Done = two.Done + 1, Lost = two.Lost + 1 },
                -diff,
                -diff,
                OlympiadProtocol.OutcomeCrashBoth,
                OlympiadProtocol.NoWinner,
                0);
        }

        if (oneLeft)
        {
            return new Transfer(
                one with { Points = one.Points - diff, Done = one.Done + 1, Lost = one.Lost + 1 },
                two with { Points = two.Points + diff, Done = two.Done + 1, Won = two.Won + 1 },
                -diff,
                diff,
                OlympiadProtocol.OutcomeCrashOne,
                twoId,
                0);
        }

        return new Transfer(
            one with { Points = one.Points + diff, Done = one.Done + 1, Won = one.Won + 1 },
            two with { Points = two.Points - diff, Done = two.Done + 1, Lost = two.Lost + 1 },
            diff,
            -diff,
            OlympiadProtocol.OutcomeCrashTwo,
            oneId,
            0);
    }

    /// <summary>HP-less tie. Each noble loses <c>points / 5</c>, capped, and the match is a draw.</summary>
    public static Transfer Tie(Score one, Score two, int maxPoints)
    {
        int lossOne = TieLoss(one.Points, maxPoints);
        int lossTwo = TieLoss(two.Points, maxPoints);
        return new Transfer(
            one with { Points = one.Points - lossOne, Done = one.Done + 1, Drawn = one.Drawn + 1 },
            two with { Points = two.Points - lossTwo, Done = two.Done + 1, Drawn = two.Drawn + 1 },
            -lossOne,
            -lossTwo,
            OlympiadProtocol.OutcomeTie,
            OlympiadProtocol.NoWinner,
            0);
    }

    /// <summary>Left or failed the requirements before the arena port. Competitions done does not increase.</summary>
    public static Transfer Default(Score one, Score two, bool oneDefault, bool twoDefault, int maxPoints)
    {
        int lossOne = oneDefault ? DefaultLoss(one.Points, maxPoints) : 0;
        int lossTwo = twoDefault ? DefaultLoss(two.Points, maxPoints) : 0;
        int outcome = oneDefault && twoDefault
            ? OlympiadProtocol.OutcomeDefaultBoth
            : oneDefault ? OlympiadProtocol.OutcomeDefaultOne : OlympiadProtocol.OutcomeDefaultTwo;
        return new Transfer(
            one with { Points = one.Points - lossOne },
            two with { Points = two.Points - lossTwo },
            -lossOne,
            -lossTwo,
            outcome,
            OlympiadProtocol.NoWinner,
            0);
    }
}

public readonly record struct OlympiadRegisterCheck(
    bool Enabled,
    bool InCompetition,
    bool Noble,
    int ClassId,
    int BaseClassId,
    bool CursedWeapon,
    int InventorySlots,
    int InventoryLimit,
    long MillisToCompEnd,
    long RegistrationCloseMillis,
    bool OnClassedList,
    bool InCompetitionRegistration,
    bool WantsClassed,
    int Points,
    int ClassedFloor,
    int NonClassedFloor);
