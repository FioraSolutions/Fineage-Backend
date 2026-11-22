using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Duels after Mobius CT 0: ask / answer, canDuel refusals, countdown, fight to 1 HP without flag / karma, surrender,
/// timeout, cancel on interference / distance / logout, party duel arena, cubic targeting, wire.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetDuelTests
{
    const int X = 46045, Y = 41251, Z = -3440;
    const int StormSkill = 10;
    static readonly CancellationToken Ct = CancellationToken.None;

    static (WorldBench Bench, MutableClock Clock) Open()
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        return (new WorldBench(clock: () => clock.Now, geo: GeoEngine.Empty), clock);
    }

    static async Task<BenchPlayer> FighterAsync(WorldBench bench, string name, int dx = 0, int dy = 0)
    {
        var p = await bench.EnterAsync(name.ToLowerInvariant(), name, [new StoredItem(57, 1000, -1)], level: 60,
            x: X + dx, y: Y + dy, z: Z, clearRecorder: false);
        p.Player.CurHp = p.Player.MaxHp = 3000;
        p.Player.CurMp = p.Player.MaxMp = 1000;
        return p;
    }

    static Task<string?> Command(WorldBench bench, BenchPlayer p, DuelCommandKind kind, string name = "", bool party = false, int response = 0) =>
        bench.World.TryDuelCommandAsync(p.Session, new DuelCommandNetMsg { Kind = kind, TargetName = name, Party = party, Response = response }, Ct);

    static List<SystemMessageMsg> Lines(WorldBench bench, BenchPlayer p) =>
        bench.Rec.Of("FanoutSystemMessage")
            .Where(a => ((IReadOnlyList<int>)a[0]!).Contains(p.ObjectId))
            .Select(a => (SystemMessageMsg)a[1]!).ToList();

    static List<DuelEventNetMsg> Events(WorldBench bench, BenchPlayer p) =>
        bench.Rec.Of(nameof(IWorldEventPublisher.FanoutDuelEvent))
            .Where(a => ((IReadOnlyList<int>)a[0]!).Contains(p.ObjectId))
            .Select(a => (DuelEventNetMsg)a[1]!).ToList();

    static void Advance(WorldBench bench, MutableClock clock, int seconds)
    {
        for (int i = 0; i < seconds; i++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            bench.World.RunDuelTickForTest();
        }
    }

    static async Task StartAsync(WorldBench bench, MutableClock clock, BenchPlayer a, BenchPlayer b, bool party = false)
    {
        Assert.Null(await Command(bench, a, DuelCommandKind.Request, b.Player.Name, party));
        Assert.Null(await Command(bench, b, DuelCommandKind.Answer, party: party, response: 1));
        Advance(bench, clock, party ? 3 + 20 + 4 : 3 + 1 + 4);
    }

    [Fact]
    public async Task One_on_one_duel_runs_ask_countdown_fight_to_one_hp_and_restores_without_flag_or_karma()
    {
        var (bench, clock) = Open();
        var a = await FighterAsync(bench, "Alpha");
        var b = await FighterAsync(bench, "Bravo", dx: 100);

        Assert.Null(await Command(bench, a, DuelCommandKind.Request, "Bravo"));
        var ask = Assert.Single(Events(bench, b));
        Assert.Equal(DuelEventKind.Ask, ask.Kind);
        Assert.Equal("Alpha", ask.Name);
        Assert.Contains(Lines(bench, b), l => l.Id == ZoneWorld.DuelSysChallengesYou);

        Assert.Null(await Command(bench, b, DuelCommandKind.Answer, response: 1));
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysTheyAccepted);
        Assert.NotEqual(0, a.Player.DuelId);
        Assert.Equal(DuelFighterState.None, a.Player.DuelState); // countdown still runs

        Advance(bench, clock, 3 + 1 + 4);
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysBeginIn);
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysBegin);
        Assert.Equal(DuelFighterState.Duelling, a.Player.DuelState);
        Assert.Equal((byte)1, a.Player.DuelTeam);
        Assert.Equal((byte)2, b.Player.DuelTeam);
        Assert.Contains(Events(bench, b), e => e.Kind == DuelEventKind.Start && e.Team == 2);
        Assert.Equal((byte)2, bench.World.NetUserInfo(b.Player).Team);

        // The other side is hostile without Ctrl and hits never flag.
        Assert.True(bench.World.PlayerAutoAttackableForTest(a.Player, b.Player));
        bench.World.SkillDamagePlayerForTest(a.Player, b.Player, 1000);
        Assert.False(a.Player.PvpFlagged);
        Assert.Equal(2000, b.Player.CurHp);
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutDuelUserInfo)));

        // Lethal hit: stays at 1 HP, beaten, not dead, no karma.
        bench.World.SkillDamagePlayerForTest(a.Player, b.Player, 5000);
        Assert.Equal(1, b.Player.CurHp);
        Assert.False(b.Player.Dead);
        Assert.Equal(DuelFighterState.Dead, b.Player.DuelState);
        Assert.Equal(0, a.Player.Karma);
        // Further hits do nothing.
        bench.World.SkillDamagePlayerForTest(a.Player, b.Player, 5000);
        Assert.Equal(1, b.Player.CurHp);

        Advance(bench, clock, 1);
        Assert.Equal(DuelFighterState.Winner, a.Player.DuelState);
        Advance(bench, clock, 5);
        Assert.Equal(0, a.Player.DuelId);
        Assert.Equal(0, b.Player.DuelId);
        Assert.Equal((byte)0, b.Player.DuelTeam);
        Assert.Equal(3000, b.Player.CurHp); // restored
        Assert.Contains(Lines(bench, b), l => l.Id == ZoneWorld.DuelSysWon && l.Params.Contains("Alpha"));
        Assert.Contains(Events(bench, b), e => e.Kind == DuelEventKind.End && e.Name == "Alpha");
        Assert.Equal(0, bench.World.DuelCountForTest);
    }

    [Fact]
    public async Task Requests_are_refused_by_can_duel_range_and_decline()
    {
        var (bench, clock) = Open();
        var a = await FighterAsync(bench, "Alpha");
        var b = await FighterAsync(bench, "Bravo", dx: 100);
        var far = await FighterAsync(bench, "Faraway", dx: 1000);

        b.Player.CurHp = 1000; // below 50 %
        await Command(bench, a, DuelCommandKind.Request, "Bravo");
        Assert.Contains(Lines(bench, a), l => l.Id == 2019);
        b.Player.CurHp = 3000;

        await Command(bench, a, DuelCommandKind.Request, "Faraway");
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysTooFar);

        await Command(bench, a, DuelCommandKind.Request, "Nobody");
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysNoOpponent);

        a.Player.CombatUntilUnixMs = clock.Now.ToUnixTimeMilliseconds() + 10_000;
        await Command(bench, a, DuelCommandKind.Request, "Bravo");
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysCannotRequest);
        a.Player.CombatUntilUnixMs = 0;

        await Command(bench, a, DuelCommandKind.Request, "Bravo");
        await Command(bench, b, DuelCommandKind.Answer, response: -1);
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysDeclined);
        Assert.Equal(0, a.Player.DuelId);

        // -1 = refuse further asks this session.
        await Command(bench, a, DuelCommandKind.Request, "Bravo");
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysBusy);
    }

    [Fact]
    public async Task Surrender_gives_the_win_and_timeout_ends_in_a_tie_with_restore()
    {
        var (bench, clock) = Open();
        var a = await FighterAsync(bench, "Alpha");
        var b = await FighterAsync(bench, "Bravo", dx: 100);
        await StartAsync(bench, clock, a, b);
        bench.World.SkillDamagePlayerForTest(b.Player, a.Player, 500);

        Assert.Null(await Command(bench, a, DuelCommandKind.Surrender));
        Advance(bench, clock, 1 + 5);
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysWithdrew);
        Assert.Equal(3000, a.Player.CurHp);
        Assert.Equal(0, a.Player.DuelId);

        clock.Now = clock.Now.AddSeconds(16); // attack stance must be over (canDuel 2021)
        await StartAsync(bench, clock, a, b);
        bench.World.SkillDamagePlayerForTest(b.Player, a.Player, 500);
        clock.Now = clock.Now.AddSeconds(121);
        bench.World.RunDuelTickForTest();
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysTie);
        Assert.Equal(3000, a.Player.CurHp);
        Assert.Equal(0, b.Player.DuelId);
    }

    [Fact]
    public async Task Interference_distance_and_logout_cancel_a_one_on_one_duel_without_restore()
    {
        var (bench, clock) = Open();
        var a = await FighterAsync(bench, "Alpha");
        var b = await FighterAsync(bench, "Bravo", dx: 100);
        var c = await FighterAsync(bench, "Charlie", dx: 200);

        await StartAsync(bench, clock, a, b);
        // A fighter cannot pick on an outsider; an outsider's hit interrupts the duel.
        Assert.True(bench.World.DuelTargetRefusedForTest(a.Player, c.Player));
        bench.World.SkillDamagePlayerForTest(c.Player, a.Player, 500);
        Assert.Equal(DuelFighterState.Interrupted, a.Player.DuelState);
        Advance(bench, clock, 1);
        Assert.Equal(0, a.Player.DuelId);
        Assert.Equal(2500, a.Player.CurHp); // canceled: no restore
        Assert.Contains(Lines(bench, a), l => l.Id == ZoneWorld.DuelSysTie);

        a.Player.CurHp = 3000;
        clock.Now = clock.Now.AddSeconds(16);
        await StartAsync(bench, clock, a, b);
        Assert.Equal(DuelFighterState.Duelling, a.Player.DuelState);
        b.MoveTo(X + 2000, Y, Z);
        Advance(bench, clock, 1);
        Assert.Equal(0, a.Player.DuelId);

        b.MoveTo(X + 100, Y, Z);
        await StartAsync(bench, clock, a, b);
        Assert.Equal(DuelFighterState.Duelling, b.Player.DuelState);
        await bench.World.LeaveAsync(b.Session, Ct);
        Advance(bench, clock, 1);
        Assert.Equal(0, a.Player.DuelId);
        Assert.Equal(0, bench.World.DuelCountForTest);
    }

    [Fact]
    public async Task Party_duel_goes_to_the_arena_and_ends_when_a_whole_side_is_down()
    {
        var (bench, clock) = Open();
        var a1 = await FighterAsync(bench, "Ana");
        var a2 = await FighterAsync(bench, "Ari", dy: 50);
        var b1 = await FighterAsync(bench, "Bea", dx: 100);
        var b2 = await FighterAsync(bench, "Bob", dx: 100, dy: 50);
        bench.World.JoinPartyForTest(a1.Player, a2.Player);
        bench.World.JoinPartyForTest(b1.Player, b2.Player);

        // Only a party leader may challenge, and only another party.
        await Command(bench, a2, DuelCommandKind.Request, "Bea", party: true);
        Assert.Contains(Lines(bench, a2), l => l.Id == ZoneWorld.DuelSysCannotRequest);

        Assert.Null(await Command(bench, a1, DuelCommandKind.Request, "Bob", party: true));
        Assert.Contains(Events(bench, b1), e => e.Kind == DuelEventKind.Ask && e.Party); // to the leader
        Assert.Null(await Command(bench, b1, DuelCommandKind.Answer, party: true, response: 1));
        Advance(bench, clock, 3);
        Assert.Contains(Lines(bench, a2), l => l.Id == ZoneWorld.DuelSysTransport);
        Assert.Equal(ZoneWorld.DuelInstanceBase + a1.Player.DuelId, b2.Player.InstanceId);
        Assert.Equal(ZoneWorld.DuelArenaX, a1.Player.X);
        Advance(bench, clock, 20 + 4);
        Assert.Equal(DuelFighterState.Duelling, b2.Player.DuelState);
        Assert.True(bench.World.PlayerAutoAttackableForTest(a2.Player, b1.Player));
        Assert.False(bench.World.PlayerAutoAttackableForTest(a2.Player, a1.Player));

        bench.World.SkillDamagePlayerForTest(a1.Player, b1.Player, 9999);
        Advance(bench, clock, 1);
        Assert.NotEqual(0, a1.Player.DuelId); // Bob still stands
        bench.World.SkillDamagePlayerForTest(a2.Player, b2.Player, 9999);
        Advance(bench, clock, 1 + 5);
        Assert.Equal(0, b2.Player.DuelId);
        Assert.Equal(0, b2.Player.InstanceId);
        Assert.Equal(X + 100, b2.Player.X);
        Assert.Equal(3000, b1.Player.CurHp);
        Assert.Contains(Lines(bench, b1), l => l.Id == ZoneWorld.DuelSysPartyWon && l.Params.Contains("Ana"));
    }

    [Fact]
    public async Task Cubics_in_a_duel_only_target_the_other_side()
    {
        var (bench, clock) = Open();
        var a = await FighterAsync(bench, "Alpha");
        var b = await FighterAsync(bench, "Bravo", dx: 100);
        var c = await FighterAsync(bench, "Charlie", dx: 200);
        Assert.True(bench.World.SummonCubicForTest(a.Player, StormSkill, 1));

        a.Session.SelectedTargetId = b.ObjectId;
        Assert.Null(bench.World.CubicTargetForTest(a.Player)); // white player, no duel

        await StartAsync(bench, clock, a, b);
        Assert.Same(b.Player, bench.World.CubicTargetForTest(a.Player));
        c.Player.PvpFlagged = true;
        a.Session.SelectedTargetId = c.ObjectId;
        Assert.Null(bench.World.CubicTargetForTest(a.Player));
    }

    [Fact]
    public void Duel_messages_and_team_tail_round_trip()
    {
        var cmd = NetCodec.DecodeDuelCommand(NetCodec.EncodeDuelCommand(new DuelCommandNetMsg
        { Kind = DuelCommandKind.Answer, TargetName = "X", Party = true, Response = -1 }));
        Assert.Equal((DuelCommandKind.Answer, "X", true, -1), (cmd.Kind, cmd.TargetName, cmd.Party, cmd.Response));

        var ev = NetCodec.DecodeDuelEvent(NetCodec.EncodeDuelEvent(new DuelEventNetMsg
        { Kind = DuelEventKind.Start, Party = true, Name = "W", Team = 2 }));
        Assert.Equal((DuelEventKind.Start, true, "W", (byte)2), (ev.Kind, ev.Party, ev.Name, ev.Team));

        var info = NetCodec.DecodeDuelUserInfo(NetCodec.EncodeDuelUserInfo(new DuelUserInfoNetMsg
        { Name = "N", ObjectId = 5, ClassId = 6, Level = 7, Hp = 8, MaxHp = 9, Mp = 10, MaxMp = 11, Cp = 12, MaxCp = 13 }));
        Assert.Equal(13, info.MaxCp);
        Assert.Equal("N", info.Name);

        var spawn = NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(new EntitySpawnMsg { Name = "P", Cubics = [1], Team = 1 }));
        Assert.Equal((byte)1, spawn.Team);
        Assert.Equal(new[] { 1 }, spawn.Cubics);
        // An old peer without the team byte still decodes.
        var bytes = NetCodec.EncodeEntitySpawn(new EntitySpawnMsg { Name = "P", Team = 2 });
        Assert.Equal((byte)0, NetCodec.DecodeEntitySpawn(bytes.AsSpan(0, bytes.Length - 1 - 8)).Team);

        var ui = NetCodec.DecodeUserInfo(NetCodec.EncodeUserInfo(new UserInfoNetMsg { Team = 2 }));
        Assert.Equal((byte)2, ui.Team);
    }
}
