namespace Fineage.Zone.MonsterRace;

/// <summary>Pure Mobius <c>MonsterRaceManager</c> math for tests and the board.</summary>
public static class MonsterRaceLogic
{
    public static IReadOnlyList<double> CalculateOdds(IReadOnlyDictionary<int, long> betsPerLane, double poolShare, double minOdd)
    {
        long sum = 0;
        var lanes = new int[8];
        for (int i = 0; i < 8; i++)
        {
            lanes[i] = i + 1;
            sum += betsPerLane.TryGetValue(lanes[i], out long v) ? v : 0;
        }

        var odds = new double[8];
        for (int i = 0; i < 8; i++)
        {
            long amount = betsPerLane.TryGetValue(lanes[i], out long v) ? v : 0;
            odds[i] = amount == 0 ? 0 : Math.Max(minOdd, (sum * poolShare) / amount);
        }

        return odds;
    }

    public static (int[][] speeds, int firstLane, int secondLane) RollSpeeds(Random random)
    {
        var speeds = new int[8][];
        int winnerDistance = 0, secondDistance = 0, firstPlace = 0, secondPlace = 0;
        for (int lane = 0; lane < 8; lane++)
        {
            speeds[lane] = new int[20];
            int total = 0;
            for (int seg = 0; seg < 20; seg++)
            {
                speeds[lane][seg] = seg == 19 ? 100 : random.Next(60) + 65;
                total += speeds[lane][seg];
            }

            if (total >= winnerDistance)
            {
                secondPlace = firstPlace;
                secondDistance = winnerDistance;
                firstPlace = lane;
                winnerDistance = total;
            }
            else if (total >= secondDistance)
            {
                secondPlace = lane;
                secondDistance = total;
            }
        }

        return (speeds, firstPlace, secondPlace);
    }

    public static int PayoutAmount(int betAdena, double oddRate, bool winner, double loserPayoutRate) =>
        (int)(betAdena * (winner ? oddRate : loserPayoutRate));

    /// <summary>Seconds elapsed within the Mobius cycle (countdown 0 at cycle start).</summary>
    public static int CountdownAt(long cycleStartUnixMs, long nowUnixMs, int wrapAbove)
    {
        if (nowUnixMs < cycleStartUnixMs)
            return 0;
        int elapsed = (int)((nowUnixMs - cycleStartUnixMs) / 1000);
        if (elapsed > wrapAbove)
            elapsed %= wrapAbove + 1;
        return elapsed;
    }
}