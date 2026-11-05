using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Olympiad;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public class OlympiadTests
{
    static readonly DateTimeOffset Evening = new(2026, 10, 28, 18, 30, 0, TimeSpan.Zero);
    static readonly long EveningMs = Evening.ToUnixTimeMilliseconds();

    [Fact]
    public void Schedule_opens_at_18_and_the_month_ends_at_noon_on_the_first()
    {
        long noon = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var before = OlympiadSchedule.At(noon, OlympiadSettings.Mobius);
        Assert.False(before.Open);
        Assert.Equal(EveningMs - 30 * 60 * 1000, before.StartUnixMs);

        var open = OlympiadSchedule.At(EveningMs, OlympiadSettings.Mobius);
        Assert.True(open.Open);
        Assert.Equal(EveningMs - 30 * 60 * 1000, open.StartUnixMs);
        Assert.Equal(open.StartUnixMs + OlympiadProtocol.CompetitionMillis, open.EndUnixMs);

        long after = new DateTimeOffset(2026, 10, 29, 1, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var next = OlympiadSchedule.At(after, OlympiadSettings.Mobius);
        Assert.False(next.Open);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 18, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), next.StartUnixMs);

        var board = new OlympiadBoard(Evening);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), board.View(1, EveningMs).OlympiadEndUnixMs);
    }

    [Fact]
    public void Countdown_steps_match_the_mobius_loop()
    {
        var stadium = OlympiadSchedule.StadiumCountdown(1_000, 120);
        Assert.Equal([120, 60, 30, 15, 10, 5, 4, 3, 2, 1], stadium.Select(row => row.Seconds).ToArray());
        Assert.Equal(1_000 + 120_000, stadium[^1].AtUnixMs + 1_000);

        var fight = OlympiadSchedule.FightCountdown(0);
        Assert.Equal([60, 50, 40, 30, 20, 10, 5, 4, 3, 2, 1], fight.Select(row => row.Seconds).ToArray());
        Assert.Equal(60_000, fight[^1].AtUnixMs + 1_000);

        var back = OlympiadSchedule.ReturnCountdown(0);
        Assert.Equal([40, 30, 20, 10, 5, 4, 3, 2, 1], back.Select(row => row.Seconds).ToArray());
    }

    [Fact]
    public void Stadium_spawns_sit_inside_the_zone()
    {
        var catalog = OlympiadStadiumCatalog.LoadDefault();
        Assert.Equal(22, catalog.Count);
        Assert.Equal(11070, catalog[0].ZoneId);
        Assert.Equal(11091, catalog[21].ZoneId);
        foreach (var site in catalog.All)
        {
            Assert.True(site.Contains(site.X, site.Y, site.Z), site.Name);
            Assert.True(site.Contains(site.X + OlympiadProtocol.SpawnSeparation, site.Y, site.Z), site.Name + " east");
            Assert.True(site.Contains(site.X - OlympiadProtocol.SpawnSeparation, site.Y, site.Z), site.Name + " west");
        }
    }

    [Fact]
    public void Point_transfer_uses_the_classed_and_non_classed_divisors()
    {
        var one = new OlympiadRules.Score(18, 0, 0, 0, 0);
        var two = new OlympiadRules.Score(18, 0, 0, 0, 0);
        var win = OlympiadRules.Win(one, two, oneWins: true, classed: true, OlympiadProtocol.MaxPoints, 50, 7);
        Assert.Equal(6, win.DeltaOne);
        Assert.Equal(24, win.One.Points);
        Assert.Equal(12, win.Two.Points);
        Assert.Equal(1, win.One.Won);
        Assert.Equal(1, win.Two.Lost);
        Assert.Equal(1, win.One.Done);
        Assert.Equal(50, win.RewardCount);

        var open = OlympiadRules.Win(one, two, oneWins: false, classed: false, OlympiadProtocol.MaxPoints, 30, 8);
        Assert.Equal(-3, open.DeltaOne);
        Assert.Equal(3, open.DeltaTwo);
        Assert.Equal(30, open.RewardCount);

        var capped = OlympiadRules.Win(
            new OlympiadRules.Score(100, 9, 0, 0, 0),
            new OlympiadRules.Score(40, 4, 0, 0, 0),
            true,
            true,
            10,
            50,
            1);
        Assert.Equal(10, capped.DeltaOne);

        var tie = OlympiadRules.Tie(one, two, 10);
        Assert.Equal(15, tie.One.Points);
        Assert.Equal(15, tie.Two.Points);
        Assert.Equal(1, tie.One.Drawn);
        Assert.Equal(0, tie.RewardCount);

        var crashed = OlympiadRules.Crash(one, two, oneLeft: true, twoLeft: false, classed: true, 10, 1, 2);
        Assert.Equal(12, crashed.One.Points);
        Assert.Equal(24, crashed.Two.Points);
        Assert.Equal(0, crashed.RewardCount);
        Assert.Equal(2, crashed.WinnerObjectId);

        var fault = OlympiadRules.Default(one, two, true, false, 10);
        Assert.Equal(12, fault.One.Points);
        Assert.Equal(18, fault.Two.Points);
        Assert.Equal(0, fault.One.Done);
        Assert.Equal(12, OlympiadRules.AddWeekly(18, 0, 10, 3));
        Assert.Equal(12, OlympiadRules.ClampStored(18, 0, 10, 3));
    }

    [Fact]
    public void Registration_is_noble_only_and_queues_need_the_configured_minimum()
    {
        var board = new OlympiadBoard(Evening);
        var commoner = Noble(1, "Ann");
        var denied = board.Register(commoner, classed: true, EveningMs);
        Assert.Equal(OlympiadProtocol.Noble, denied.Error);

        Assert.True(board.GrantNoblesse(1));
        Assert.Null(board.Register(commoner, classed: true, EveningMs).Error);
        Assert.Equal(18, board.PointsOf(1));
        Assert.Equal(OlympiadProtocol.Classed, board.Register(commoner, classed: true, EveningMs).Error);
        Assert.Null(board.Unregister(commoner, EveningMs).Error);
        Assert.Equal(OlympiadProtocol.None, board.Unregister(commoner, EveningMs).Error);

        var late = EveningMs + (6 * 60 - 30) * 60 * 1000 - 9 * 60 * 1000;
        Assert.Equal(OlympiadProtocol.Late, board.Register(commoner, true, late).Error);

        var stuffed = commoner with { InventorySlots = 64 };
        Assert.Equal(OlympiadProtocol.Inventory, board.Register(stuffed, true, EveningMs).Error);
        Assert.Equal(OlympiadProtocol.Subclass, board.Register(commoner with { ClassId = 2, BaseClassId = 1 }, true, EveningMs).Error);
        Assert.Equal(OlympiadProtocol.Cursed, board.Register(commoner with { CursedWeapon = true }, false, EveningMs).Error);

        var poor = new OlympiadBoard(Evening, new OlympiadSettings { StartPoints = 2 });
        Assert.True(poor.GrantNoblesse(4));
        Assert.Equal(OlympiadProtocol.Points, poor.Register(Noble(4, "Bea"), true, EveningMs).Error);
        Assert.Equal(2, poor.PointsOf(4));
        Assert.Equal(0, poor.ClassedWaiting(0));

        for (int id = 10; id < 14; id++)
        {
            Assert.True(board.GrantNoblesse(id));
            Assert.Null(board.Register(Noble(id, "C" + id), true, EveningMs).Error);
        }

        Assert.Equal(0, board.LiveMatchCount);
        board.Advance(EveningMs, Online);
        Assert.Equal(0, board.LiveMatchCount);
        Assert.True(board.GrantNoblesse(14));
        Assert.Null(board.Register(Noble(14, "C14"), true, EveningMs).Error);
        board.Advance(EveningMs + 31_000, Online);
        Assert.Equal(2, board.LiveMatchCount);
        Assert.Equal(1, board.ClassedWaiting(0));
    }

    [Fact]
    public void Non_classed_queue_fills_the_first_half_of_the_stadiums()
    {
        var board = new OlympiadBoard(Evening);
        for (int id = 1; id <= 9; id++)
        {
            Assert.True(board.GrantNoblesse(id));
            Assert.Null(board.Register(Noble(id, "N" + id, classId: id), classed: false, EveningMs).Error);
        }

        for (int id = 20; id < 25; id++)
        {
            Assert.True(board.GrantNoblesse(id));
            Assert.Null(board.Register(Noble(id, "K" + id), classed: true, EveningMs).Error);
        }

        board.Advance(EveningMs, Online);
        Assert.Equal(4 + 2, board.LiveMatchCount);
        Assert.Equal(1, board.NonClassedWaiting);
        Assert.Equal(1, board.ClassedWaiting(0));
    }

    [Fact]
    public void Leave_before_the_port_defaults_and_a_report_pays_the_gate_pass()
    {
        var settings = new OlympiadSettings { ClassedMin = 2, WaitSeconds = 5, BattleMillis = 10_000 };
        var board = new OlympiadBoard(Evening, settings);
        Assert.True(board.GrantNoblesse(1));
        Assert.True(board.GrantNoblesse(2));
        Assert.Null(board.Register(Noble(1, "Ann"), true, EveningMs).Error);
        Assert.Null(board.Register(Noble(2, "Bea"), true, EveningMs).Error);
        board.Advance(EveningMs, Online);
        int match = board.MatchIdFor(1);
        Assert.True(match > 0);

        var left = board.Leave(1, EveningMs, Online);
        Assert.Null(left.Error);
        Assert.Equal(12, board.PointsOf(1));
        Assert.Equal(18, board.PointsOf(2));
        Assert.Equal(0, board.View(1, EveningMs).CompetitionsDone);
        Assert.Equal(0, board.LiveMatchCount);
        Assert.Contains(
            new[] { OlympiadProtocol.OutcomeDefaultOne, OlympiadProtocol.OutcomeDefaultTwo },
            outcome => outcome == Assert.Single(left.Results).Outcome);

        Assert.Null(board.Register(Noble(1, "Ann"), true, EveningMs).Error);
        Assert.Null(board.Register(Noble(2, "Bea"), true, EveningMs).Error);
        board.Advance(EveningMs + 31_000, Online);
        long fight = EveningMs + 31_000 + 70_000;
        board.Advance(fight, Online);
        var win = board.Report(board.MatchIdFor(1), 1, fight);
        Assert.Null(win.Error);
        Assert.Equal(16, board.PointsOf(1));
        Assert.Equal(14, board.PointsOf(2));
        Assert.Equal(OlympiadProtocol.RewardItemId, Assert.Single(win.Grants).ItemId);
        Assert.Equal(50, Assert.Single(win.Grants).Count);
        Assert.Equal(1, win.Grants[0].CharacterId);

        Assert.NotEqual(0, board.MatchIdFor(1));
        board.Advance(fight + OlympiadProtocol.ReturnCountdownMillis, Online);
        Assert.Equal(0, board.MatchIdFor(1));
    }

    [Fact]
    public void Disconnect_after_the_port_is_a_crash_and_timeout_is_a_tie()
    {
        var settings = new OlympiadSettings { ClassedMin = 2, NonClassedMin = 2, WaitSeconds = 5, BattleMillis = 10_000 };
        var board = new OlympiadBoard(Evening, settings);
        Assert.True(board.GrantNoblesse(1));
        Assert.True(board.GrantNoblesse(2));
        Assert.Null(board.Register(Noble(1, "Ann"), true, EveningMs).Error);
        Assert.Null(board.Register(Noble(2, "Bea"), true, EveningMs).Error);
        board.Advance(EveningMs, Standing);
        board.Advance(EveningMs + 5_000, Standing);
        var crash = board.Disconnect(2, EveningMs + 5_000, Standing);
        Assert.Equal(24, board.PointsOf(1));
        Assert.Equal(12, board.PointsOf(2));
        Assert.Equal(1, board.View(1, EveningMs).CompetitionsDone);
        Assert.Equal(0, Assert.Single(crash.Results).RewardCount);
        Assert.Contains(crash.Moves, move => move.CharacterId == 1 && move.InstanceId == 0);

        Assert.Null(board.Register(Noble(1, "Ann"), false, EveningMs + 60_000).Error);
        Assert.Null(board.Register(Noble(2, "Bea"), false, EveningMs + 60_000).Error);
        long start = EveningMs + 90_000;
        board.Advance(start, Standing);
        long timeout = start + 5_000 + 5_000 + 60_000 + 10_000;
        var step = board.Advance(timeout, Standing);
        Assert.Equal(OlympiadProtocol.OutcomeTie, Assert.Single(step.Results).Outcome);
        Assert.Equal(20, board.PointsOf(1));
        Assert.Equal(10, board.PointsOf(2));
        Assert.Empty(step.Grants);
    }

    [Fact]
    public void Points_and_noblesse_round_trip_and_weekly_points_honor_the_cap()
    {
        string path = Path.Combine(Path.GetTempPath(), "olympiad-" + Guid.NewGuid().ToString("n") + ".json");
        var early = new DateTimeOffset(2026, 10, 1, 18, 30, 0, TimeSpan.Zero);
        var board = new OlympiadBoard(early, store: new JsonOlympiadStore(path));
        Assert.True(board.GrantNoblesse(9));
        Assert.Null(board.Register(Noble(9, "Ivo"), true, early.ToUnixTimeMilliseconds()).Error);
        Assert.Equal(18, board.PointsOf(9));

        var reloaded = new OlympiadBoard(early, store: new JsonOlympiadStore(path));
        Assert.True(reloaded.IsNoble(9));
        Assert.Equal(12, reloaded.PointsOf(9));

        long week = early.AddDays(7).ToUnixTimeMilliseconds();
        var again = new OlympiadBoard(early, store: new MemoryOlympiadStore());
        Assert.True(again.GrantNoblesse(3));
        Assert.Null(again.Register(Noble(3, "Kai"), true, early.ToUnixTimeMilliseconds()).Error);
        again.Advance(week, Online);
        Assert.Equal(12, again.PointsOf(3));
        Assert.Equal(OlympiadProtocol.PeriodCompetition, again.View(3, week).Period);
    }

    [Fact]
    public async Task World_ports_the_pair_and_report_pays_the_pass_then_sends_them_home()
    {
        var store = JsonAccountStore.InMemory();
        Assert.True(new LoginService(store, new HandoffTokens("test")).Register("ann", "secret1").Ok);
        Assert.True(new LoginService(store, new HandoffTokens("test")).Register("bea", "secret2").Ok);
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter("ann", "Ann", HumanClasses.Fighter, 0, 0, out var ann));
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter("bea", "Bea", HumanClasses.Fighter, 0, 0, out var bea));
        Assert.NotNull(ann);
        Assert.NotNull(bea);
        DateTimeOffset now = Evening;
        var world = new ZoneWorld(
            accounts: store,
            clock: () => now,
            olympiadSettings: new OlympiadSettings { ClassedMin = 2, WaitSeconds = 5, BattleMillis = 60_000 });
        var left = await Enter(world, "ann", ann);
        var right = await Enter(world, "bea", bea);
        Assert.True(world.GrantNoblesse(ann.Id));
        Assert.True(world.GrantNoblesse(bea.Id));
        Assert.Null(await world.TryOlympiadRegisterAsync(left, new OlympiadRegisterRequestMsg { Classed = 1 }, CancellationToken.None));
        Assert.Null(await world.TryOlympiadRegisterAsync(right, new OlympiadRegisterRequestMsg { Classed = 1 }, CancellationToken.None));

        now = now.AddSeconds(1);
        await world.AdvanceOlympiadAsync();
        int homeLeftX = left.X;
        int homeLeftY = left.Y;
        int homeLeftZ = left.Z;
        int homeRightX = right.X;
        int homeRightY = right.Y;
        int homeRightZ = right.Z;
        long start = now.ToUnixTimeMilliseconds();
        now = DateTimeOffset.FromUnixTimeMilliseconds(start + 70_000);
        await world.AdvanceOlympiadAsync();

        int east = world.Olympiad.Stadiums[0].X + OlympiadProtocol.SpawnSeparation;
        int west = world.Olympiad.Stadiums[0].X - OlympiadProtocol.SpawnSeparation;
        Assert.Equal(new[] { east, west }.OrderBy(value => value), new[] { left.X, right.X }.OrderBy(value => value));
        Assert.Equal(world.Olympiad.Stadiums[0].Y, left.Y);
        Assert.Equal(world.Olympiad.Stadiums[0].Z, left.Z);

        Assert.Null(world.ReportOlympiadResult(world.Olympiad.MatchIdFor(ann.Id), ann.Id, now));
        Assert.Equal(24, world.Olympiad.PointsOf(ann.Id));
        Assert.Equal(12, world.Olympiad.PointsOf(bea.Id));
        Assert.Contains(store.FindById("ann", ann.Id)!.Items!, row => row.ItemId == OlympiadProtocol.RewardItemId && row.Count == 50);

        now = now.AddMilliseconds(OlympiadProtocol.ReturnCountdownMillis);
        await world.AdvanceOlympiadAsync();
        Assert.Equal(homeLeftX, left.X);
        Assert.Equal(homeLeftY, left.Y);
        Assert.Equal(homeLeftZ, left.Z);
        Assert.Equal(homeRightX, right.X);
        Assert.Equal(homeRightY, right.Y);
        Assert.Equal(homeRightZ, right.Z);
        Assert.Equal(0, world.Olympiad.MatchIdFor(ann.Id));
    }

    [Fact]
    public async Task Disconnect_during_the_countdown_defaults_the_noble()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens("test"));
        Assert.True(login.Register("ann", "secret1").Ok);
        Assert.True(login.Register("bea", "secret2").Ok);
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter("ann", "Ann", HumanClasses.Fighter, 0, 0, out var ann));
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter("bea", "Bea", HumanClasses.Fighter, 0, 0, out var bea));
        Assert.NotNull(ann);
        Assert.NotNull(bea);
        DateTimeOffset now = Evening;
        var world = new ZoneWorld(accounts: store, clock: () => now, olympiadSettings: new OlympiadSettings { ClassedMin = 2, WaitSeconds = 120 });
        var left = await Enter(world, "ann", ann);
        var right = await Enter(world, "bea", bea);
        int stayX = right.X;
        world.GrantNoblesse(ann.Id);
        world.GrantNoblesse(bea.Id);
        Assert.Null(await world.TryOlympiadRegisterAsync(left, new OlympiadRegisterRequestMsg { Classed = 1 }, CancellationToken.None));
        Assert.Null(await world.TryOlympiadRegisterAsync(right, new OlympiadRegisterRequestMsg { Classed = 1 }, CancellationToken.None));
        await world.AdvanceOlympiadAsync();
        Assert.NotEqual(0, world.Olympiad.MatchIdFor(bea.Id));

        await world.LeaveAsync(left, CancellationToken.None);
        Assert.Equal(12, world.Olympiad.PointsOf(ann.Id));
        Assert.Equal(18, world.Olympiad.PointsOf(bea.Id));
        Assert.Equal(0, world.Olympiad.MatchIdFor(bea.Id));
        Assert.Equal(stayX, right.X);
    }

    static async Task<ZoneSession> Enter(ZoneWorld world, string account, CharacterRecord character)
    {
        var session = new ZoneSession(new MemoryStream());
        session.Bind(character.Id, account, character.Name, character.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        return session;
    }

    static OlympiadActor Noble(int id, string name, int classId = 0) =>
        new(id, name, classId, classId, 0, OlympiadProtocol.InventoryLimit, false);

    static OlympiadPresence? Online(int id) =>
        new(true, false, 0, 0, 1000 + id, 2000, -3100, 1, 80, false);

    static OlympiadPresence? Standing(int id) =>
        new(true, false, 0, 0, 4000 + id, 5000, -3112, 1, 80, false);
}
