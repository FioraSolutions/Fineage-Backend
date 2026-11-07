using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Dungeons;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Catacombs and necropolises over Net: the cabal gate that refuses an unregistered visitor, a member entering
/// and leaving, the validation kick that clears the losers, the recruiting kick, and the adena fee. The world
/// runs on <see cref="WorldBench"/>, so these assert the recorded
/// <see cref="Fineage.Zone.Net.INetPresenceBridge"/> fanout rather than MessagePack frames.
/// </summary>
public sealed class NetDungeonTests
{
    static readonly DateTimeOffset Start = new(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unregistered_visitor_is_refused_and_a_member_enters_then_exits()
    {
        var clock = new MutableGameClock(Start);
        DungeonCatalog catalog = DungeonCatalog.LoadDefault();
        Assert.True(catalog.TryGet(1, out var sacrifice));
        var bench = World(clock, catalog);
        BenchPlayer outsider = await bench.EnterAsync("gate", "Out", classId: 1);
        BenchPlayer member = await bench.EnterAsync("gate2", "In", classId: 1);
        await AdvanceAsync(bench, clock);

        int originX = outsider.Session.X;
        Assert.Equal(
            DungeonProtocol.Cabal,
            await outsider.World.TryDungeonAsync(outsider.Session, Enter(1), CancellationToken.None));
        Assert.Equal(originX, outsider.Session.X);
        var refused = LastSystemMessage(bench);
        Assert.Equal(DungeonProtocol.GateRefusedMessage, refused.Id);
        Assert.Equal(DungeonProtocol.GateRefusedText, refused.Text);

        Assert.Null(await member.World.TrySevenSignsActionAsync(
            member.Session, Join(SevenSignsCabal.Dusk, SevenSignsSeal.Avarice), CancellationToken.None));
        Assert.Null(await member.World.TryDungeonAsync(member.Session, Enter(1), CancellationToken.None));
        Assert.Equal(sacrifice.EntranceX, member.Session.X);
        Assert.Equal(sacrifice.EntranceY, member.Session.Y);
        Assert.Equal(sacrifice.EntranceZ, member.Session.Z);
        Assert.Equal(1, bench.World.PlayerDungeonId(member.ObjectId));

        var entered = LastDungeon(bench);
        Assert.True(entered.Inside);
        Assert.Equal(DungeonProtocol.ActionEnter, entered.Action);
        Assert.Equal(sacrifice.Id, entered.DungeonId);
        Assert.Equal(sacrifice.EntranceX, entered.X);

        Assert.Null(await bench.World.TryDungeonAsync(member.Session, new DungeonEnterRequestMsg
        {
            DungeonId = 1,
            Action = DungeonProtocol.ActionExit
        }, CancellationToken.None));
        Assert.Equal(sacrifice.ExitX, member.Session.X);
        Assert.Equal(0, bench.World.PlayerDungeonId(member.ObjectId));
        var left = LastDungeon(bench);
        Assert.False(left.Inside);
        Assert.Equal(DungeonProtocol.ActionExit, left.Action);
        Assert.Equal(sacrifice.ExitX, left.X);
    }

    [Fact]
    public async Task Validation_kicks_the_losing_cabal_and_leaves_the_seal_owner()
    {
        var clock = new MutableGameClock(Start);
        DungeonCatalog catalog = DungeonCatalog.LoadDefault();
        Assert.True(catalog.TryGet(1, out var sacrifice));
        var bench = World(clock, catalog);
        BenchPlayer dusk = await bench.EnterAsync("gate", "Dusk", classId: 1);
        BenchPlayer dawn = await bench.EnterAsync("gate2", "Dawn", classId: 1);
        await AdvanceAsync(bench, clock);

        Assert.Null(await bench.World.TryGrantItemAsync(dusk.Session, SevenSignsProtocol.RedSealStoneId, 1, CancellationToken.None));
        Assert.Null(await bench.World.TrySevenSignsActionAsync(
            dusk.Session, Join(SevenSignsCabal.Dusk, SevenSignsSeal.Avarice), CancellationToken.None));
        Assert.Null(await bench.World.TrySevenSignsActionAsync(dusk.Session, new SevenSignsActionRequestMsg
        {
            Action = SevenSignsProtocol.ActionContribute,
            RedStones = 1
        }, CancellationToken.None));
        Assert.Null(await bench.World.TrySevenSignsActionAsync(
            dawn.Session, Join(SevenSignsCabal.Dawn, SevenSignsSeal.Strife), CancellationToken.None));
        Assert.Null(await bench.World.TryDungeonAsync(dusk.Session, Enter(1), CancellationToken.None));
        Assert.Null(await bench.World.TryDungeonAsync(dawn.Session, Enter(1), CancellationToken.None));

        await AdvanceAsync(bench, clock);
        Assert.Equal(SevenSignsPeriod.Results, bench.World.SevenSigns.Period);
        Assert.Equal(1, bench.World.PlayerDungeonId(dawn.ObjectId));
        Assert.Equal(1, bench.World.PlayerDungeonId(dusk.ObjectId));

        await AdvanceAsync(bench, clock);
        Assert.Equal(SevenSignsPeriod.SealValidation, bench.World.SevenSigns.Period);
        Assert.Equal(0, bench.World.PlayerDungeonId(dawn.ObjectId));
        Assert.Equal(1, bench.World.PlayerDungeonId(dusk.ObjectId));
        Assert.Equal(sacrifice.ExitX, dawn.Session.X);
        Assert.Equal(sacrifice.EntranceX, dusk.Session.X);

        var kicked = LastDungeon(bench);
        Assert.Equal(DungeonProtocol.ActionKick, kicked.Action);
        Assert.False(kicked.Inside);
        Assert.Equal(DungeonProtocol.KickedValidationMessage, LastSystemMessage(bench).Id);

        // The winner keeps the instance, so the last dungeon frame stays the one that took it inside.
        Assert.Contains(bench.Rec.Of("FanoutDungeonEntered"), call =>
            Assert.IsAssignableFrom<IReadOnlyList<int>>(call[0]).Contains(dawn.ObjectId) == false);
    }

    [Fact]
    public async Task Recruiting_after_validation_kicks_everyone_who_is_still_inside()
    {
        var clock = new MutableGameClock(Start);
        DungeonCatalog catalog = DungeonCatalog.LoadDefault();
        Assert.True(catalog.TryGet(9, out var heretics));
        var bench = World(clock, catalog);
        BenchPlayer member = await bench.EnterAsync("gate", "Stay", classId: 1);
        await AdvanceAsync(bench, clock);
        Assert.Null(await bench.World.TrySevenSignsActionAsync(
            member.Session, Join(SevenSignsCabal.Dawn, SevenSignsSeal.Gnosis), CancellationToken.None));
        Assert.Null(await bench.World.TryDungeonAsync(member.Session, Enter(9), CancellationToken.None));

        await AdvanceAsync(bench, clock);
        await AdvanceAsync(bench, clock);
        Assert.Equal(SevenSignsPeriod.SealValidation, bench.World.SevenSigns.Period);
        Assert.Equal(9, bench.World.PlayerDungeonId(member.ObjectId));

        bench.Rec.Calls.Clear();
        await AdvanceAsync(bench, clock);
        Assert.Equal(SevenSignsPeriod.Recruiting, bench.World.SevenSigns.Period);
        Assert.Equal(0, bench.World.PlayerDungeonId(member.ObjectId));
        Assert.Equal(heretics.ExitX, member.Session.X);
        Assert.Equal(DungeonProtocol.KickedUnsignedMessage, LastSystemMessage(bench).Id);
        Assert.Equal(DungeonProtocol.ActionKick, LastDungeon(bench).Action);
    }

    [Fact]
    public async Task A_configured_adena_fee_is_taken_once()
    {
        var clock = new MutableGameClock(Start);
        var catalog = new DungeonCatalog(
        [
            new DungeonSite(1, DungeonKind.Necropolis, "Fee", 1, 2, 10, 20, 30, 11, 21, 31, DungeonProtocol.AdenaItemId, 100)
        ]);
        var bench = World(clock, catalog);
        BenchPlayer member = await bench.EnterAsync("gate", "Payer", classId: 1);
        await AdvanceAsync(bench, clock);
        Assert.Null(await bench.World.TrySevenSignsActionAsync(
            member.Session, Join(SevenSignsCabal.Dusk, SevenSignsSeal.Avarice), CancellationToken.None));

        Assert.Equal(
            DungeonProtocol.Fee,
            await bench.World.TryDungeonAsync(member.Session, Enter(1), CancellationToken.None));
        Assert.NotEqual(10, member.Session.X);

        Assert.Null(await bench.World.TryGrantItemAsync(member.Session, DungeonProtocol.AdenaItemId, 100, CancellationToken.None));
        bench.Rec.Calls.Clear();
        Assert.Null(await bench.World.TryDungeonAsync(member.Session, Enter(1), CancellationToken.None));
        Assert.Equal(10, member.Session.X);
        Assert.Equal(20, member.Session.Y);

        // The fee leaves the bag, so the client must see the row it lost. A fully spent stack reports 0 left.
        var paid = Assert.IsType<InventoryUpdateNetMsg>(Assert.Single(bench.Rec.Of("FanoutInventoryUpdate"))[1]);
        var row = Assert.Single(paid.Changes);
        Assert.Equal(DungeonProtocol.AdenaItemId, row.ItemId);
        Assert.Equal(0, row.Count);
        Assert.Equal(2, row.Change);

        Assert.Equal(
            DungeonProtocol.Fee,
            await bench.World.TryDungeonAsync(member.Session, Enter(1), CancellationToken.None));
    }

    [Fact]
    public async Task Dead_members_and_unknown_dungeons_do_not_move()
    {
        var clock = new MutableGameClock(Start);
        var bench = World(clock, DungeonCatalog.LoadDefault());
        BenchPlayer member = await bench.EnterAsync("gate", "Dead", classId: 1);
        await AdvanceAsync(bench, clock);
        Assert.Null(await bench.World.TrySevenSignsActionAsync(
            member.Session, Join(SevenSignsCabal.Dawn, SevenSignsSeal.Avarice), CancellationToken.None));
        Assert.True(bench.World.TrySetPlayerDead(member.ObjectId, true));
        int x = member.Session.X;
        Assert.Equal(
            InventoryProtocol.Dead,
            await bench.World.TryDungeonAsync(member.Session, Enter(1), CancellationToken.None));
        Assert.Equal(x, member.Session.X);
        Assert.True(bench.World.TrySetPlayerDead(member.ObjectId, false));
        Assert.Equal(
            DungeonProtocol.Unknown,
            await bench.World.TryDungeonAsync(member.Session, Enter(99), CancellationToken.None));
        Assert.Equal("bad_request", await bench.World.TryDungeonAsync(member.Session, new DungeonEnterRequestMsg
        {
            DungeonId = 1,
            Action = 0
        }, CancellationToken.None));
        Assert.Empty(bench.Rec.Of("FanoutDungeonEntered"));
    }

    [Fact]
    public void Request_and_result_codecs_round_trip()
    {
        var request = NetCodec.DecodeDungeonEnterRequest(NetCodec.EncodeDungeonEnterRequest(
            new DungeonEnterRequestNetMsg { DungeonId = 9, Action = DungeonProtocol.ActionEnter }));
        Assert.Equal(9, request.DungeonId);
        Assert.Equal(DungeonProtocol.ActionEnter, request.Action);

        var entered = NetCodec.DecodeDungeonEntered(NetCodec.EncodeDungeonEntered(new DungeonEnteredNetMsg
        {
            DungeonId = 9,
            X = -53174,
            Y = -250275,
            Z = -7911,
            Inside = true,
            Action = DungeonProtocol.ActionEnter
        }));
        Assert.Equal(9, entered.DungeonId);
        Assert.Equal(-53174, entered.X);
        Assert.Equal(-250275, entered.Y);
        Assert.Equal(-7911, entered.Z);
        Assert.True(entered.Inside);
        Assert.Equal(DungeonProtocol.ActionEnter, entered.Action);
    }

    static WorldBench World(MutableGameClock clock, DungeonCatalog catalog) => new(
        clock: () => clock.UtcNow,
        sevenSigns: new SevenSignsEngine(clock),
        dungeons: catalog,
        dungeonAccess: new DungeonAccessOptions());

    static async Task AdvanceAsync(WorldBench bench, MutableGameClock clock)
    {
        clock.UtcNow = bench.World.SevenSigns.NextChange;
        Assert.True(await bench.World.AdvanceSevenSignsAsync(CancellationToken.None));
    }

    static DungeonEnterRequestMsg Enter(int dungeonId) => new()
    {
        DungeonId = dungeonId,
        Action = DungeonProtocol.ActionEnter
    };

    static SevenSignsActionRequestMsg Join(SevenSignsCabal cabal, SevenSignsSeal seal) => new()
    {
        Action = SevenSignsProtocol.ActionJoin,
        Cabal = (int)cabal,
        Seal = (int)seal
    };

    static DungeonEnteredNetMsg LastDungeon(WorldBench bench) =>
        Assert.IsType<DungeonEnteredNetMsg>(bench.Rec.Of("FanoutDungeonEntered").Last()[1]);

    static SystemMessageNetMsg LastSystemMessage(WorldBench bench) =>
        Assert.IsType<SystemMessageNetMsg>(bench.Rec.Of("FanoutSystemMessage").Last()[1]);
}
