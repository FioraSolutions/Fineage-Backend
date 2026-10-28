using Fineage.Protocol;
using Fineage.Zone.Dungeons;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class DungeonAccessRuleTests
{
    const int Sacrifice = 1;
    const int Heretics = 9;

    [Fact]
    public void Catalog_has_eight_necropolises_and_six_catacombs_at_the_ziggurat_points()
    {
        var catalog = DungeonCatalog.LoadDefault();
        Assert.Equal(14, catalog.All.Count);
        Assert.All(catalog.All, site => Assert.Equal(0, site.FeeCount));

        Assert.True(catalog.TryGet(Sacrifice, out var sacrifice));
        Assert.Equal(DungeonKind.Necropolis, sacrifice.Kind);
        Assert.Equal("Necropolis of Sacrifice", sacrifice.Name);
        Assert.Equal(31095, sacrifice.EntranceNpcId);
        Assert.Equal((-41570, 209785, -5089), (sacrifice.EntranceX, sacrifice.EntranceY, sacrifice.EntranceZ));
        Assert.Equal(31103, sacrifice.ExitNpcId);
        Assert.Equal((-41567, 209292, -5091), (sacrifice.ExitX, sacrifice.ExitY, sacrifice.ExitZ));

        Assert.True(catalog.TryGet(Heretics, out var heretics));
        Assert.Equal(DungeonKind.Catacomb, heretics.Kind);
        Assert.Equal("Heretics Catacomb", heretics.Name);
        Assert.Equal(31114, heretics.EntranceNpcId);
        Assert.Equal((-53174, -250275, -7911), (heretics.EntranceX, heretics.EntranceY, heretics.EntranceZ));
        Assert.Equal(31120, heretics.ExitNpcId);
        Assert.Equal((42514, 143917, -5385), (heretics.ExitX, heretics.ExitY, heretics.ExitZ));

        Assert.Equal(8, catalog.All.Count(site => site.Kind == DungeonKind.Necropolis));
        Assert.Equal(6, catalog.All.Count(site => site.Kind == DungeonKind.Catacomb));
    }

    [Theory]
    [InlineData(SevenSignsPeriod.Recruiting)]
    [InlineData(SevenSignsPeriod.Competition)]
    [InlineData(SevenSignsPeriod.Results)]
    public void Outside_validation_either_cabal_enters_and_the_unregistered_are_refused(SevenSignsPeriod period)
    {
        var access = Gate(period, SevenSignsCabal.Dawn, SevenSignsCabal.Dawn, SevenSignsCabal.Dawn);
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.Dawn), Sacrifice));
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.Dusk), Heretics));
        Assert.Equal(DungeonAccessCode.NotRegistered, access.CanEnter(Who(SevenSignsCabal.None), Sacrifice));
        Assert.Equal(DungeonAccessCode.NotRegistered, access.CanEnter(Who(SevenSignsCabal.None), Heretics));
        var lines = access.RefusalLines(DungeonAccessCode.NotRegistered, Sacrifice);
        Assert.Equal(DungeonProtocol.GateRefusedMessage, lines[0].Id);
        Assert.Equal(DungeonProtocol.GateRefusedText, lines[0].Text);
    }

    [Fact]
    public void Seal_validation_gives_necropolises_to_the_avarice_winner_and_catacombs_to_the_gnosis_winner()
    {
        var dawn = Gate(SevenSignsPeriod.SealValidation, SevenSignsCabal.Dawn, SevenSignsCabal.Dawn, SevenSignsCabal.None);
        Assert.Equal(DungeonAccessCode.Allowed, dawn.CanEnter(Who(SevenSignsCabal.Dawn), Sacrifice));
        Assert.Equal(DungeonAccessCode.DawnOnly, dawn.CanEnter(Who(SevenSignsCabal.Dusk), Sacrifice));
        Assert.Equal(DungeonAccessCode.DawnOnly, dawn.CanEnter(Who(SevenSignsCabal.None), Sacrifice));
        Assert.Equal(DungeonAccessCode.DawnOnly, dawn.CanEnter(Who(SevenSignsCabal.Dawn), Heretics));

        var dusk = Gate(SevenSignsPeriod.SealValidation, SevenSignsCabal.Dusk, SevenSignsCabal.None, SevenSignsCabal.Dusk);
        Assert.Equal(DungeonAccessCode.Allowed, dusk.CanEnter(Who(SevenSignsCabal.Dusk), Heretics));
        Assert.Equal(DungeonAccessCode.DuskOnly, dusk.CanEnter(Who(SevenSignsCabal.Dawn), Heretics));
        Assert.Equal(DungeonAccessCode.DuskOnly, dusk.CanEnter(Who(SevenSignsCabal.Dusk), Sacrifice));

        var dawnLines = dawn.RefusalLines(DungeonAccessCode.DawnOnly, Sacrifice);
        Assert.Equal(DungeonProtocol.DawnOnlyMessage, dawnLines[0].Id);
        Assert.Equal(DungeonProtocol.DawnOnlyText, dawnLines[0].Text);
        Assert.Equal(DungeonProtocol.GateRefusedMessage, dawnLines[1].Id);
        var duskLines = dusk.RefusalLines(DungeonAccessCode.DuskOnly, Heretics);
        Assert.Equal(DungeonProtocol.DuskOnlyMessage, duskLines[0].Id);
        Assert.Equal(DungeonProtocol.DuskOnlyText, duskLines[0].Text);
    }

    [Fact]
    public void A_tie_during_validation_admits_either_cabal_and_refuses_the_unregistered()
    {
        var access = Gate(SevenSignsPeriod.SealValidation, SevenSignsCabal.None, SevenSignsCabal.Dawn, SevenSignsCabal.Dusk);
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.Dawn), Sacrifice));
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.Dusk), Heretics));
        Assert.Equal(DungeonAccessCode.NotRegistered, access.CanEnter(Who(SevenSignsCabal.None), Sacrifice));
        Assert.Equal(DungeonProtocol.Cabal, DungeonAccess.ToWire(DungeonAccessCode.NotRegistered));
        Assert.Equal(DungeonProtocol.Dawn, DungeonAccess.ToWire(DungeonAccessCode.DawnOnly));
        Assert.Equal(DungeonProtocol.Dusk, DungeonAccess.ToWire(DungeonAccessCode.DuskOnly));
    }

    [Fact]
    public void Open_flags_skip_the_cabal_check_for_that_kind_only()
    {
        var access = Gate(
            SevenSignsPeriod.SealValidation,
            SevenSignsCabal.Dawn,
            SevenSignsCabal.Dawn,
            SevenSignsCabal.Dawn,
            new DungeonAccessOptions { OpenNecropolis = true });
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.None), Sacrifice));
        Assert.Equal(DungeonAccessCode.DawnOnly, access.CanEnter(Who(SevenSignsCabal.Dusk), Heretics));
    }

    [Fact]
    public void Dead_and_fee_are_checked_only_after_the_cabal_gate()
    {
        var priced = new DungeonCatalog(
        [
            new DungeonSite(3, DungeonKind.Necropolis, "Priced", 1, 2, 1, 2, 3, 4, 5, 6, DungeonProtocol.AdenaItemId, 100),
            new DungeonSite(4, DungeonKind.Catacomb, "Ancient", 1, 2, 1, 2, 3, 4, 5, 6, DungeonProtocol.AncientAdenaItemId, 10)
        ]);
        var access = new DungeonAccess(priced);
        access.Use(new DungeonAccessState(SevenSignsPeriod.Competition, SevenSignsCabal.None, SevenSignsCabal.None, SevenSignsCabal.None));

        Assert.Equal(DungeonAccessCode.NotRegistered, access.CanEnter(Who(SevenSignsCabal.None, dead: true), 3));
        Assert.Equal(DungeonAccessCode.Dead, access.CanEnter(Who(SevenSignsCabal.Dawn, dead: true, adena: 100), 3));
        Assert.Equal(DungeonAccessCode.FeeRequired, access.CanEnter(Who(SevenSignsCabal.Dawn, adena: 99), 3));
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.Dawn, adena: 100), 3));
        Assert.Equal(DungeonProtocol.NotEnoughAdenaMessage, access.RefusalLines(DungeonAccessCode.FeeRequired, 3)[0].Id);

        Assert.Equal(DungeonAccessCode.FeeRequired, access.CanEnter(new DungeonCharacter(SevenSignsCabal.Dusk, false, _ => 0), 4));
        Assert.Equal(DungeonProtocol.NotEnoughAncientAdenaText, access.RefusalLines(DungeonAccessCode.FeeRequired, 4)[0].Text);
        Assert.Equal(DungeonAccessCode.UnknownDungeon, access.CanEnter(Who(SevenSignsCabal.Dawn), 99));
    }

    [Fact]
    public void Staying_inside_ignores_a_fee_the_visitor_already_paid()
    {
        var priced = new DungeonCatalog(
        [
            new DungeonSite(1, DungeonKind.Necropolis, "Priced", 1, 2, 1, 2, 3, 4, 5, 6, DungeonProtocol.AdenaItemId, 100)
        ]);
        var access = new DungeonAccess(priced);
        access.Use(new DungeonAccessState(SevenSignsPeriod.Competition, SevenSignsCabal.None, SevenSignsCabal.None, SevenSignsCabal.None));
        Assert.True(access.MayRemain(SevenSignsCabal.Dawn, 1));
        Assert.False(access.MayRemain(SevenSignsCabal.None, 1));

        access.Use(new DungeonAccessState(SevenSignsPeriod.SealValidation, SevenSignsCabal.Dusk, SevenSignsCabal.Dusk, SevenSignsCabal.Dusk));
        Assert.False(access.MayRemain(SevenSignsCabal.Dawn, 1));
        Assert.True(access.MayRemain(SevenSignsCabal.Dusk, 1));
        Assert.Equal(DungeonProtocol.KickedValidationMessage, DungeonAccess.KickNotice(SevenSignsPeriod.SealValidation).Id);
        Assert.Equal(DungeonProtocol.KickedUnsignedMessage, DungeonAccess.KickNotice(SevenSignsPeriod.Recruiting).Id);
    }

    static DungeonAccess Gate(
        SevenSignsPeriod period,
        SevenSignsCabal winner,
        SevenSignsCabal avarice,
        SevenSignsCabal gnosis,
        DungeonAccessOptions? options = null)
    {
        var access = new DungeonAccess(DungeonCatalog.LoadDefault(), options);
        access.Use(new DungeonAccessState(period, winner, avarice, gnosis));
        return access;
    }

    static DungeonCharacter Who(SevenSignsCabal cabal, bool dead = false, int adena = 0) =>
        new(cabal, dead, itemId => itemId == DungeonProtocol.AdenaItemId ? adena : 0);
}

public sealed class DungeonWireTests
{
    static readonly DateTimeOffset Start = new(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unregistered_competition_visitor_is_refused_and_a_member_enters_then_exits()
    {
        var clock = new MutableGameClock(Start);
        var engine = new SevenSignsEngine(clock);
        var world = new ZoneWorld(sevenSigns: engine, dungeonAccess: new DungeonAccessOptions());
        await using var outsider = await EnterAsync(world, 7101, "Out");
        await using var member = await EnterAsync(world, 7102, "In");
        await Advance(engine, world, clock);

        outsider.Checkpoint();
        var originX = outsider.Session.X;
        Assert.Equal(DungeonProtocol.Cabal, await world.TryDungeonAsync(outsider.Session, Enter(1), CancellationToken.None));
        Assert.Equal(originX, outsider.Session.X);
        var refused = await outsider.ReadNewAsync();
        var line = MessagePackSerializer.Deserialize<SystemMessageMsg>(
            refused.Single(frame => frame.Type == (ushort)MsgType.SystemMessage).Body);
        Assert.Equal(DungeonProtocol.GateRefusedMessage, line.Id);
        Assert.Equal(DungeonProtocol.GateRefusedText, line.Text);

        Assert.Null(await world.TrySevenSignsActionAsync(member.Session, Join(SevenSignsCabal.Dusk, SevenSignsSeal.Avarice), CancellationToken.None));
        member.Checkpoint();
        Assert.Null(await world.TryDungeonAsync(member.Session, Enter(1), CancellationToken.None));
        var sacrifice = DungeonCatalog.LoadDefault();
        Assert.True(sacrifice.TryGet(1, out var site));
        Assert.Equal(site.EntranceX, member.Session.X);
        Assert.Equal(site.EntranceY, member.Session.Y);
        Assert.Equal(site.EntranceZ, member.Session.Z);
        Assert.Equal(1, world.PlayerDungeonId(member.Session.ObjectId));
        var entered = await member.ReadNewAsync();
        Assert.Contains(entered, frame => frame.Type == (ushort)MsgType.MoveToAck);
        var ack = MessagePackSerializer.Deserialize<MoveToMsg>(entered.Single(frame => frame.Type == (ushort)MsgType.MoveToAck).Body);
        Assert.Equal(site.EntranceX, ack.DestX);
        var result = MessagePackSerializer.Deserialize<DungeonEnteredMsg>(entered.Single(frame => frame.Type == (ushort)MsgType.DungeonEntered).Body);
        Assert.True(result.Inside);
        Assert.Equal(DungeonProtocol.ActionEnter, result.Action);

        member.Checkpoint();
        Assert.Null(await world.TryDungeonAsync(member.Session, new DungeonEnterRequestMsg
        {
            DungeonId = 1,
            Action = DungeonProtocol.ActionExit
        }, CancellationToken.None));
        Assert.Equal(site.ExitX, member.Session.X);
        Assert.Equal(0, world.PlayerDungeonId(member.Session.ObjectId));
        var left = MessagePackSerializer.Deserialize<DungeonEnteredMsg>(
            (await member.ReadNewAsync()).Single(frame => frame.Type == (ushort)MsgType.DungeonEntered).Body);
        Assert.False(left.Inside);
        Assert.Equal(DungeonProtocol.ActionExit, left.Action);
    }

    [Fact]
    public async Task Validation_kicks_the_losing_cabal_and_leaves_the_seal_owner()
    {
        var clock = new MutableGameClock(Start);
        var engine = new SevenSignsEngine(clock);
        var world = new ZoneWorld(sevenSigns: engine, dungeonAccess: new DungeonAccessOptions());
        await using var dusk = await EnterAsync(world, 7201, "Dusk");
        await using var dawn = await EnterAsync(world, 7202, "Dawn");
        await Advance(engine, world, clock);

        Assert.Null(await world.TryGrantItemAsync(dusk.Session, new GrantItemRequestMsg
        {
            ItemId = SevenSignsProtocol.RedSealStoneId,
            Count = 1
        }, CancellationToken.None));
        Assert.Null(await world.TrySevenSignsActionAsync(dusk.Session, Join(SevenSignsCabal.Dusk, SevenSignsSeal.Avarice), CancellationToken.None));
        Assert.Null(await world.TrySevenSignsActionAsync(dusk.Session, new SevenSignsActionRequestMsg
        {
            Action = SevenSignsProtocol.ActionContribute,
            RedStones = 1
        }, CancellationToken.None));
        Assert.Null(await world.TrySevenSignsActionAsync(dawn.Session, Join(SevenSignsCabal.Dawn, SevenSignsSeal.Strife), CancellationToken.None));
        Assert.Null(await world.TryDungeonAsync(dusk.Session, Enter(1), CancellationToken.None));
        Assert.Null(await world.TryDungeonAsync(dawn.Session, Enter(1), CancellationToken.None));

        await Advance(engine, world, clock);
        Assert.Equal(SevenSignsPeriod.Results, engine.Period);
        Assert.Equal(1, world.PlayerDungeonId(dawn.Session.ObjectId));
        Assert.Equal(1, world.PlayerDungeonId(dusk.Session.ObjectId));

        dawn.Checkpoint();
        dusk.Checkpoint();
        await Advance(engine, world, clock);
        Assert.Equal(SevenSignsPeriod.SealValidation, engine.Period);
        Assert.Equal(SevenSignsCabal.Dusk, engine.WinningCabal);
        Assert.Equal(0, world.PlayerDungeonId(dawn.Session.ObjectId));
        Assert.Equal(1, world.PlayerDungeonId(dusk.Session.ObjectId));

        Assert.True(DungeonCatalog.LoadDefault().TryGet(1, out var site));
        Assert.Equal(site.ExitX, dawn.Session.X);
        Assert.Equal(site.EntranceX, dusk.Session.X);
        var kicked = await dawn.ReadNewAsync();
        Assert.Contains(kicked, frame => frame.Type == (ushort)MsgType.SystemMessage &&
            MessagePackSerializer.Deserialize<SystemMessageMsg>(frame.Body).Id == DungeonProtocol.KickedValidationMessage);
        var gone = MessagePackSerializer.Deserialize<DungeonEnteredMsg>(
            kicked.Single(frame => frame.Type == (ushort)MsgType.DungeonEntered).Body);
        Assert.Equal(DungeonProtocol.ActionKick, gone.Action);
        Assert.False(gone.Inside);
        var stayed = await dusk.ReadNewAsync();
        Assert.DoesNotContain(stayed, frame => frame.Type == (ushort)MsgType.DungeonEntered);
    }

    [Fact]
    public async Task Recruiting_after_validation_kicks_everyone_who_is_still_inside()
    {
        var clock = new MutableGameClock(Start);
        var engine = new SevenSignsEngine(clock);
        var world = new ZoneWorld(sevenSigns: engine, dungeonAccess: new DungeonAccessOptions());
        await using var member = await EnterAsync(world, 7301, "Stay");
        await Advance(engine, world, clock);
        Assert.Null(await world.TrySevenSignsActionAsync(member.Session, Join(SevenSignsCabal.Dawn, SevenSignsSeal.Gnosis), CancellationToken.None));
        Assert.Null(await world.TryDungeonAsync(member.Session, Enter(9), CancellationToken.None));

        await Advance(engine, world, clock);
        await Advance(engine, world, clock);
        Assert.Equal(SevenSignsPeriod.SealValidation, engine.Period);
        Assert.Equal(9, world.PlayerDungeonId(member.Session.ObjectId));

        member.Checkpoint();
        await Advance(engine, world, clock);
        Assert.Equal(SevenSignsPeriod.Recruiting, engine.Period);
        Assert.Equal(0, world.PlayerDungeonId(member.Session.ObjectId));
        Assert.True(DungeonCatalog.LoadDefault().TryGet(9, out var site));
        Assert.Equal(site.ExitX, member.Session.X);
        var frames = await member.ReadNewAsync();
        Assert.Contains(frames, frame => frame.Type == (ushort)MsgType.SystemMessage &&
            MessagePackSerializer.Deserialize<SystemMessageMsg>(frame.Body).Id == DungeonProtocol.KickedUnsignedMessage);
    }

    [Fact]
    public async Task A_configured_adena_fee_is_taken_once()
    {
        var clock = new MutableGameClock(Start);
        var engine = new SevenSignsEngine(clock);
        var catalog = new DungeonCatalog(
        [
            new DungeonSite(1, DungeonKind.Necropolis, "Fee", 1, 2, 10, 20, 30, 11, 21, 31, DungeonProtocol.AdenaItemId, 100)
        ]);
        var world = new ZoneWorld(sevenSigns: engine, dungeons: catalog, dungeonAccess: new DungeonAccessOptions());
        await using var member = await EnterAsync(world, 7401, "Payer");
        await Advance(engine, world, clock);
        Assert.Null(await world.TrySevenSignsActionAsync(member.Session, Join(SevenSignsCabal.Dusk, SevenSignsSeal.Avarice), CancellationToken.None));

        Assert.Equal(DungeonProtocol.Fee, await world.TryDungeonAsync(member.Session, Enter(1), CancellationToken.None));
        Assert.NotEqual(10, member.Session.X);

        Assert.Null(await world.TryGrantItemAsync(member.Session, new GrantItemRequestMsg
        {
            ItemId = DungeonProtocol.AdenaItemId,
            Count = 100
        }, CancellationToken.None));
        member.Checkpoint();
        Assert.Null(await world.TryDungeonAsync(member.Session, Enter(1), CancellationToken.None));
        Assert.Equal(10, member.Session.X);
        Assert.Equal(20, member.Session.Y);
        var paid = await member.ReadNewAsync();
        Assert.Contains(paid, frame => frame.Type == (ushort)MsgType.InventoryUpdate);

        Assert.Equal(DungeonProtocol.Fee, await world.TryDungeonAsync(member.Session, Enter(1), CancellationToken.None));
    }

    [Fact]
    public async Task Dead_members_and_unknown_dungeons_do_not_move()
    {
        var clock = new MutableGameClock(Start);
        var engine = new SevenSignsEngine(clock);
        var world = new ZoneWorld(sevenSigns: engine, dungeonAccess: new DungeonAccessOptions());
        await using var member = await EnterAsync(world, 7501, "Dead");
        await Advance(engine, world, clock);
        Assert.Null(await world.TrySevenSignsActionAsync(member.Session, Join(SevenSignsCabal.Dawn, SevenSignsSeal.Avarice), CancellationToken.None));
        Assert.True(world.TrySetPlayerDead(member.Session.ObjectId, true));
        var x = member.Session.X;
        Assert.Equal(InventoryProtocol.Dead, await world.TryDungeonAsync(member.Session, Enter(1), CancellationToken.None));
        Assert.Equal(x, member.Session.X);
        Assert.True(world.TrySetPlayerDead(member.Session.ObjectId, false));
        Assert.Equal(DungeonProtocol.Unknown, await world.TryDungeonAsync(member.Session, Enter(99), CancellationToken.None));
        Assert.Equal("bad_request", await world.TryDungeonAsync(member.Session, new DungeonEnterRequestMsg
        {
            DungeonId = 1,
            Action = 0
        }, CancellationToken.None));
    }

    [Fact]
    public void Enter_request_round_trips()
    {
        var request = new DungeonEnterRequestMsg { DungeonId = 9, Action = DungeonProtocol.ActionEnter };
        var copy = MessagePackSerializer.Deserialize<DungeonEnterRequestMsg>(MessagePackSerializer.Serialize(request));
        Assert.Equal(9, copy.DungeonId);
        Assert.Equal(DungeonProtocol.ActionEnter, copy.Action);

        var entered = new DungeonEnteredMsg
        {
            DungeonId = 9,
            X = -53174,
            Y = -250275,
            Z = -7911,
            Inside = true,
            Action = DungeonProtocol.ActionEnter
        };
        var back = MessagePackSerializer.Deserialize<DungeonEnteredMsg>(MessagePackSerializer.Serialize(entered));
        Assert.True(back.Inside);
        Assert.Equal(-250275, back.Y);
        Assert.Equal(DungeonProtocol.EnterRequestOpcode, (ushort)MsgType.DungeonEnterRequest);
        Assert.Equal(DungeonProtocol.EnteredOpcode, (ushort)MsgType.DungeonEntered);
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

    static async Task Advance(SevenSignsEngine engine, ZoneWorld world, MutableGameClock clock)
    {
        clock.UtcNow = engine.NextChange;
        Assert.True(await world.AdvanceSevenSignsAsync(CancellationToken.None));
    }

    static async Task<Speaker> EnterAsync(ZoneWorld world, int objectId, string name)
    {
        var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(objectId, "gate", name, 1);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        var speaker = new Speaker(session, stream);
        await speaker.WelcomeAsync();
        return speaker;
    }

    sealed class Speaker : IAsyncDisposable
    {
        readonly MemoryStream _stream;
        long _mark;

        public Speaker(ZoneSession session, MemoryStream stream)
        {
            Session = session;
            _stream = stream;
        }

        public ZoneSession Session { get; }

        public void Checkpoint() => _mark = _stream.Position;

        public async Task WelcomeAsync()
        {
            var frames = await ReadNewAsync();
            Assert.Equal((ushort)MsgType.SystemMessage, frames[^1].Type);
        }

        public async Task<List<(ushort Type, byte[] Body)>> ReadNewAsync()
        {
            _stream.Position = _mark;
            var frames = new List<(ushort, byte[])>();
            while (true)
            {
                var frame = await FrameIO.ReadAsync(_stream);
                if (frame == null)
                    break;
                frames.Add(frame.Value);
            }

            _mark = _stream.Position;
            return frames;
        }

        public async ValueTask DisposeAsync() => await _stream.DisposeAsync();
    }
}
