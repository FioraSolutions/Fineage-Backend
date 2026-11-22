using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Olympiad;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Block G2 after Mobius <c>OlympiadGame</c> / <c>Olympiad</c> / <c>Hero</c> / <c>OlympiadManager</c>: arena entry and
/// return, the knockout that is not a death, the damage-decided timeout, opponent-only targeting, the monthly hero
/// pick with gate pass settlement and ranking, the Monument of Heroes, and the manager pages.
/// </summary>
public sealed class NetOlympiadArenaTests
{
    static readonly DateTimeOffset Evening = new(2026, 10, 28, 18, 30, 0, TimeSpan.Zero);
    static readonly long EveningMs = Evening.ToUnixTimeMilliseconds();
    static Random Matchmaking => new(7);
    const int Duelist = 88, Dreadnought = 89, OlympiadManagerNpc = 31688;

    static OlympiadEntrant Noble(int id, string name, int classId = Duelist) =>
        new(id, name, classId, classId, 0, OlympiadProtocol.InventoryLimit, false);

    static OlympiadPresence? Standing(int id) =>
        new(true, false, 0, 0, 4000 + id, 5000, -3112, 1, 80, false);

    static string LastHtml(WorldBench bench) =>
        Assert.IsType<NpcHtmlMsg>(bench.Rec.Of("FanoutNpcHtml").Last()[1]).Html;

    static SystemMessageMsg LastSystem(WorldBench bench) =>
        Assert.IsType<SystemMessageMsg>(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);

    [Fact]
    public void Timeout_goes_to_the_side_that_dealt_more_damage()
    {
        var settings = new OlympiadSettings { ClassedMin = 2, WaitSeconds = 5, BattleMillis = 10_000 };
        var board = new OlympiadBoard(Evening, settings);
        board.GrantNoblesse(1);
        board.GrantNoblesse(2);
        Assert.Null(board.Register(Noble(1, "Ann"), true, EveningMs).Error);
        Assert.Null(board.Register(Noble(2, "Bea"), true, EveningMs).Error);
        board.Advance(EveningMs, Standing, Matchmaking);
        long fight = EveningMs + 5_000 + OlympiadProtocol.ArenaPauseMillis + OlympiadProtocol.BattleCountdownMillis;
        board.Advance(EveningMs + 5_000, Standing, Matchmaking);
        board.Advance(EveningMs + 5_000 + OlympiadProtocol.ArenaPauseMillis, Standing, Matchmaking);
        board.Advance(fight, Standing, Matchmaking);
        Assert.Equal(OlympiadMatchPhase.Fighting, board.MatchState(1)!.Value.Phase);

        board.NoteDamage(2, 300);
        board.NoteDamage(1, 100);
        var step = board.Advance(fight + 10_000, Standing, Matchmaking);
        var result = Assert.Single(step.Results);
        Assert.Equal(2, result.WinnerObjectId);
        Assert.Contains(step.Grants, g => g.CharacterId == 2 && g.ItemId == OlympiadProtocol.RewardItemId);
    }

    static OlympiadNobleRow Row(int id, string name, int classId, int points, int done, int won) =>
        new() { CharacterId = id, Name = name, ClassId = classId, Points = points, Done = done, Won = won, Lost = done - won };

    static OlympiadBoard SeededBoard(IOlympiadStore store, IEnumerable<OlympiadNobleRow> nobles, IEnumerable<OlympiadHeroRow>? heroes = null)
    {
        var snap = new OlympiadSnapshot
        {
            Cycle = 3,
            Period = OlympiadProtocol.PeriodCompetition,
            OlympiadEndUnixMs = EveningMs + 1_000,
            NextWeeklyUnixMs = EveningMs + 86_400_000,
            Granted = nobles.Select(n => n.CharacterId).ToList(),
            Nobles = nobles.ToList(),
            Heroes = heroes?.ToList() ?? []
        };
        store.Save(snap);
        return new OlympiadBoard(Evening, new OlympiadSettings(), store);
    }

    [Fact]
    public void Period_end_crowns_one_hero_per_class_and_settles_gate_passes()
    {
        var store = new MemoryOlympiadStore();
        var board = SeededBoard(store,
        [
            Row(1, "Ace", Duelist, points: 40, done: 12, won: 9),
            Row(2, "Bob", Duelist, points: 40, done: 10, won: 7),   // same points, fewer matches
            Row(3, "Cid", Duelist, points: 90, done: 8, won: 8),    // under nine matches: never ranked
            Row(4, "Dan", Dreadnought, points: 15, done: 9, won: 0), // no win: no hero, still ranked
        ],
        [new OlympiadHeroRow { CharacterId = 77, ClassId = Duelist, Name = "Old", Claimed = true, Count = 1 }]);
        Assert.True(board.IsHero(77));

        var step = board.Advance(EveningMs + 2_000, Standing, Matchmaking);
        Assert.Contains(77, step.RevokedHeroes);
        Assert.False(board.IsHero(77));
        Assert.True(board.IsUnclaimedHero(1));
        Assert.False(board.IsHero(1));
        Assert.Single(board.Heroes());
        Assert.Equal(["Ace", "Bob"], board.ClassLeaders(Duelist));
        Assert.Equal(["Dan"], board.ClassLeaders(Dreadnought));
        Assert.Equal(40, board.LastPeriodPoints(1));

        Assert.Equal((40 + OlympiadProtocol.HeroPoints) * OlympiadProtocol.GatePassesPerPoint, board.UnclaimedPasses(1));
        Assert.Equal(40 * OlympiadProtocol.GatePassesPerPoint, board.UnclaimedPasses(2));
        Assert.Equal(0, board.UnclaimedPasses(3));

        Assert.True(board.ClaimHero(1));
        Assert.False(board.ClaimHero(1));
        Assert.True(board.IsHero(1));

        // Heroes, the frozen table, and the passes survive a reload.
        var again = new OlympiadBoard(Evening.AddSeconds(3), new OlympiadSettings(), store);
        Assert.True(again.IsHero(1));
        Assert.Equal(["Ace", "Bob"], again.ClassLeaders(Duelist));
        Assert.Equal(40 * OlympiadProtocol.GatePassesPerPoint, again.TakePasses(2));
        Assert.Equal(0, again.TakePasses(2));
    }

    static async Task<(WorldBench Bench, BenchPlayer One, BenchPlayer Two, OlympiadMatchMsg Match, Func<DateTimeOffset, Task> At)> Fight()
    {
        var clock = Evening;
        var bench = new WorldBench(clock: () => clock, olympiadSettings: new OlympiadSettings { ClassedMin = 2, WaitSeconds = 5 });
        var one = await bench.EnterAsync("arena1", "One", classId: Duelist, level: 78);
        var two = await bench.EnterAsync("arena2", "Two", classId: Duelist, level: 78);
        bench.World.TrySetNoble(one.ObjectId, true);
        bench.World.TrySetNoble(two.ObjectId, true);
        Assert.Null(await bench.World.TryOlympiadRegisterAsync(one.Session, new OlympiadRegisterRequestMsg { Classed = 1 }, CancellationToken.None));
        Assert.Null(await bench.World.TryOlympiadRegisterAsync(two.Session, new OlympiadRegisterRequestMsg { Classed = 1 }, CancellationToken.None));
        async Task At(DateTimeOffset when)
        {
            clock = when;
            await bench.World.AdvanceOlympiadAsync(CancellationToken.None);
        }
        await At(Evening.AddSeconds(61));
        var match = Assert.IsType<OlympiadMatchMsg>(Assert.Single(bench.Rec.Of("FanoutOlympiadMatch"))[1]);
        return (bench, one, two, match, At);
    }

    [Fact]
    public async Task Arena_fills_the_bars_and_a_knockout_ends_the_match_without_a_death_penalty()
    {
        var (bench, one, two, match, at) = await Fight();
        var t = Evening.AddSeconds(61);
        one.Player.CurHp = 10;
        one.Player.Hero = true;
        bench.World.RefreshKnownSkillsForTest(one.Player);
        Assert.Equal(1, one.Player.KnownSkills[395]);

        t = t.AddSeconds(5);
        await at(t); // ported: arena pause
        Assert.True(bench.World.InOlympiadArena(one.ObjectId));
        Assert.Equal(one.Player.MaxHp, one.Player.CurHp);
        Assert.DoesNotContain(395, one.Player.KnownSkills.Keys);
        Assert.True(bench.World.OlympiadTargetRefusedForTest(one.Player, two.Player)); // not fighting yet

        t = t.AddMilliseconds(OlympiadProtocol.ArenaPauseMillis);
        await at(t);
        t = t.AddMilliseconds(OlympiadProtocol.BattleCountdownMillis);
        await at(t);
        Assert.False(bench.World.OlympiadTargetRefusedForTest(one.Player, two.Player));

        long exp = two.Player.Exp;
        bench.World.KillPlayerByNpcForTest(two.Player, one.Player);
        Assert.True(two.Player.Dead);
        Assert.Equal(exp, two.Player.Exp);
        var result = Assert.IsType<OlympiadResultMsg>(bench.Rec.Of("FanoutOlympiadResult").Last()[1]);
        Assert.Equal(one.ObjectId, result.WinnerObjectId);
        Assert.Equal(OlympiadProtocol.ClassedRewardCount, one.Inventory.CountOf(OlympiadProtocol.RewardItemId));

        t = t.AddMilliseconds(OlympiadProtocol.ReturnCountdownMillis);
        await at(t);
        Assert.False(bench.World.InOlympiadArena(two.ObjectId));
        Assert.False(two.Player.Dead);
        Assert.Equal(two.Player.MaxHp, two.Player.CurHp);
        Assert.Equal(0, two.Player.InstanceId);
        Assert.Equal(1, one.Player.KnownSkills[395]); // hero skills back home
    }

    [Fact]
    public async Task Arena_takes_off_hero_gear_refuses_restricted_items_and_readies_short_reuses()
    {
        var (bench, one, _, _, at) = await Fight();
        var ct = CancellationToken.None;
        one.Player.Hero = true;
        Assert.Null(await bench.World.TryGiveItemAsync(one.Session, 6611, 1, ct)); // Infinity Blade
        Assert.Null(await bench.World.TryUseItemAsync(one.Session, one.Slot(6611), ct));
        Assert.True(one.Inventory.Items.Single(i => i.ItemId == 6611).IsEquipped);
        Assert.Null(await bench.World.TryGiveItemAsync(one.Session, 1, 1, ct)); // Short Sword (no grade)
        Assert.Null(await bench.World.TryGiveItemAsync(one.Session, 3947, 10, ct)); // Blessed Spiritshot: No Grade
        long far = Evening.ToUnixTimeMilliseconds() + 3_600_000;
        one.Player.SkillReuseUntilMs[1] = far;   // Triple Slash, 3 s reuse
        one.Player.SkillReuseUntilMs[460] = far; // Symbol of the Assassin, 30 min reuse (kept, 900000 < delay)

        await at(Evening.AddSeconds(66)); // ported
        Assert.True(bench.World.InOlympiadArena(one.ObjectId));
        Assert.False(one.Inventory.Items.Single(i => i.ItemId == 6611).IsEquipped);
        Assert.DoesNotContain(1, one.Player.SkillReuseUntilMs.Keys);
        Assert.Equal(far, one.Player.SkillReuseUntilMs[460]);

        Assert.Equal(InventoryProtocol.OlympiadRestricted, await bench.World.TryUseItemAsync(one.Session, one.Slot(6611), ct));
        Assert.False(one.Inventory.Items.Single(i => i.ItemId == 6611).IsEquipped);

        Assert.Null(await bench.World.TryUseItemAsync(one.Session, one.Slot(1), ct));
        Assert.Null(await bench.World.TryUseItemAsync(one.Session, one.Slot(3947), ct));
        Assert.Equal(0, one.Player.SpiritshotCharge);
        Assert.Equal(10, one.Inventory.CountOf(3947));
        Assert.Null(await bench.World.TryGiveItemAsync(one.Session, 2509, 10, ct)); // plain Spiritshot: No Grade still works
        Assert.Null(await bench.World.TryUseItemAsync(one.Session, one.Slot(2509), ct));
        Assert.True(one.Player.SpiritshotCharge > 0);
    }

    [Fact]
    public async Task Arena_drops_cubics_granted_by_others()
    {
        const int LifeSkill = 67, StormSkill = 10, CubicMastery = 143;
        var (bench, one, _, _, at) = await Fight();
        one.Player.KnownSkills[CubicMastery] = 2;
        Assert.True(bench.World.SummonCubicForTest(one.Player, LifeSkill, 1, givenByOther: false));
        Assert.True(bench.World.SummonCubicForTest(one.Player, StormSkill, 1, givenByOther: true));
        Assert.Equal(2, one.Player.Cubics.Count);

        await at(Evening.AddSeconds(66));
        Assert.True(bench.World.InOlympiadArena(one.ObjectId));
        Assert.Single(one.Player.Cubics);
        Assert.DoesNotContain(one.Player.Cubics, cubic => cubic.GivenByOther);
    }

    [Fact]
    public async Task Observer_watches_a_stadium_untouchable_and_goes_back_where_they_stood()
    {
        var (bench, one, two, match, at) = await Fight();
        var watcher = await bench.EnterAsync("arena4", "Watcher", classId: Duelist, level: 40);
        var manager = bench.World.SpawnFolkForTests(OlympiadManagerNpc, watcher.Player.X + 20, watcher.Player.Y, watcher.Player.Z);
        var (x, y, z) = (watcher.Player.X, watcher.Player.Y, watcher.Player.Z);
        await at(Evening.AddSeconds(66)); // fighters ported

        Assert.Null(await watcher.BypassAsync(manager.ObjectId, "Olympiad 1"));
        Assert.Contains("Olympiad 3_", LastHtml(bench));
        Assert.Null(await watcher.BypassAsync(manager.ObjectId, $"Olympiad 3_{match.StadiumId}"));
        Assert.True(bench.World.OlympiadObserving(watcher.ObjectId));
        Assert.Equal(one.Player.InstanceId, watcher.Player.InstanceId);
        Assert.True(bench.World.OlympiadTargetRefusedForTest(one.Player, watcher.Player));
        Assert.True(bench.World.OlympiadTargetRefusedForTest(watcher.Player, two.Player));

        Assert.Null(await bench.World.TryOlympiadLeaveAsync(watcher.Session, CancellationToken.None));
        Assert.False(bench.World.OlympiadObserving(watcher.ObjectId));
        Assert.Equal((x, y, z), (watcher.Player.X, watcher.Player.Y, watcher.Player.Z));
        Assert.Equal(0, watcher.Player.InstanceId);
    }

    [Fact]
    public async Task Outsiders_cannot_reach_into_an_arena()
    {
        var (bench, one, _, _, at) = await Fight();
        var stranger = await bench.EnterAsync("arena3", "Three", classId: Duelist, level: 78);
        await at(Evening.AddSeconds(66));
        Assert.True(bench.World.OlympiadTargetRefusedForTest(stranger.Player, one.Player));
        Assert.True(bench.World.OlympiadTargetRefusedForTest(one.Player, stranger.Player));
    }

    [Fact]
    public async Task Manager_pages_register_show_points_and_rank()
    {
        var store = new MemoryOlympiadStore();
        store.Save(new OlympiadSnapshot
        {
            OlympiadEndUnixMs = EveningMs + 86_400_000L * 3,
            NextWeeklyUnixMs = EveningMs + 86_400_000L,
            LastPeriod = [Row(500, "Champ", Duelist, 70, 20, 15), Row(501, "Runner", Duelist, 30, 9, 4)]
        });
        var bench = new WorldBench(clock: () => Evening, olympiad: new OlympiadBoard(Evening, new OlympiadSettings(), store));
        var p = await bench.EnterAsync("mgr", "Mgr", classId: Duelist, level: 78);
        var manager = bench.World.SpawnFolkForTests(31688, p.Player.X + 20, p.Player.Y, p.Player.Z);

        Assert.Null(await p.Talk(manager.ObjectId));
        Assert.Contains("You are not a Noblesse", LastHtml(bench));
        Assert.Null(await p.BypassAsync(manager.ObjectId, "OlympiadNoble 5"));
        Assert.Contains("Only a Noblesse who has completed the 3rd class", LastHtml(bench));

        bench.World.TrySetNoble(p.ObjectId, true);
        Assert.Null(await p.Talk(manager.ObjectId));
        Assert.Contains("OlympiadDesc 1a", LastHtml(bench));
        Assert.Null(await p.BypassAsync(manager.ObjectId, "OlympiadNoble 5"));
        Assert.Equal(OlympiadProtocol.QueueClassed, bench.World.Olympiad.View(p.ObjectId, EveningMs).Queue);
        Assert.Null(await p.BypassAsync(manager.ObjectId, "OlympiadNoble 2"));
        Assert.Contains(">1<", LastHtml(bench));
        Assert.Null(await p.BypassAsync(manager.ObjectId, "OlympiadNoble 3"));
        Assert.Contains("There are 18 Grand Olympiad points", LastHtml(bench));
        Assert.Null(await p.BypassAsync(manager.ObjectId, "OlympiadNoble 6"));
        Assert.DoesNotContain("OlympiadNoble 10", LastHtml(bench));

        Assert.Null(await p.BypassAsync(manager.ObjectId, $"Olympiad 2_{Duelist}"));
        string ranking = LastHtml(bench);
        Assert.Contains(">Champ<", ranking);
        Assert.True(ranking.IndexOf("Champ", StringComparison.Ordinal) < ranking.IndexOf("Runner", StringComparison.Ordinal));
        Assert.DoesNotContain("%rank3%", ranking);

        Assert.Null(await p.BypassAsync(manager.ObjectId, "OlympiadDesc 2a"));
        Assert.Contains("OlympiadNoble 3", LastHtml(bench));
    }

    [Fact]
    public async Task Subclass_characters_get_the_subclass_page()
    {
        var bench = new WorldBench(clock: () => Evening);
        var p = await bench.EnterAsync("mgrsub", "Sub", classId: Duelist, level: 78);
        var master = bench.World.SpawnFolkForTests(30109, p.Player.X + 30, p.Player.Y, p.Player.Z);
        var manager = bench.World.SpawnFolkForTests(31688, p.Player.X + 20, p.Player.Y, p.Player.Z);
        bench.World.TrySetNoble(p.ObjectId, true);
        Assert.Null(await p.BypassAsync(master.ObjectId, "Subclass 4 5"));
        Assert.Null(await p.BypassAsync(manager.ObjectId, "OlympiadNoble 5"));
        Assert.Contains("sub-class", LastHtml(bench));
        Assert.Equal(OlympiadProtocol.QueueNone, bench.World.Olympiad.View(p.ObjectId, EveningMs).Queue);
    }

    [Fact]
    public async Task Monument_crowns_the_chosen_noble_and_hands_out_hero_gear_once()
    {
        var store = new MemoryOlympiadStore();
        var board = new OlympiadBoard(Evening, new OlympiadSettings(), store);
        var heroBench = new WorldBench(clock: () => Evening, olympiad: board);
        var h = await heroBench.EnterAsync("hero", "Hero", classId: Duelist, level: 78);
        // Seed the board so this character is the unclaimed hero of the finished period.
        var snap = store.Load();
        snap.Heroes = [new OlympiadHeroRow { CharacterId = h.ObjectId, ClassId = Duelist, Name = "Hero" }];
        store.Save(snap);
        board = new OlympiadBoard(Evening, new OlympiadSettings(), store);
        heroBench = new WorldBench(clock: () => Evening, olympiad: board);
        h = await heroBench.EnterAsync("hero", "Hero", classId: Duelist, level: 78);
        var monument = heroBench.World.SpawnFolkForTests(31690, h.Player.X + 20, h.Player.Y, h.Player.Z);
        heroBench.World.TrySetNoble(h.ObjectId, true);

        Assert.Null(await h.Talk(monument.ObjectId));
        Assert.Contains("HeroClaim", LastHtml(heroBench));
        Assert.Null(await h.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroWeapon"));
        Assert.Contains("You must become a Hero first", LastHtml(heroBench));
        Assert.Null(await h.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroClaim"));
        Assert.Contains("HeroReceive", LastHtml(heroBench));
        Assert.Null(await h.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroReceive"));
        Assert.True(h.Player.Hero);
        Assert.Equal(1, h.Player.KnownSkills[395]);
        Assert.True(heroBench.World.Olympiad.IsHero(h.ObjectId));
        Assert.True(Assert.IsType<UserInfoMsg>(heroBench.Rec.Of("FanoutUserInfo").Last()[1]).Hero);
        // Hero aura for everyone else: the CharInfo (remote EntitySpawn) carries the flag.
        Assert.True(Fineage.Zone.Net.NetEnterWorldProjector.BuildRemoteSpawn(h.Player, EntityId.FromObjectId(h.ObjectId)).Hero);

        Assert.Null(await h.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroWeapon"));
        Assert.Contains("MonumentOfHeroes 6611", LastHtml(heroBench));
        Assert.Null(await h.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes 6619"));
        Assert.Equal(1, h.Inventory.CountOf(6619));
        var olympiad = LedgerAccount.Of(SystemAccount.Olympiad);
        var bag = LedgerAccount.Inventory(h.ObjectId);
        Assert.Contains(heroBench.Ledger.Entries, e =>
            e.Transaction is { Kind: LedgerTxKind.OlympiadGrant, Reference: 6619 }
            && e.Transaction.Postings.Any(p => p.Account == bag && p.ItemId == 6619 && p.Amount > 0)
            && e.Transaction.Postings.Any(p => p.Account == olympiad && p.ItemId == 6619 && p.Amount < 0));

        heroBench.Rec.Calls.Clear();
        Assert.Null(await h.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes 6611"));
        Assert.Equal(0, h.Inventory.CountOf(6611));
        Assert.Equal(OlympiadProtocol.HeroWeaponAlready, LastSystem(heroBench).Id);
        Assert.Contains("cannot have another", LastSystem(heroBench).Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(heroBench.Ledger.Entries, e => e.Transaction.Reference == 6611);

        heroBench.Rec.Calls.Clear();
        Assert.Null(await h.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroWeapon"));
        Assert.Contains("cannot have another", LastHtml(heroBench), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OlympiadProtocol.HeroWeaponAlready, LastSystem(heroBench).Id);

        Assert.Null(await h.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroCirclet"));
        Assert.Null(await h.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroCirclet"));
        Assert.Equal(1, h.Inventory.CountOf(OlympiadProtocol.HeroCirclet));
        Assert.Contains(heroBench.Ledger.Entries, e =>
            e.Transaction is { Kind: LedgerTxKind.OlympiadGrant, Reference: OlympiadProtocol.HeroCirclet });

        // Relog keeps the claimed status.
        await heroBench.World.LeaveAsync(h.Session, CancellationToken.None);
        var back = await heroBench.EnterAsync("hero", "Hero");
        Assert.True(back.Player.Hero);
        Assert.Equal(1, back.Player.KnownSkills[395]);
    }

    /// <summary>
    /// Mobius <c>Hero.computeNewHeroes</c> strips hero items online; weapon eligibility is bag-only (no weekly flag in
    /// <c>Hero.java</c>).
    /// </summary>
    [Fact]
    public async Task Period_end_revokes_hero_gear_and_weapon_menu_follows_the_bag_not_a_weekly_flag()
    {
        var when = Evening;
        var store = new MemoryOlympiadStore();
        var board = new OlympiadBoard(Evening, new OlympiadSettings(), store);
        var bench = new WorldBench(clock: () => when, olympiad: board);
        var incumbent = await bench.EnterAsync("oldhero", "OldHero", classId: Duelist, level: 78);
        var snap = store.Load();
        snap.Cycle = 5;
        snap.Period = OlympiadProtocol.PeriodCompetition;
        snap.OlympiadEndUnixMs = when.AddHours(1).ToUnixTimeMilliseconds();
        snap.Heroes =
        [
            new OlympiadHeroRow
            {
                CharacterId = incumbent.ObjectId,
                ClassId = Duelist,
                Name = "OldHero",
                Claimed = true,
                Count = 2
            }
        ];
        snap.Nobles =
        [
            Row(incumbent.ObjectId + 1, "Successor", Dreadnought, points: 30, done: 10, won: 6)
        ];
        store.Save(snap);
        board = new OlympiadBoard(Evening, new OlympiadSettings(), store);
        bench = new WorldBench(clock: () => when, olympiad: board);
        incumbent = await bench.EnterAsync("oldhero", "OldHero", classId: Duelist, level: 78);
        Assert.True(incumbent.Player.Hero);

        var monument = bench.World.SpawnFolkForTests(31690, incumbent.Player.X + 20, incumbent.Player.Y, incumbent.Player.Z);
        bench.World.TrySetNoble(incumbent.ObjectId, true);
        Assert.Null(await incumbent.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes 6619"));
        Assert.Equal(1, incumbent.Inventory.CountOf(6619));
        Assert.Null(await incumbent.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroCirclet"));
        Assert.Equal(1, incumbent.Inventory.CountOf(OlympiadProtocol.HeroCirclet));
        var bow = incumbent.Inventory.Items.Single(i => i.ItemId == 6619);
        incumbent.Inventory.Equip(bow);
        Assert.True(bow.IsEquipped);

        when = when.AddHours(2);
        await bench.World.AdvanceOlympiadAsync(CancellationToken.None);

        Assert.False(incumbent.Player.Hero);
        Assert.False(bench.World.Olympiad.IsHero(incumbent.ObjectId));
        Assert.Equal(0, incumbent.Inventory.CountOf(6619));
        Assert.Equal(0, incumbent.Inventory.CountOf(OlympiadProtocol.HeroCirclet));
        Assert.DoesNotContain(
            incumbent.Inventory.Items,
            i => i.ItemId is >= OlympiadProtocol.FirstHeroWeapon and <= OlympiadProtocol.LastHeroWeapon);

        bench.Rec.Calls.Clear();
        Assert.Null(await incumbent.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroWeapon"));
        Assert.Contains("You must become a Hero first", LastHtml(bench), StringComparison.OrdinalIgnoreCase);

        // Same era, new unclaimed pick: weapon eligibility is whatever is in the bag (Mobius has no weekly weapon flag).
        snap = store.Load();
        snap.Heroes =
        [
            new OlympiadHeroRow
            {
                CharacterId = incumbent.ObjectId,
                ClassId = Duelist,
                Name = "OldHero",
                Claimed = false,
                Count = 3
            }
        ];
        store.Save(snap);
        board = new OlympiadBoard(when, new OlympiadSettings(), store);
        bench = new WorldBench(clock: () => when, olympiad: board);
        incumbent = await bench.EnterAsync("oldhero", "OldHero");
        monument = bench.World.SpawnFolkForTests(31690, incumbent.Player.X + 20, incumbent.Player.Y, incumbent.Player.Z);
        Assert.Null(await incumbent.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroReceive"));
        Assert.True(incumbent.Player.Hero);

        bench.Rec.Calls.Clear();
        Assert.Null(await incumbent.BypassAsync(monument.ObjectId, "Script MonumentOfHeroes HeroWeapon"));
        Assert.Contains("MonumentOfHeroes 6611", LastHtml(bench));
    }
}
