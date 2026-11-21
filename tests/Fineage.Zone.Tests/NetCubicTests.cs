using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Cubics after Mobius CT 0: summon skill (crystals, one per kind, cap / Cubic Mastery), the cubic list on UserInfo /
/// CharInfo, offensive actions only in attack stance on a valid target, max count / use-up, debuff land, Life Cubic
/// heal, mass summon on the party, lifetime, death and logout.
/// </summary>
public sealed class NetCubicTests
{
    const int StormSkill = 10, LifeSkill = 67, ViperSkill = 278, BindingSkill = 1279, MassStormSkill = 1328, Mastery = 143;
    const int StormCubicAction = 4049, ViperCubicAction = 4052, BindingCubicAction = 4164;
    const int CrystalD = 1458;
    const int Gremlin = 20001;
    const int X = 46045, Y = 41251, Z = -3440;
    static readonly CancellationToken Ct = CancellationToken.None;

    sealed class AlwaysRandom : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0;
    }

    static (WorldBench Bench, MutableClock Clock) Open(bool alwaysLand = true)
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        return (new WorldBench(clock: () => clock.Now, geo: GeoEngine.Empty, combatRandom: alwaysLand ? new AlwaysRandom() : null), clock);
    }

    static async Task<BenchPlayer> WarlockAsync(WorldBench bench, string name, int x = X, int y = Y, params StoredItem[] items)
    {
        var all = new List<StoredItem> { new(57, 1000, -1) };
        all.AddRange(items);
        var p = await bench.EnterAsync(name.ToLowerInvariant(), name, [.. all], level: 60, x: x, y: y, z: Z, clearRecorder: false);
        p.Player.CurMp = p.Player.MaxMp = 5000;
        p.Player.CurHp = p.Player.MaxHp = 5000;
        return p;
    }

    static long Now(MutableClock clock) => clock.Now.ToUnixTimeMilliseconds();

    /// <summary>Owner fights: target selected, monster hating it, attack stance on; time moves by <paramref name="ms"/>.</summary>
    static void Fight(WorldBench bench, MutableClock clock, BenchPlayer owner, ZoneEntity mob, int ms)
    {
        clock.Now = clock.Now.AddMilliseconds(ms);
        owner.Session.SelectedTargetId = mob.ObjectId;
        mob.HateList[owner.ObjectId] = 100;
        owner.Player.CombatUntilUnixMs = Now(clock) + 15_000;
        bench.World.TickCubicsForTest();
    }

    static ZoneEntity Dummy(WorldBench bench, int dx = 150)
    {
        var mob = bench.World.SpawnMonsterForTests(Gremlin, X + dx, Y, Z);
        mob.CurHp = mob.MaxHp = 10_000_000;
        return mob;
    }

    [Fact]
    public async Task Summon_skill_spends_crystals_and_the_cubic_list_reaches_user_info_and_char_info()
    {
        var (bench, _) = Open();
        var owner = await WarlockAsync(bench, "Caller", items: new StoredItem(CrystalD, 20, -1));
        var watcher = await bench.EnterAsync("eyes", "Eyes", x: X + 100, y: Y, z: Z, clearRecorder: false);
        owner.Player.KnownSkills[StormSkill] = 1;
        owner.Player.KnownSkills[LifeSkill] = 1;
        bench.Rec.Calls.Clear();

        Assert.Null(await bench.World.TrySkillUseAsync(owner.Session, StormSkill, 0, Ct));
        Assert.False(owner.Player.HasCubics); // 6 s cast
        bench.World.ResolvePendingHitForTest(owner.Player);

        Assert.Equal(new[] { 1 }, owner.Player.CubicIds());
        Assert.Equal(15, owner.Inventory.CountOf(CrystalD));
        var info = (UserInfoMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutUserInfo)).Last()[1]!;
        Assert.Equal(new[] { 1 }, info.Cubics);
        Assert.Contains(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutCharInfo)),
            a => ((IReadOnlyList<int>)a[0]!).Contains(watcher.ObjectId) && (int)a[1]! == owner.ObjectId);
        Assert.Equal(new[] { 1 }, NetEnterWorldProjector.BuildRemoteSpawn(owner.Player, default).Cubics);

        // Without Cubic Mastery only one cubic: a second kind pushes the first out.
        Assert.True(bench.World.SummonCubicForTest(owner.Player, LifeSkill, 1));
        Assert.Equal(new[] { 3 }, owner.Player.CubicIds());

        // Cubic Mastery 1: two at once; the same kind again replaces itself.
        owner.Player.KnownSkills[Mastery] = 1;
        Assert.True(bench.World.SummonCubicForTest(owner.Player, StormSkill, 1));
        Assert.True(bench.World.SummonCubicForTest(owner.Player, StormSkill, 2));
        Assert.Equal(new[] { 3, 1 }, owner.Player.CubicIds());
        Assert.True(bench.World.SummonCubicForTest(owner.Player, ViperSkill, 1));
        Assert.Equal(new[] { 1, 4 }, owner.Player.CubicIds());
    }

    [Fact]
    public async Task Storm_cubic_hits_a_monster_hating_its_owner_only_while_the_owner_fights()
    {
        var (bench, clock) = Open();
        var owner = await WarlockAsync(bench, "Storm");
        var watcher = await bench.EnterAsync("eyes", "Eyes", x: X + 100, y: Y, z: Z, clearRecorder: false);
        var mob = Dummy(bench);
        Assert.True(bench.World.SummonCubicForTest(owner.Player, StormSkill, 1));
        bench.Rec.Calls.Clear();

        // Selected but no fight and no hate: nothing.
        owner.Session.SelectedTargetId = mob.ObjectId;
        clock.Now = clock.Now.AddSeconds(11);
        bench.World.TickCubicsForTest();
        Assert.Equal(mob.MaxHp, mob.CurHp);

        Fight(bench, clock, owner, mob, 0);
        int expected = (int)(91d * (282 + 436) / mob.MDef);
        Assert.Equal(mob.MaxHp - expected, mob.CurHp);
        Assert.True(mob.DamageBy.ContainsKey(owner.ObjectId));
        Assert.Contains(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)),
            a => ((IReadOnlyList<int>)a[0]!).Contains(watcher.ObjectId) && ((MagicSkillUseMsg)a[1]!).CasterObjectId == owner.ObjectId
                 && ((MagicSkillUseMsg)a[1]!).SkillId == 4049 && ((MagicSkillUseMsg)a[1]!).TargetObjectId == mob.ObjectId);

        // Next action only after the 10 s delay.
        Fight(bench, clock, owner, mob, 5_000);
        Assert.Equal(mob.MaxHp - expected, mob.CurHp);
        Fight(bench, clock, owner, mob, 5_000);
        Assert.Equal(mob.MaxHp - 2 * expected, mob.CurHp);

        // Out of range (> 900): no hit.
        mob.X = X + 2000;
        Fight(bench, clock, owner, mob, 10_000);
        Assert.Equal(mob.MaxHp - 2 * expected, mob.CurHp);
    }

    [Fact]
    public async Task After_max_count_a_use_up_cubic_leaves_and_viper_just_stops_until_the_next_fight()
    {
        var (bench, clock) = Open();
        var owner = await WarlockAsync(bench, "Counter");
        var mob = Dummy(bench);
        owner.Player.KnownSkills[Mastery] = 1;
        Assert.True(bench.World.SummonCubicForTest(owner.Player, StormSkill, 1));  // max 30, useUp
        Assert.True(bench.World.SummonCubicForTest(owner.Player, ViperSkill, 1));  // max 15, no useUp, 20 s

        for (int i = 0; i < 31; i++)
            Fight(bench, clock, owner, mob, 10_000);
        Assert.Equal(new[] { 4 }, owner.Player.CubicIds());
        var viper = owner.Player.Cubics.Single();
        Assert.Equal(0, viper.Count);
        Assert.False(viper.Running);

        // The fight ends, a new one starts: Viper acts again.
        clock.Now = clock.Now.AddSeconds(30);
        bench.World.TickCubicsForTest();
        Fight(bench, clock, owner, mob, 0);
        Assert.True(viper.Running);
        Assert.Equal(1, viper.Count);
        Assert.Contains(mob.Abnormals, a => a.Id == 4052);
    }

    [Fact]
    public void Offensive_cubic_debuffs_include_owner_m_atk_in_land_rolls()
    {
        // Action skill ids from data/cubics.json: Binding cubic 6 → 4164, Viper cubic 4 → 4052, Storm cubic 1 → 4049.
        const int BindingAction = 4164, ViperAction = 4052, StormAction = 4049;
        var (bench, _) = Open();
        Assert.True(bench.World.CubicActionUsesMagicAtkForTests(BindingCubicAction));
        Assert.True(bench.World.CubicActionUsesMagicAtkForTests(ViperCubicAction));
        Assert.True(bench.World.CubicActionUsesMagicAtkForTests(StormCubicAction));
    }

    [Fact]
    public async Task Binding_cubic_paralyzes_the_target()
    {
        var (bench, clock) = Open();
        var owner = await WarlockAsync(bench, "Binder");
        var mob = Dummy(bench);
        Assert.True(bench.World.SummonCubicForTest(owner.Player, BindingSkill, 1));
        Fight(bench, clock, owner, mob, 0);
        var ab = Assert.Single(mob.Abnormals, a => a.Id == 4164);
        Assert.Equal("PARALYZE", ab.Slot);
        Assert.True(ZoneWorld.IsActionBlocked(mob));
    }

    [Fact]
    public async Task Life_cubic_heals_the_most_hurt_party_member_in_range_at_once_and_every_delay()
    {
        var (bench, clock) = Open();
        var owner = await WarlockAsync(bench, "Healer");
        var friend = await WarlockAsync(bench, "Friend", x: X + 300);
        var far = await WarlockAsync(bench, "Far", x: X + 3000);
        bench.World.JoinPartyForTest(owner.Player, friend.Player, far.Player);
        owner.Player.CurHp = 4000;  // 80 %
        friend.Player.CurHp = 3000; // 60 %
        far.Player.CurHp = 100;     // out of range

        Assert.True(bench.World.SummonCubicForTest(owner.Player, LifeSkill, 1)); // heal 201
        bench.World.TickCubicsForTest();
        Assert.Equal(3201, friend.Player.CurHp);
        Assert.Equal(4000, owner.Player.CurHp);
        Assert.Equal(100, far.Player.CurHp);

        clock.Now = clock.Now.AddSeconds(13);
        bench.World.TickCubicsForTest();
        Assert.Equal(3402, friend.Player.CurHp);

        // Missing HP not above the heal: no cast.
        friend.Player.CurHp = 4900;
        owner.Player.CurHp = 4850;
        clock.Now = clock.Now.AddSeconds(13);
        bench.World.TickCubicsForTest();
        Assert.Equal(4900, friend.Player.CurHp);
        Assert.Equal(4850, owner.Player.CurHp);
    }

    [Fact]
    public async Task Mass_summon_gives_the_cubic_to_party_members_near_the_caster()
    {
        var (bench, _) = Open();
        var owner = await WarlockAsync(bench, "Mass", items: new StoredItem(CrystalD, 50, -1));
        var friend = await WarlockAsync(bench, "Near", x: X + 300);
        var far = await WarlockAsync(bench, "Away", x: X + 5000);
        bench.World.JoinPartyForTest(owner.Player, friend.Player, far.Player);
        owner.Player.KnownSkills[MassStormSkill] = 1;

        Assert.Null(await bench.World.TrySkillUseAsync(owner.Session, MassStormSkill, 0, Ct));
        bench.World.ResolvePendingHitForTest(owner.Player);
        Assert.Equal(30, owner.Inventory.CountOf(CrystalD));
        Assert.Equal(new[] { 1 }, owner.Player.CubicIds());
        Assert.Equal(new[] { 1 }, friend.Player.CubicIds());
        Assert.True(friend.Player.Cubics[0].GivenByOther);
        Assert.False(far.Player.HasCubics);
    }

    [Fact]
    public async Task Cubic_ignores_a_white_player_and_hits_a_flagged_enemy_while_the_owner_is_flagged()
    {
        var (bench, clock) = Open();
        var owner = await WarlockAsync(bench, "Pvper");
        var enemy = await WarlockAsync(bench, "Enemy", x: X + 200);
        Assert.True(bench.World.SummonCubicForTest(owner.Player, StormSkill, 1));

        owner.Session.SelectedTargetId = enemy.ObjectId;
        owner.Player.CombatUntilUnixMs = Now(clock) + 15_000;
        bench.World.TickCubicsForTest();
        Assert.Equal(5000, enemy.Player.CurHp);

        owner.Player.PvpFlagged = true;
        enemy.Player.PvpFlagged = true;
        clock.Now = clock.Now.AddSeconds(10);
        owner.Player.CombatUntilUnixMs = Now(clock) + 15_000;
        bench.World.TickCubicsForTest();
        Assert.True(enemy.Player.CurHp < 5000);
    }

    [Fact]
    public async Task Cubics_leave_at_the_end_of_their_lifetime_on_death_and_on_logout()
    {
        var (bench, clock) = Open();
        var owner = await WarlockAsync(bench, "Mortal");
        owner.Player.KnownSkills[Mastery] = 2;
        Assert.True(bench.World.SummonCubicForTest(owner.Player, StormSkill, 1));
        clock.Now = clock.Now.AddSeconds(899);
        bench.World.TickCubicsForTest();
        Assert.True(owner.Player.HasCubics);
        clock.Now = clock.Now.AddSeconds(2);
        bench.World.TickCubicsForTest();
        Assert.False(owner.Player.HasCubics);

        Assert.True(bench.World.SummonCubicForTest(owner.Player, StormSkill, 1));
        Assert.True(bench.World.SummonCubicForTest(owner.Player, LifeSkill, 1));
        var mob = Dummy(bench);
        bench.World.KillPlayerByNpcForTest(owner.Player, mob);
        Assert.False(owner.Player.HasCubics);

        var other = await WarlockAsync(bench, "Leaver");
        Assert.True(bench.World.SummonCubicForTest(other.Player, StormSkill, 1));
        await bench.World.LeaveAsync(other.Session, Ct);
        Assert.False(other.Player.HasCubics);
    }
}
