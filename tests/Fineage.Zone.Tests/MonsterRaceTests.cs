using Fineage.Protocol;
using Fineage.Zone.MonsterRace;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class MonsterRaceTests
{
    [Fact]
    public void Odds_match_mobius_pool_share()
    {
        var bets = new Dictionary<int, long> { [1] = 1000, [2] = 0, [3] = 500, [4] = 0, [5] = 0, [6] = 0, [7] = 0, [8] = 0 };
        var odds = MonsterRaceLogic.CalculateOdds(bets, 0.7, 1.25);
        Assert.Equal(1.05, odds[0], 2);
        Assert.Equal(0, odds[1]);
        Assert.Equal(2.1, odds[2], 2);
    }

    [Fact]
    public void Odds_floor_at_min_odd_when_lane_has_bets()
    {
        var bets = new Dictionary<int, long> { [1] = 1_000_000, [2] = 0, [3] = 0, [4] = 0, [5] = 0, [6] = 0, [7] = 0, [8] = 0 };
        var odds = MonsterRaceLogic.CalculateOdds(bets, 0.7, 1.25);
        Assert.Equal(1.25, odds[0]);
    }

    [Fact]
    public void Payout_winner_uses_race_odd_loser_gets_one_percent()
    {
        Assert.Equal(500, MonsterRaceLogic.PayoutAmount(100, 5.0, true));
        Assert.Equal(1, MonsterRaceLogic.PayoutAmount(100, 5.0, false));
    }

    [Fact]
    public void Countdown_wraps_after_configured_second()
    {
        long start = 1_000_000;
        Assert.Equal(0, MonsterRaceLogic.CountdownAt(start, start, 1200));
        Assert.Equal(600, MonsterRaceLogic.CountdownAt(start, start + 600_000, 1200));
        Assert.Equal(0, MonsterRaceLogic.CountdownAt(start, start + 1201_000, 1200));
    }

    [Fact]
    public void RollSpeeds_picks_first_and_second_by_total_distance()
    {
        var random = new Random(42);
        var (speeds, first, second) = MonsterRaceLogic.RollSpeeds(random);
        Assert.Equal(8, speeds.Length);
        Assert.All(speeds, lane => Assert.Equal(20, lane.Length));
        Assert.True(speeds[first][19] == 100);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Board_fires_close_bets_at_second_900()
    {
        var store = new MemoryMonsterRaceStore();
        store.Save(new MonsterRaceSnapshot { CycleStartUnixMs = 900_000, LastHandledCountdown = 899 });
        var board = new MonsterRaceBoard(
            0,
            TestCatalogs.Game.Items,
            TestCatalogs.Npcs,
            () => 900_000,
            MonsterRaceSettings.Mobius,
            store);
        board.Tick(900_000, new Random(1));
        Assert.Equal(MonsterRaceState.Waiting, board.State);
    }
}