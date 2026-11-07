using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Seven Signs over Net: the record and sky a client reads, the period gate on joining, the seal-stone
/// contribution that moves the cabal score, and the period change that reaches every live player.
/// The world runs on <see cref="WorldBench"/>, so these assert the recorded
/// <see cref="Fineage.Zone.Net.INetPresenceBridge"/> fanout rather than MessagePack frames.
/// </summary>
public sealed class NetSevenSignsTests
{
    static readonly DateTimeOffset Recruiting = new(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Status_join_and_contribution_follow_the_record()
    {
        var clock = new MutableGameClock(Recruiting);
        var engine = new SevenSignsEngine(clock);
        var bench = new WorldBench(clock: () => clock.UtcNow, sevenSigns: engine);
        // Class 1 is the first fighter profession; the base archetype (0) has no class level and is refused.
        BenchPlayer player = await bench.EnterAsync("ssq", "Priest", classId: 1);

        Assert.Null(await bench.World.TrySevenSignsStatusAsync(player.Session, CancellationToken.None));
        Assert.Equal(SevenSignsProtocol.PeriodRecruiting, LastStatus(bench).Period);
        Assert.Equal(SevenSignsProtocol.SkyNormal, LastSky(bench).State);
        Assert.Equal(0, LastStatus(bench).PlayerCabal);

        // Recruiting has not opened the competition yet, so the join is refused with the period code.
        Assert.Equal(
            SevenSignsProtocol.PeriodClosed,
            await bench.World.TrySevenSignsActionAsync(player.Session, new SevenSignsActionRequestMsg
            {
                Action = SevenSignsProtocol.ActionJoin,
                Cabal = SevenSignsProtocol.CabalDusk,
                Seal = SevenSignsProtocol.SealAvarice
            }, CancellationToken.None));

        // A period change is one system line plus the new sky, to every live player.
        clock.UtcNow = engine.NextChange;
        Assert.True(await bench.World.AdvanceSevenSignsAsync(CancellationToken.None));
        Assert.Equal(SevenSignsProtocol.PeriodCompetitionMessage, LastSystemMessage(bench).Id);
        Assert.Equal(SevenSignsProtocol.SkyNormal, LastSky(bench).State);

        Assert.Null(await bench.World.TryGrantItemAsync(player.Session, SevenSignsProtocol.RedSealStoneId, 2, CancellationToken.None));

        Assert.Null(await bench.World.TrySevenSignsActionAsync(player.Session, new SevenSignsActionRequestMsg
        {
            Action = SevenSignsProtocol.ActionJoin,
            Cabal = SevenSignsProtocol.CabalDusk,
            Seal = SevenSignsProtocol.SealStrife
        }, CancellationToken.None));
        Assert.Null(await bench.World.TrySevenSignsActionAsync(player.Session, new SevenSignsActionRequestMsg
        {
            Action = SevenSignsProtocol.ActionContribute,
            RedStones = 2
        }, CancellationToken.None));

        var after = engine.StatusFor(player.ObjectId);
        Assert.Equal(SevenSignsProtocol.CabalDusk, after.PlayerCabal);
        Assert.Equal(SevenSignsProtocol.SealStrife, after.PlayerSeal);
        Assert.Equal(20, after.PlayerContribution);
        Assert.Equal(20, after.PlayerAncientAdena);
        Assert.Equal(500, after.DuskScore);

        // The record the client sees must match the engine, and the stones leaving the bag must cost a row.
        var record = LastStatus(bench);
        Assert.Equal(SevenSignsProtocol.CabalDusk, record.PlayerCabal);
        Assert.Equal(SevenSignsProtocol.SealStrife, record.PlayerSeal);
        Assert.Equal(20, record.PlayerContribution);
        Assert.Equal(500, record.DuskScore);
        var spent = Assert.IsType<InventoryUpdateNetMsg>(bench.Rec.Of("FanoutInventoryUpdate").Last()[1]);
        var row = Assert.Single(spent.Changes);
        Assert.Equal(SevenSignsProtocol.RedSealStoneId, row.ItemId);
        Assert.Equal(2, row.Change);
    }

    [Fact]
    public async Task An_unknown_action_is_a_bad_request()
    {
        var clock = new MutableGameClock(Recruiting);
        var engine = new SevenSignsEngine(clock);
        var bench = new WorldBench(clock: () => clock.UtcNow, sevenSigns: engine);
        // Class 1 is the first fighter profession; the base archetype (0) has no class level and is refused.
        BenchPlayer player = await bench.EnterAsync("ssq", "Priest", classId: 1);

        Assert.Equal(
            "bad_request",
            await bench.World.TrySevenSignsActionAsync(player.Session, new SevenSignsActionRequestMsg
            {
                Action = 9
            }, CancellationToken.None));
        Assert.Empty(bench.Rec.Of("FanoutSevenSignsStatus"));
    }

    [Fact]
    public void Status_and_sky_codecs_round_trip_every_field()
    {
        var status = new SevenSignsStatusNetMsg
        {
            Period = 1,
            Cycle = 2,
            PlayerCabal = 1,
            PlayerSeal = 3,
            PlayerStoneCount = 4,
            PlayerAncientAdena = 5,
            PlayerContribution = 6,
            DawnScore = 7,
            DuskScore = 8,
            DawnFestivalScore = 9,
            DuskFestivalScore = 10,
            DawnStonePoints = 11,
            DuskStonePoints = 12,
            AvariceOwner = 1,
            GnosisOwner = 2,
            StrifeOwner = 3,
            DawnMembers = 13,
            DuskMembers = 14,
            WinningCabal = 2,
            NextChangeUnixMs = 15,
            SkyState = SevenSignsProtocol.SkyDawn,
            DawnAvarice = 1,
            DawnGnosis = 1,
            DawnStrife = 0,
            DuskAvarice = 0,
            DuskGnosis = 1,
            DuskStrife = 1,
            PreviousWinner = 2
        };

        var copy = NetCodec.DecodeSevenSignsStatus(NetCodec.EncodeSevenSignsStatus(status));
        Assert.Equal(1, copy.Period);
        Assert.Equal(2, copy.Cycle);
        Assert.Equal(1, copy.PlayerCabal);
        Assert.Equal(3, copy.PlayerSeal);
        Assert.Equal(4, copy.PlayerStoneCount);
        Assert.Equal(5, copy.PlayerAncientAdena);
        Assert.Equal(6, copy.PlayerContribution);
        Assert.Equal(7, copy.DawnScore);
        Assert.Equal(8, copy.DuskScore);
        Assert.Equal(9, copy.DawnFestivalScore);
        Assert.Equal(10, copy.DuskFestivalScore);
        Assert.Equal(11, copy.DawnStonePoints);
        Assert.Equal(12, copy.DuskStonePoints);
        Assert.Equal(1, copy.AvariceOwner);
        Assert.Equal(2, copy.GnosisOwner);
        Assert.Equal(3, copy.StrifeOwner);
        Assert.Equal(13, copy.DawnMembers);
        Assert.Equal(14, copy.DuskMembers);
        Assert.Equal(2, copy.WinningCabal);
        Assert.Equal(15, copy.NextChangeUnixMs);
        Assert.Equal(SevenSignsProtocol.SkyDawn, copy.SkyState);
        Assert.Equal(1, copy.DawnAvarice);
        Assert.Equal(1, copy.DawnGnosis);
        Assert.Equal(0, copy.DawnStrife);
        Assert.Equal(0, copy.DuskAvarice);
        Assert.Equal(1, copy.DuskGnosis);
        Assert.Equal(1, copy.DuskStrife);
        Assert.Equal(2, copy.PreviousWinner);

        var sky = NetCodec.DecodeSevenSignsSky(NetCodec.EncodeSevenSignsSky(new SevenSignsSkyNetMsg
        {
            State = SevenSignsProtocol.SkyDusk,
            Period = SevenSignsProtocol.PeriodResults,
            WinningCabal = SevenSignsProtocol.CabalDusk
        }));
        Assert.Equal(SevenSignsProtocol.SkyDusk, sky.State);
        Assert.Equal(SevenSignsProtocol.PeriodResults, sky.Period);
        Assert.Equal(SevenSignsProtocol.CabalDusk, sky.WinningCabal);

        var action = NetCodec.DecodeSevenSignsActionRequest(NetCodec.EncodeSevenSignsActionRequest(
            new SevenSignsActionRequestNetMsg
            {
                Action = SevenSignsProtocol.ActionContribute,
                Cabal = SevenSignsProtocol.CabalDawn,
                Seal = SevenSignsProtocol.SealGnosis,
                BlueStones = 1,
                GreenStones = 2,
                RedStones = 3
            }));
        Assert.Equal(SevenSignsProtocol.ActionContribute, action.Action);
        Assert.Equal(SevenSignsProtocol.CabalDawn, action.Cabal);
        Assert.Equal(SevenSignsProtocol.SealGnosis, action.Seal);
        Assert.Equal(1, action.BlueStones);
        Assert.Equal(2, action.GreenStones);
        Assert.Equal(3, action.RedStones);
    }

    static SevenSignsStatusNetMsg LastStatus(WorldBench bench) =>
        Assert.IsType<SevenSignsStatusNetMsg>(bench.Rec.Of("FanoutSevenSignsStatus").Last()[1]);

    static SevenSignsSkyNetMsg LastSky(WorldBench bench) =>
        Assert.IsType<SevenSignsSkyNetMsg>(bench.Rec.Of("FanoutSevenSignsSky").Last()[1]);

    static SystemMessageNetMsg LastSystemMessage(WorldBench bench) =>
        Assert.IsType<SystemMessageNetMsg>(bench.Rec.Of("FanoutSystemMessage").Last()[1]);
}
