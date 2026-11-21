using Fineage.Accounts;
using Fineage.Game.Skills;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Servitors after Mobius CT 0: summon skill (crystals, one at a time), visibility with the owner tail, orders,
/// combat credit to the owner, exp multiplier, lifetime / upkeep, death, PvP through the summon.
/// </summary>
public sealed class NetSummonTests
{
    const int SummonKat = 1111;            // Kat the Cat lv1: 3 × D crystal, 1200 s, exp ×0.7
    const int SummonSiegeGolem = 13;       // 300 × C crystal, upkeep 70 × 2131 every 240 s
    const int SummonReanimated = 1129;     // corpse summon
    const int CrystalD = 1458, CrystalC = 1459, Gemstone = 2131;
    const int Gremlin = 20001;
    const int X = 46045, Y = 41251, Z = -3440;
    static readonly CancellationToken Ct = CancellationToken.None;

    static (WorldBench Bench, MutableClock Clock) Open()
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        return (new WorldBench(clock: () => clock.Now, geo: GeoEngine.Empty), clock);
    }

    static List<SystemMessageMsg> Lines(WorldBench bench) =>
        bench.Rec.Of("FanoutSystemMessage").Select(args => (SystemMessageMsg)args[1]!).ToList();

    static Task<BenchPlayer> SummonerAsync(WorldBench bench, string name, params StoredItem[] items) =>
        SummonerAsync(bench, name, 40, items);

    static async Task<BenchPlayer> SummonerAsync(WorldBench bench, string name, int level, params StoredItem[] items)
    {
        var all = new List<StoredItem> { new(57, 1000, -1) };
        all.AddRange(items);
        var p = await bench.EnterAsync(name.ToLowerInvariant(), name, [.. all], level: level, x: X, y: Y, z: Z);
        p.Player.KnownSkills[SummonKat] = 1;
        p.Player.KnownSkills[SummonReanimated] = 1;
        p.Player.CurMp = p.Player.MaxMp = 2000;
        return p;
    }

    static async Task RunAsync(WorldBench bench, MutableClock clock, int steps, int stepMs = 200)
    {
        for (int i = 0; i < steps; i++)
        {
            clock.Now = clock.Now.AddMilliseconds(stepMs);
            await bench.World.RunCombatTickAsync(Ct);
        }
    }

    [Fact]
    public async Task Summon_skill_spends_crystals_spawns_a_servitor_seen_by_others_and_only_one_at_a_time()
    {
        var (bench, _) = Open();
        var owner = await SummonerAsync(bench, "Caller", new StoredItem(CrystalD, 10, -1));
        var watcher = await bench.EnterAsync("eyes", "Eyes", x: X + 100, y: Y, z: Z, clearRecorder: false);
        bench.Rec.Calls.Clear();

        Assert.Null(await bench.World.TrySkillUseAsync(owner.Session, SummonKat, 0, Ct));
        Assert.Null(bench.World.SummonOfForTest(owner.Player)); // still casting
        bench.World.ResolvePendingHitForTest(owner.Player);

        var kat = bench.World.SummonOfForTest(owner.Player);
        Assert.NotNull(kat);
        Assert.Equal(14111, kat!.NpcId);
        Assert.Equal(7, owner.Inventory.CountOf(CrystalD));
        Assert.Equal(owner.ObjectId, kat.Summon!.OwnerObjectId);
        Assert.Equal(0.7, kat.Summon.ExpMultiplier, 3);

        var diffs = bench.Rec.Of(nameof(IWorldEventPublisher.FanoutKnownlistDiff));
        Assert.Contains(diffs, a => (int)a[0]! == watcher.ObjectId && ((IReadOnlyList<int>)a[1]!).Contains(kat.ObjectId));
        var info = (SummonInfoNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSummonInfo)).Last()[1]!;
        Assert.Equal((kat.ObjectId, SummonKindNet.Servitor, 14111), (info.ObjectId, info.Kind, info.NpcId));
        Assert.Equal(1200, info.MaxFeed);

        var spawn = NetEnterWorldProjector.BuildNpcSpawn(kat, new EntityId(1, 1));
        Assert.Equal(SummonKindNet.Servitor, spawn.SummonKind);
        Assert.Equal(owner.ObjectId, spawn.OwnerObjectId);
        Assert.Equal("Caller", spawn.Title);

        owner.Player.SkillReuseUntilMs.Clear();
        owner.Player.CastingUntilMs = 0;
        Assert.Null(await bench.World.TrySkillUseAsync(owner.Session, SummonKat, 0, Ct));
        Assert.Equal(SkillCastConditionRefusal.MultiplePetsOrServitors, Lines(bench).Last().Id);
        Assert.Equal(7, owner.Inventory.CountOf(CrystalD));
    }

    [Fact]
    public async Task No_crystals_no_summon()
    {
        var (bench, _) = Open();
        var owner = await SummonerAsync(bench, "Broke", new StoredItem(CrystalD, 2, -1));
        Assert.Null(await bench.World.TrySkillUseAsync(owner.Session, SummonKat, 0, Ct));
        Assert.Equal(ZoneWorld.SysMsgNotEnoughItems, Lines(bench).Last().Id);
        Assert.Null(bench.World.SummonOfForTest(owner.Player));
    }

    [Fact]
    public async Task Corpse_summon_needs_a_monster_corpse_and_uses_it_up()
    {
        var (bench, _) = Open();
        var owner = await SummonerAsync(bench, "Necro", new StoredItem(CrystalC, 10, -1));
        var mob = bench.World.SpawnMonsterForTests(Gremlin, X + 50, Y, Z);

        Assert.Null(await bench.World.TrySkillUseAsync(owner.Session, SummonReanimated, mob.ObjectId, Ct));
        Assert.Null(bench.World.SummonOfForTest(owner.Player));
        Assert.Empty(owner.Player.SkillReuseUntilMs); // refused before the cast

        mob.CurHp = 0;
        bench.World.SetDecayAtMs(mob, bench.Now().ToUnixTimeMilliseconds() + 60_000);
        Assert.Null(await bench.World.TrySkillUseAsync(owner.Session, SummonReanimated, mob.ObjectId, Ct));
        bench.World.ResolvePendingHitForTest(owner.Player);
        var man = bench.World.SummonOfForTest(owner.Player);
        Assert.NotNull(man);
        Assert.Equal(14001, man!.NpcId);
        Assert.Equal(8, owner.Inventory.CountOf(CrystalC));
        Assert.Equal(0, mob.DecayAtMs); // the body was used up
    }

    [Fact]
    public async Task Ordered_attack_kills_the_monster_for_the_owner_with_the_servitor_exp_penalty()
    {
        var (bench, clock) = Open();
        var owner = await SummonerAsync(bench, "Tamer", 1);
        var kat = bench.World.SpawnServitorForTest(owner.Player, SummonKat, 1)!;
        var mob = bench.World.SpawnMonsterForTests(Gremlin, X + 150, Y, Z);
        long exp0 = owner.Player.Exp;

        Assert.Null(await bench.World.TrySummonCommandAsync(owner.Session,
            new SummonCommandNetMsg { Kind = SummonCommandKind.Attack, TargetObjectId = mob.ObjectId }, Ct));
        Assert.Equal(mob.ObjectId, kat.Summon!.TargetId);
        await RunAsync(bench, clock, 300);

        Assert.True(mob.CurHp <= 0);
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));
        long gained = owner.Player.Exp - exp0;
        Assert.True(gained > 0);
        Assert.Equal(0, kat.Summon.TargetId);

        // Same kill without the servitor pays 1 / 0.7 of that.
        var solo = await bench.EnterAsync("solo", "Solo", level: 1, x: X, y: Y + 300, z: Z);
        var mob2 = bench.World.SpawnMonsterForTests(Gremlin, X, Y + 350, Z);
        long soloExp0 = solo.Player.Exp;
        mob2.DamageBy[solo.ObjectId] = mob2.MaxHp;
        bench.World.GrantKillRewardForTest(solo.Player, mob2);
        long soloGain = solo.Player.Exp - soloExp0;
        Assert.Equal(Math.Round(soloGain * 0.7), gained, 1d);
    }

    [Fact]
    public async Task Follow_stay_move_and_stop_orders()
    {
        var (bench, clock) = Open();
        var owner = await SummonerAsync(bench, "Walker");
        var kat = bench.World.SpawnServitorForTest(owner.Player, SummonKat, 1)!;

        owner.MoveTo(X + 600, Y, Z);
        await RunAsync(bench, clock, 50);
        Assert.True(ZoneCombat_Within(kat, owner.Player, ZoneWorld.SummonFollowRange));

        Assert.Null(await bench.World.TrySummonCommandAsync(owner.Session, new SummonCommandNetMsg { Kind = SummonCommandKind.Stay }, Ct));
        int sx = kat.X;
        owner.MoveTo(X, Y, Z);
        await RunAsync(bench, clock, 30);
        Assert.Equal(sx, kat.X);

        int ky = kat.Y;
        Assert.Null(await bench.World.TrySummonCommandAsync(owner.Session,
            new SummonCommandNetMsg { Kind = SummonCommandKind.Move, X = kat.X, Y = ky + 200, Z = Z }, Ct));
        await RunAsync(bench, clock, 30);
        Assert.InRange(kat.Y, ky + 190, ky + 210);

        Assert.Null(await bench.World.TrySummonCommandAsync(owner.Session, new SummonCommandNetMsg { Kind = SummonCommandKind.Follow }, Ct));
        await RunAsync(bench, clock, 40);
        Assert.True(ZoneCombat_Within(kat, owner.Player, ZoneWorld.SummonFollowRange));
        Assert.True(kat.Summon!.Following);
    }

    static bool ZoneCombat_Within(ZoneEntity a, ZoneEntity b, int range) =>
        (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y) <= (long)range * range;

    [Fact]
    public async Task Upkeep_is_spent_every_interval_and_the_servitor_leaves_when_it_runs_out()
    {
        var (bench, clock) = Open();
        var owner = await SummonerAsync(bench, "Engineer", new StoredItem(Gemstone, 100, -1));
        var golem = bench.World.SpawnServitorForTest(owner.Player, SummonSiegeGolem, 1)!;
        Assert.Equal(14737, golem.NpcId);

        for (int i = 0; i < 48; i++)
        {
            clock.Now = clock.Now.AddMilliseconds(ZoneWorld.SummonLifeTickMs);
            bench.World.TickSummonsForTest();
        }
        Assert.Equal(30, owner.Inventory.CountOf(Gemstone));
        Assert.Contains(Lines(bench), l => l.Id == ZoneWorld.SysMsgSummonedMonsterUses);

        for (int i = 0; i < 48; i++)
        {
            clock.Now = clock.Now.AddMilliseconds(ZoneWorld.SummonLifeTickMs);
            bench.World.TickSummonsForTest();
        }
        Assert.Null(bench.World.SummonOfForTest(owner.Player));
        Assert.Equal(ZoneWorld.SysMsgServitorUpkeepFailed, Lines(bench).Last().Id);
        var gone = (SummonGoneNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSummonGone)).Last()[1]!;
        Assert.Equal((golem.ObjectId, 2), (gone.ObjectId, gone.Reason));
        Assert.Equal(0, owner.Player.SummonObjectId);
    }

    [Fact]
    public async Task Lifetime_runs_out()
    {
        var (bench, clock) = Open();
        var owner = await SummonerAsync(bench, "Timer");
        var kat = bench.World.SpawnServitorForTest(owner.Player, SummonKat, 1)!;
        for (int i = 0; i <= 1200 / 5; i++)
        {
            clock.Now = clock.Now.AddMilliseconds(ZoneWorld.SummonLifeTickMs);
            bench.World.TickSummonsForTest();
        }
        Assert.Null(bench.World.SummonOfForTest(owner.Player));
        Assert.Equal(ZoneWorld.SysMsgServitorPassedAway, Lines(bench).Last().Id);
        var gone = (SummonGoneNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSummonGone)).Last()[1]!;
        Assert.Equal((kat.ObjectId, 1), (gone.ObjectId, gone.Reason));
    }

    [Fact]
    public async Task Monsters_fight_the_servitor_and_its_corpse_decays()
    {
        var (bench, clock) = Open();
        var owner = await SummonerAsync(bench, "Bait");
        var kat = bench.World.SpawnServitorForTest(owner.Player, SummonKat, 1)!;
        var mob = bench.World.SpawnMonsterForTests(20218, X + 120, Y, Z); // Premo lv45
        kat.CurHp = 1;
        Assert.Null(await bench.World.TrySummonCommandAsync(owner.Session,
            new SummonCommandNetMsg { Kind = SummonCommandKind.Attack, TargetObjectId = mob.ObjectId }, Ct));
        for (int i = 0; i < 300 && !kat.Dead; i++)
            await RunAsync(bench, clock, 1);

        Assert.True(kat.Dead);
        Assert.Contains(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutDie)), a => (int)a[1]! == kat.ObjectId);
        Assert.Equal(kat.ObjectId, owner.Player.SummonObjectId); // corpse still out
        await RunAsync(bench, clock, 40);
        Assert.Equal(0, owner.Player.SummonObjectId);
        var gone = (SummonGoneNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSummonGone)).Last()[1]!;
        Assert.Equal(3, gone.Reason);
    }

    [Fact]
    public async Task Servitor_attacking_a_white_player_needs_ctrl_and_flags_the_owner()
    {
        var (bench, clock) = Open();
        var owner = await SummonerAsync(bench, "Rogue");
        var victim = await bench.EnterAsync("victim", "Victim", level: 40, x: X + 150, y: Y, z: Z, clearRecorder: false);
        victim.Player.CurHp = victim.Player.MaxHp = 50_000;
        var kat = bench.World.SpawnServitorForTest(owner.Player, SummonKat, 1)!;

        Assert.Null(await bench.World.TrySummonCommandAsync(owner.Session,
            new SummonCommandNetMsg { Kind = SummonCommandKind.Attack, TargetObjectId = victim.ObjectId }, Ct));
        Assert.Equal(0, kat.Summon!.TargetId);
        Assert.Equal(ZoneWorld.SysMsgSummonWrongTarget, Lines(bench).Last().Id);

        Assert.Null(await bench.World.TrySummonCommandAsync(owner.Session,
            new SummonCommandNetMsg { Kind = SummonCommandKind.Attack, TargetObjectId = victim.ObjectId, Forced = true }, Ct));
        Assert.Equal(victim.ObjectId, kat.Summon.TargetId);
        await RunAsync(bench, clock, 40);
        Assert.True(owner.Player.PvpFlagged);
        Assert.False(victim.Player.PvpFlagged);

        // The victim hits back at the summon: that is a hit on the (now flagged) owner.
        Assert.Null(await bench.World.TryAttackAsync(owner.Session, kat.ObjectId, Ct)); // own summon: refused quietly
        Assert.Equal(0, owner.Player.Intent.AutoAttackTargetId);
    }

    [Fact]
    public async Task Unsummon_order_and_logout_remove_the_servitor()
    {
        var (bench, _) = Open();
        var owner = await SummonerAsync(bench, "Leaver");
        var kat = bench.World.SpawnServitorForTest(owner.Player, SummonKat, 1)!;
        Assert.Null(await bench.World.TrySummonCommandAsync(owner.Session, new SummonCommandNetMsg { Kind = SummonCommandKind.Unsummon }, Ct));
        Assert.Null(bench.World.SummonOfForTest(owner.Player));
        Assert.Contains(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSummonGone)), a => ((SummonGoneNetMsg)a[1]!).Reason == 0);

        var kat2 = bench.World.SpawnServitorForTest(owner.Player, SummonKat, 1)!;
        Assert.Equal(1, bench.World.CountNpcTemplateForTest(14111));
        await bench.World.LeaveAsync(owner.Session, Ct);
        Assert.Equal(0, bench.World.CountNpcTemplateForTest(14111));
        Assert.False(bench.World.TryGetEntityForTest(kat2.ObjectId, out _));
    }

    [Fact]
    public async Task Ordered_skill_is_cast_on_the_target_and_listed_in_the_summon_window()
    {
        var (bench, clock) = Open();
        var owner = await SummonerAsync(bench, "Mechanic", 1);
        var golem = bench.World.SpawnServitorForTest(owner.Player, 25, 1)!; // Mechanic Golem: Hammer Crush 4068
        var info = (SummonInfoNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSummonInfo)).Last()[1]!;
        Assert.Contains(4068, info.SkillIds);
        var mob = bench.World.SpawnMonsterForTests(20003, X + 200, Y, Z);

        Assert.Null(await bench.World.TrySummonCommandAsync(owner.Session,
            new SummonCommandNetMsg { Kind = SummonCommandKind.UseSkill, SkillId = 9999, TargetObjectId = mob.ObjectId }, Ct));
        Assert.Equal(0, golem.Summon!.PendingSkillId);
        Assert.Null(await bench.World.TrySummonCommandAsync(owner.Session,
            new SummonCommandNetMsg { Kind = SummonCommandKind.UseSkill, SkillId = 4068, TargetObjectId = mob.ObjectId }, Ct));
        Assert.Equal(4068, golem.Summon.PendingSkillId);
        await RunAsync(bench, clock, 60);

        Assert.Contains(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)),
            a => ((MagicSkillUseMsg)a[1]!).CasterObjectId == golem.ObjectId && ((MagicSkillUseMsg)a[1]!).SkillId == 4068);
        Assert.True(mob.CurHp < mob.MaxHp);
        Assert.True(mob.DamageBy.ContainsKey(owner.ObjectId));
    }
}
