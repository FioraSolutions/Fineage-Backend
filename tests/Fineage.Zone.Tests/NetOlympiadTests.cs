using Fineage.Game;
using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Olympiad;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Olympiad over Net: the period sheet, the Noblesse gate on registration, the queue and its cancel, and the
/// admin report gate. The world runs on <see cref="WorldBench"/>, so these assert the recorded
/// <see cref="Fineage.Zone.World.IWorldEventPublisher"/> fanout rather than MessagePack frames.
/// </summary>
public sealed class NetOlympiadTests
{
    static readonly DateTimeOffset Evening = new(2026, 10, 28, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Status_register_and_unregister_follow_the_noble_check()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("oly", "Oly", classId: HumanClasses.Fighter);

        Assert.Null(await bench.World.TryOlympiadStatusAsync(actor.Session, CancellationToken.None));
        var start = LastSystemMessage(bench);
        Assert.Equal(OlympiadProtocol.CompetitionStarted, start.Id);
        var status = LastStatus(bench);
        Assert.False(status.Noble);
        Assert.True(status.InCompetition);
        Assert.True(status.RegistrationOpen);
        Assert.Equal(0, status.Points);
        Assert.Equal(OlympiadProtocol.QueueNone, status.Queue);

        // A commoner may not enter the classed bracket: the refusal is a line plus a coded error.
        Assert.Equal(
            OlympiadProtocol.Noble,
            await bench.World.TryOlympiadRegisterAsync(
                actor.Session, new OlympiadRegisterRequestMsg { Classed = 1 }, CancellationToken.None));
        Assert.Equal(OlympiadProtocol.NoblesOnly, LastSystemMessage(bench).Id);

        Assert.True(bench.World.GrantNoblesse(actor.Ticket.CharacterId));
        Assert.Null(await bench.World.TryOlympiadRegisterAsync(
            actor.Session, new OlympiadRegisterRequestMsg { Classed = 1 }, CancellationToken.None));
        Assert.Equal(OlympiadProtocol.RegisteredClassed, LastSystemMessage(bench).Id);
        var queued = LastStatus(bench);
        Assert.True(queued.Noble);
        Assert.Equal(18, queued.Points);
        Assert.Equal(OlympiadProtocol.QueueClassed, queued.Queue);

        Assert.Null(await bench.World.TryOlympiadUnregisterAsync(actor.Session, CancellationToken.None));
        Assert.Equal(OlympiadProtocol.Unregistered, LastSystemMessage(bench).Id);
        Assert.Equal(OlympiadProtocol.QueueNone, LastStatus(bench).Queue);
    }

    [Fact]
    public async Task A_match_fans_out_to_both_participants_with_its_countdown_and_result()
    {
        var clock = Evening;
        // ClassedMin 2 and a 5 s wait keep the whole countdown → fight walk inside the test's own clock.
        var bench = new WorldBench(
            clock: () => clock,
            olympiadSettings: new OlympiadSettings { ClassedMin = 2, WaitSeconds = 5 });
        BenchPlayer one = await bench.EnterAsync("oly1", "One", classId: HumanClasses.Fighter);
        BenchPlayer two = await bench.EnterAsync("oly2", "Two", classId: HumanClasses.Fighter);
        Assert.True(bench.World.GrantNoblesse(one.Ticket.CharacterId));
        Assert.True(bench.World.GrantNoblesse(two.Ticket.CharacterId));

        Assert.Null(await bench.World.TryOlympiadRegisterAsync(
            one.Session, new OlympiadRegisterRequestMsg { Classed = 1 }, CancellationToken.None));
        Assert.Null(await bench.World.TryOlympiadRegisterAsync(
            two.Session, new OlympiadRegisterRequestMsg { Classed = 1 }, CancellationToken.None));

        // Two nobles in one bracket make a match; the note goes out once, addressed to both participants.
        // The first advance parks matchmaking for the idle window because the queues were still empty.
        clock = clock.AddSeconds(61);
        await bench.World.AdvanceOlympiadAsync(CancellationToken.None);
        var matchCall = Assert.Single(bench.Rec.Of("FanoutOlympiadMatch"));
        Assert.Equal(2, Assert.IsAssignableFrom<IReadOnlyList<int>>(matchCall[0]).Count);
        var match = Assert.IsType<OlympiadMatchNetMsg>(matchCall[1]);
        // Which side each participant takes is a random draw, so the pair is asserted as a set.
        Assert.Equal(
            new[] { one.ObjectId, two.ObjectId }.Order(),
            new[] { match.PlayerOneId, match.PlayerTwoId }.Order());
        Assert.Equal(new[] { "One", "Two" }.Order(), new[] { match.PlayerOneName, match.PlayerTwoName }.Order());
        Assert.True(match.Classed);

        // Countdown (WaitSeconds) → arena pause → fight countdown → fighting.
        clock = clock.AddSeconds(OlympiadProtocol.WaitSeconds);
        await bench.World.AdvanceOlympiadAsync(CancellationToken.None);
        Assert.NotEmpty(bench.Rec.Of("FanoutOlympiadCountdown"));
        var countdown = Assert.IsType<OlympiadCountdownNetMsg>(bench.Rec.Of("FanoutOlympiadCountdown").Last()[1]);
        Assert.Equal(match.MatchId, countdown.MatchId);
        Assert.Equal(1, countdown.Seconds);

        clock = clock.AddMilliseconds(OlympiadProtocol.ArenaPauseMillis);
        await bench.World.AdvanceOlympiadAsync(CancellationToken.None);
        clock = clock.AddMilliseconds(OlympiadProtocol.BattleCountdownMillis);
        await bench.World.AdvanceOlympiadAsync(CancellationToken.None);

        // Report the first side as the winner; the pair order is drawn at matchmaking.
        int winner = match.PlayerOneId;
        Assert.Null(bench.World.ReportOlympiadResult(match.MatchId, winner, clock));
        var result = Assert.IsType<OlympiadResultNetMsg>(bench.Rec.Of("FanoutOlympiadResult").Last()[1]);
        Assert.Equal(match.MatchId, result.MatchId);
        Assert.Equal(winner, result.WinnerObjectId);
        Assert.Equal(OlympiadProtocol.RewardItemId, result.RewardItemId);
        Assert.Equal(OlympiadProtocol.ClassedRewardCount, result.RewardCount);
        Assert.True(result.DeltaOne > 0);
        Assert.Equal(1, result.CompetitionsDoneOne);
        // Both participants hear the outcome, so the result reaches them in one fanout.
        var resultCall = bench.Rec.Of("FanoutOlympiadResult").Last();
        Assert.Equal(2, Assert.IsAssignableFrom<IReadOnlyList<int>>(resultCall[0]).Count);

        // The Gate Pass lands in the winner's bag, so the Net inventory row must follow the V2 one.
        var grant = Assert.IsType<InventoryUpdateNetMsg>(bench.Rec.Of("FanoutInventoryUpdate").Last()[1]);
        var added = Assert.Single(grant.Changes);
        Assert.Equal(OlympiadProtocol.RewardItemId, added.ItemId);
        Assert.Equal(OlympiadProtocol.ClassedRewardCount, added.Count);
    }

    [Fact]
    public async Task Admin_report_is_refused_when_the_match_does_not_exist()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("oly", "Oly");

        Assert.Equal(
            OlympiadProtocol.MatchMissing,
            await bench.World.TryAdminReportOlympiadAsync(actor.Session, 4, 0, CancellationToken.None));
    }

    [Fact]
    public void Status_codec_round_trips_every_field()
    {
        var message = new OlympiadStatusNetMsg
        {
            Period = 2,
            Cycle = 3,
            InCompetition = true,
            CompetitionStartUnixMs = 10,
            CompetitionEndUnixMs = 20,
            OlympiadEndUnixMs = 30,
            ValidationEndUnixMs = 40,
            NextWeeklyUnixMs = 50,
            RegistrationOpen = false,
            Noble = true,
            Points = 60,
            CompetitionsDone = 7,
            Won = 8,
            Lost = 9,
            Drawn = 10,
            Queue = OlympiadProtocol.QueueNonClassed,
            MatchId = 11,
            StadiumId = 12,
            ClassId = 13
        };

        var copy = NetCodec.DecodeOlympiadStatus(NetCodec.EncodeOlympiadStatus(message));
        Assert.Equal(2, copy.Period);
        Assert.Equal(3, copy.Cycle);
        Assert.True(copy.InCompetition);
        Assert.Equal(10, copy.CompetitionStartUnixMs);
        Assert.Equal(20, copy.CompetitionEndUnixMs);
        Assert.Equal(30, copy.OlympiadEndUnixMs);
        Assert.Equal(40, copy.ValidationEndUnixMs);
        Assert.Equal(50, copy.NextWeeklyUnixMs);
        Assert.False(copy.RegistrationOpen);
        Assert.True(copy.Noble);
        Assert.Equal(60, copy.Points);
        Assert.Equal(7, copy.CompetitionsDone);
        Assert.Equal(8, copy.Won);
        Assert.Equal(9, copy.Lost);
        Assert.Equal(10, copy.Drawn);
        Assert.Equal(OlympiadProtocol.QueueNonClassed, copy.Queue);
        Assert.Equal(11, copy.MatchId);
        Assert.Equal(12, copy.StadiumId);
        Assert.Equal(13, copy.ClassId);
    }

    [Fact]
    public void Event_codecs_round_trip()
    {
        var match = NetCodec.DecodeOlympiadMatch(NetCodec.EncodeOlympiadMatch(new OlympiadMatchNetMsg
        {
            MatchId = 1,
            StadiumId = 2,
            ZoneId = 3,
            Classed = true,
            Phase = 4,
            PlayerOneId = 5,
            PlayerTwoId = 6,
            PlayerOneName = "One",
            PlayerTwoName = "Two",
            InstanceId = 7
        }));
        Assert.Equal(1, match.MatchId);
        Assert.Equal(2, match.StadiumId);
        Assert.Equal(3, match.ZoneId);
        Assert.True(match.Classed);
        Assert.Equal(4, match.Phase);
        Assert.Equal(5, match.PlayerOneId);
        Assert.Equal(6, match.PlayerTwoId);
        Assert.Equal("One", match.PlayerOneName);
        Assert.Equal("Two", match.PlayerTwoName);
        Assert.Equal(7, match.InstanceId);

        var countdown = NetCodec.DecodeOlympiadCountdown(NetCodec.EncodeOlympiadCountdown(new OlympiadCountdownNetMsg
        {
            MatchId = 1,
            Kind = 2,
            Seconds = 3
        }));
        Assert.Equal(1, countdown.MatchId);
        Assert.Equal(2, countdown.Kind);
        Assert.Equal(3, countdown.Seconds);

        var result = NetCodec.DecodeOlympiadResult(NetCodec.EncodeOlympiadResult(new OlympiadResultNetMsg
        {
            MatchId = 1,
            Outcome = 2,
            WinnerObjectId = 3,
            PointsOne = 4,
            PointsTwo = 5,
            DeltaOne = 6,
            DeltaTwo = 7,
            RewardItemId = 8,
            RewardCount = 9,
            CompetitionsDoneOne = 10,
            CompetitionsDoneTwo = 11
        }));
        Assert.Equal(1, result.MatchId);
        Assert.Equal(2, result.Outcome);
        Assert.Equal(3, result.WinnerObjectId);
        Assert.Equal(4, result.PointsOne);
        Assert.Equal(5, result.PointsTwo);
        Assert.Equal(6, result.DeltaOne);
        Assert.Equal(7, result.DeltaTwo);
        Assert.Equal(8, result.RewardItemId);
        Assert.Equal(9, result.RewardCount);
        Assert.Equal(10, result.CompetitionsDoneOne);
        Assert.Equal(11, result.CompetitionsDoneTwo);
    }

    static OlympiadStatusNetMsg LastStatus(WorldBench bench) =>
        Assert.IsType<OlympiadStatusNetMsg>(bench.Rec.Of("FanoutOlympiadStatus").Last()[1]);

    static SystemMessageMsg LastSystemMessage(WorldBench bench) =>
        Assert.IsType<SystemMessageMsg>(bench.Rec.Of("FanoutSystemMessage").Last()[1]);

    /// <summary>18:30 on a competition day, so the sheet opens rather than waiting for the start hour.</summary>
    static WorldBench Open() => new(clock: () => Evening);
}
