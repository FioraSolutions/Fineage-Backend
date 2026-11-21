using Fineage.Accounts;
using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Fishing;
using Fineage.Zone.Geo;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Fishing after Mobius CT_0: learn the guild skills, cast a lure onto fishing water, wait for the bite, pump and reel
/// the fish's HP to zero for the fish item (or a fishing monster), with watchers seeing the line, the fight and the end.
/// </summary>
public sealed class NetFishingTests
{
    const int Rod = 6529;           // Baby Duck Rod: no grade, level 20, damage 20
    const int GreenLure = 6519;     // day lure, normal grade
    const int NightGreenLure = 8505;
    const int GreenFish = 6411;     // Small Nimble Green Fish (level 1, swift group, 100 HP)
    const int ShotNoGrade = 6535, ShotD = 6536;
    const int Fisherman = 31562;
    const int X = 46045, Y = 41251, Z = -3440;
    static readonly CancellationToken Ct = CancellationToken.None;

    /// <summary>Scripted rolls: queued values first, then the fallback.</summary>
    sealed class ScriptRandom(int fallback, params int[] rolls) : Random
    {
        readonly Queue<int> _rolls = new(rolls);
        public Queue<double> Doubles { get; } = new();
        public void Push(params int[] values) { foreach (int v in values) _rolls.Enqueue(v); }
        int Take() => _rolls.Count > 0 ? _rolls.Dequeue() : fallback;
        public override int Next(int maxValue) => Math.Clamp(Take(), 0, Math.Max(0, maxValue - 1));
        public override int Next(int minValue, int maxValue) => Math.Clamp(Take(), minValue, maxValue - 1);
        public override int Next() => Take();
        public override double NextDouble() => Doubles.Count > 0 ? Doubles.Dequeue() : 0.5;
    }

    sealed class Rig
    {
        public required WorldBench Bench { get; init; }
        public required MutableClock Clock { get; init; }
        public required ScriptRandom Rnd { get; init; }
        public ZoneWorld World => Bench.World;
        public void Advance(int ms) { Clock.Now = Clock.Now.AddMilliseconds(ms); World.TickFishingForTests(); }
    }

    static Rig Open(int fallback = 0, bool day = true)
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        var rnd = new ScriptRandom(fallback);
        var bench = new WorldBench(clock: () => clock.Now, geo: GeoEngine.Empty, fishingRandom: rnd);
        // The water starts 50 units east of the fisher; its surface is just below the shore.
        bench.World.Water = new WaterZones([(X + 50, X + 2000, Y - 2000, Y + 2000, Z - 500, Z - 20)]);
        var d = FishingData.Default;
        var zone = new FishingZone("test", Z - 500, Z, [(X - 100, Y - 1000), (X + 1000, Y - 1000), (X + 1000, Y + 1000), (X - 100, Y + 1000)],
            X - 100, X + 1000, Y - 1000, Y + 1000);
        bench.World.FishingTable = new FishingData(d.Fish, d.Rods, d.Monsters, d.SkillTree, [zone], d.SkillPower);
        while (bench.World.IsNight == day)
            clock.Now = clock.Now.AddMinutes(10);
        return new Rig { Bench = bench, Clock = clock, Rnd = rnd };
    }

    static async Task<BenchPlayer> FisherAsync(Rig rig, int lure = GreenLure, int level = 10, bool learn = true, string name = "Angler",
        params StoredItem[] extra)
    {
        var items = new List<StoredItem> { new(57, 100_000, -1), new(Rod, 1, 7), new(lure, 5, -1) };
        items.AddRange(extra);
        var p = await rig.Bench.EnterAsync(name.ToLowerInvariant(), name, [.. items], level: level, x: X, y: Y, z: Z, clearRecorder: false);
        p.Player.Heading = 0; // facing +X, toward the water
        Assert.Null(await p.Use(p.Slot(lure)));
        Assert.Equal(lure, p.Inventory.GetEquipped(PaperdollSlot.LHand)?.ItemId);
        Assert.Equal(Rod, p.Inventory.GetEquipped(PaperdollSlot.RHand)?.ItemId);
        if (learn)
        {
            foreach (int skill in new[] { FishingProtocol.FishingSkillId, FishingProtocol.PumpingSkillId, FishingProtocol.ReelingSkillId, FishingProtocol.ExpertiseSkillId })
                Assert.Null(await rig.World.TryAcquireSkillAsync(p.Session, skill, 1, FishingProtocol.AcquireSkillTypeFishing, false, Ct));
        }
        rig.Bench.Rec.Calls.Clear();
        return p;
    }

    static Task<string?> Cast(Rig rig, BenchPlayer p, int skillId) => rig.World.TrySkillUseAsync(p.Session, skillId, p.ObjectId, Ct);

    /// <summary>Reuse is 2 s for pumping and reeling; each test pull steps past it.</summary>
    static async Task Pull(Rig rig, BenchPlayer p, int skillId)
    {
        Assert.Null(await Cast(rig, p, skillId));
        rig.Clock.Now = rig.Clock.Now.AddMilliseconds(2000);
    }

    static List<int> Lines(WorldBench bench, int objectId) =>
        bench.Rec.Of("FanoutSystemMessage")
            .Where(a => ((IReadOnlyList<int>)a[0]!).Contains(objectId))
            .Select(a => ((SystemMessageMsg)a[1]!).Id).ToList();

    static List<(IReadOnlyList<int> To, FishingEventNetMsg Msg)> Events(WorldBench bench) =>
        bench.Rec.Of(nameof(IWorldEventPublisher.FanoutFishing))
            .Select(a => ((IReadOnlyList<int>)a[0]!, (FishingEventNetMsg)a[1]!)).ToList();

    /// <summary>Bait 90 in front, level roll 0 (expertise level), group roll 0 (swift), fish pick 0.</summary>
    static void ScriptCast(Rig rig) => rig.Rnd.Push(90, 0, 0, 0);

    [Fact]
    public async Task Cast_bite_pump_and_catch_the_fish_while_a_watcher_sees_the_line()
    {
        var rig = Open();
        var p = await FisherAsync(rig);
        var watcher = await rig.Bench.EnterAsync("watch", "Watcher", x: X - 30, y: Y, z: Z, clearRecorder: false);
        rig.Bench.Rec.Calls.Clear();

        ScriptCast(rig);
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        Assert.True(p.Player.Fishing);
        Assert.Equal(4, p.Inventory.FindByItemId(GreenLure)!.Count);
        var line = rig.World.FishingLineForTests(p.ObjectId)!.Value;
        Assert.Equal((1, 1, X + 90, Y, Z - 20), line);
        var start = Events(rig.Bench).Single();
        Assert.Equal(FishingProtocol.EventStart, start.Msg.Kind);
        Assert.Contains(watcher.ObjectId, start.To);
        Assert.Equal((X + 90, Z - 20, false), (start.Msg.X, start.Msg.Z, start.Msg.NightLure));
        Assert.Contains(FishingProtocol.CastLine, Lines(rig.Bench, p.ObjectId));

        // Immobilized while the line is out; other items cannot be used.
        Assert.Equal(ActionProtocol.Immobilized, await rig.World.TryMoveAsync(p.Session, X - 200, Y, Z, Ct));
        Assert.Equal(FishingProtocol.Busy, await p.Use(p.Slot(57)));
        Assert.Contains(FishingProtocol.CannotWhileFishingUse, Lines(rig.Bench, p.ObjectId));
        Assert.Equal(FishingProtocol.Busy, await rig.World.TryDropItemAsync(p.Session, p.Slot(57), 1, 0, 0, 0, Ct));

        // First bite check 10 s after the cast: guts 50 beats roll 0; the fish rests (mode roll 0).
        rig.Rnd.Push(0, 0);
        rig.Advance(9_999);
        Assert.Null(rig.World.FishFightForTests(p.ObjectId));
        rig.Advance(1);
        Assert.Equal((100, 100, 0, 0, 24), rig.World.FishFightForTests(p.ObjectId));
        var combat = Events(rig.Bench).Last().Msg;
        Assert.Equal((FishingProtocol.EventCombat, 100, 1), (combat.Kind, combat.Hp, combat.LureType));
        Assert.Contains(FishingProtocol.CombatBite, Lines(rig.Bench, p.ObjectId));

        // (20 rod + 0.66 expertise + 0.66 power) · 2.0 grade bonus = 42 per good pump.
        rig.Rnd.Push(0, 0, 0, 99); // three pulls not resisted, then the monster roll misses
        await Pull(rig, p, FishingProtocol.PumpingSkillId);
        Assert.Equal(58, rig.World.FishFightForTests(p.ObjectId)!.Value.Hp);
        var pump = Events(rig.Bench).Last();
        Assert.Equal((FishingProtocol.EventHp, 58, 1, 1), (pump.Msg.Kind, pump.Msg.Hp, pump.Msg.GoodUse, pump.Msg.Anim));
        Assert.Contains(watcher.ObjectId, pump.To);
        await Pull(rig, p, FishingProtocol.PumpingSkillId);
        await Pull(rig, p, FishingProtocol.PumpingSkillId);

        Assert.False(p.Player.Fishing);
        Assert.Equal(1, p.Inventory.CountOf(GreenFish));
        var end = Events(rig.Bench).Last();
        Assert.Equal((FishingProtocol.EventEnd, true), (end.Msg.Kind, end.Msg.Win));
        Assert.Contains(watcher.ObjectId, end.To);
        var lines = Lines(rig.Bench, p.ObjectId);
        Assert.Equal(3, lines.Count(id => id == FishingProtocol.PumpSuccess));
        Assert.Contains(FishingProtocol.CaughtSomething, lines);
        Assert.Contains(FishingProtocol.StopFishing, lines);
        Assert.DoesNotContain(FishingProtocol.BaitLost, lines);
        Assert.Null(await rig.World.TryMoveAsync(p.Session, X - 200, Y, Z, Ct));
    }

    [Fact]
    public async Task Reeling_needs_a_fighting_fish_and_deception_reverses_the_pull()
    {
        var rig = Open();
        var p = await FisherAsync(rig);
        ScriptCast(rig);
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        rig.Rnd.Push(0, 0);
        rig.Advance(10_000);

        // Resting fish (mode 0): a reel fails and the fish heals by the damage.
        await Pull(rig, p, FishingProtocol.ReelingSkillId);
        Assert.Equal(142, rig.World.FishFightForTests(p.ObjectId)!.Value.Hp);
        Assert.Contains(FishingProtocol.ReelFailed, Lines(rig.Bench, p.ObjectId));
        Assert.Equal(2, Events(rig.Bench).Last().Msg.GoodUse);

        // Deceptive resting fish: the reel works.
        rig.World.SetFishModeForTests(p.ObjectId, mode: 0, deceptive: 1);
        await Pull(rig, p, FishingProtocol.ReelingSkillId);
        Assert.Equal(100, rig.World.FishFightForTests(p.ObjectId)!.Value.Hp);
        Assert.Contains(FishingProtocol.ReelSuccess, Lines(rig.Bench, p.ObjectId));

        // A resisting fish (roll above 90) takes nothing.
        rig.Rnd.Push(95);
        await Pull(rig, p, FishingProtocol.ReelingSkillId);
        Assert.Equal(100, rig.World.FishFightForTests(p.ObjectId)!.Value.Hp);
        Assert.Contains(FishingProtocol.FishResisted, Lines(rig.Bench, p.ObjectId));

        // Pulling the fish to twice its HP loses it.
        rig.World.SetFishModeForTests(p.ObjectId, mode: 1, deceptive: 0);
        await Pull(rig, p, FishingProtocol.PumpingSkillId);
        await Pull(rig, p, FishingProtocol.PumpingSkillId);
        await Pull(rig, p, FishingProtocol.PumpingSkillId);
        Assert.False(p.Player.Fishing);
        Assert.False(Events(rig.Bench).Last().Msg.Win);
        Assert.Equal(0, p.Inventory.CountOf(GreenFish));
    }

    [Fact]
    public async Task The_fight_times_out_and_the_line_comes_in()
    {
        var rig = Open();
        var p = await FisherAsync(rig);
        ScriptCast(rig);
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        rig.Rnd.Push(0, 0);
        rig.Advance(10_000);
        rig.Advance(1000);
        Assert.Equal(23, rig.World.FishFightForTests(p.ObjectId)!.Value.Time);
        var tick = Events(rig.Bench).Last();
        Assert.Equal(new[] { p.ObjectId }, tick.To); // a plain HP tick only reaches the fisher
        rig.Advance(30_000);
        Assert.False(p.Player.Fishing);
        var lines = Lines(rig.Bench, p.ObjectId);
        Assert.Contains(FishingProtocol.CombatTimeUp, lines);
        Assert.DoesNotContain(FishingProtocol.BaitLost, lines);
    }

    [Fact]
    public async Task A_night_lure_by_day_never_bites_and_the_bait_is_lost()
    {
        var rig = Open(day: true);
        var p = await FisherAsync(rig, lure: NightGreenLure);
        ScriptCast(rig);
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        var start = Events(rig.Bench).Single().Msg;
        Assert.Equal((-1, true), (start.FishGroup, start.NightLure));
        rig.Advance(29_000);
        Assert.True(p.Player.Fishing);
        rig.Advance(2_000); // start combat time 20 s + 10 s
        Assert.False(p.Player.Fishing);
        Assert.Contains(FishingProtocol.BaitLost, Lines(rig.Bench, p.ObjectId));
        Assert.False(Events(rig.Bench).Last().Msg.Win);
    }

    [Fact]
    public async Task Refusals_cancel_and_out_of_fight_pulls()
    {
        var rig = Open();
        var p = await FisherAsync(rig);

        Assert.Null(await Cast(rig, p, FishingProtocol.PumpingSkillId));
        Assert.Contains(FishingProtocol.PumpOnlyFishing, Lines(rig.Bench, p.ObjectId));
        Assert.Null(await Cast(rig, p, FishingProtocol.ReelingSkillId));
        Assert.Contains(FishingProtocol.ReelOnlyFishing, Lines(rig.Bench, p.ObjectId));

        // Facing away from the water: no fishing spot at any distance.
        p.Player.Heading = 32768;
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        Assert.Contains(FishingProtocol.CantFishHere, Lines(rig.Bench, p.ObjectId));
        Assert.Equal(5, p.Inventory.FindByItemId(GreenLure)!.Count);
        p.Player.Heading = 0;

        // Standing in the water.
        p.MoveTo(X + 100, Y, Z - 100);
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        Assert.Contains(FishingProtocol.UnderWater, Lines(rig.Bench, p.ObjectId));
        p.MoveTo(X, Y, Z);

        // Casting again while the line is out reels it in.
        ScriptCast(rig);
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        Assert.True(p.Player.Fishing);
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        Assert.False(p.Player.Fishing);
        Assert.Contains(FishingProtocol.Cancelled, Lines(rig.Bench, p.ObjectId));

        // No lure, then no rod.
        Assert.Null(await p.Unequip((int)PaperdollSlot.LHand));
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        Assert.Contains(FishingProtocol.NoLure, Lines(rig.Bench, p.ObjectId));
        Assert.Null(await p.Unequip((int)PaperdollSlot.RHand));
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        Assert.Contains(FishingProtocol.NoRod, Lines(rig.Bench, p.ObjectId));
    }

    [Fact]
    public async Task Fishing_shot_doubles_one_pull_and_must_match_the_rod_grade()
    {
        var rig = Open();
        var p = await FisherAsync(rig, extra: [new StoredItem(ShotNoGrade, 10, -1), new StoredItem(ShotD, 10, -1)]);

        Assert.Null(await p.Use(p.Slot(ShotD)));
        Assert.Contains(FishingProtocol.ShotGradeMismatch, Lines(rig.Bench, p.ObjectId));
        Assert.False(p.Player.FishShotCharged);
        Assert.Null(await p.Use(p.Slot(ShotNoGrade)));
        Assert.True(p.Player.FishShotCharged);
        Assert.Equal(9, p.Inventory.CountOf(ShotNoGrade));
        Assert.Null(await p.Use(p.Slot(ShotNoGrade))); // already charged: nothing spent
        Assert.Equal(9, p.Inventory.CountOf(ShotNoGrade));
        Assert.Contains(rig.Bench.Rec.Of("FanoutMagicSkillUse"), a => ((MagicSkillUseMsg)a[1]!).SkillId == 2181);

        ScriptCast(rig);
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        rig.Rnd.Push(0, 0);
        rig.Advance(10_000);
        await Pull(rig, p, FishingProtocol.PumpingSkillId);
        Assert.Equal(100 - 85, rig.World.FishFightForTests(p.ObjectId)!.Value.Hp);
        Assert.False(p.Player.FishShotCharged);
    }

    [Fact]
    public async Task A_won_fight_can_hook_a_fishing_monster()
    {
        var rig = Open();
        var p = await FisherAsync(rig);
        ScriptCast(rig);
        Assert.Null(await Cast(rig, p, FishingProtocol.FishingSkillId));
        rig.Rnd.Push(0, 0);
        rig.Advance(10_000);
        rig.Rnd.Push(0, 0, 0, 5); // monster roll 5 ≤ 5% for the 6–15 band
        for (int i = 0; i < 3; i++)
            await Pull(rig, p, FishingProtocol.PumpingSkillId);
        Assert.Contains(FishingProtocol.CaughtMonster, Lines(rig.Bench, p.ObjectId));
        Assert.Equal(0, p.Inventory.CountOf(GreenFish));
        Assert.NotNull(rig.World.FindNpcTemplateForTest(18319));
    }

    [Fact]
    public async Task Fisherman_teaches_guild_skills_for_adena()
    {
        var rig = Open();
        var p = await FisherAsync(rig, learn: false, level: 5);
        Assert.Null(rig.World.NpcServiceForTest(p.Session, Fisherman, NpcServiceIntent.FishSkillList));
        var list = (AcquireSkillListMsg)rig.Bench.Rec.Of("FanoutAcquireSkillList").Last()[1]!;
        Assert.Equal(FishingProtocol.AcquireSkillTypeFishing, list.SkillType);
        Assert.Contains(list.Skills, s => s.SkillId == FishingProtocol.FishingSkillId && s.NextLevel == 1);
        Assert.Contains(list.Skills, s => s.SkillId == FishingProtocol.ExpertiseSkillId && s.NextLevel == 1);

        long adena = p.Inventory.CountOf(57);
        Assert.Null(await rig.World.TryAcquireSkillAsync(p.Session, FishingProtocol.FishingSkillId, 1, FishingProtocol.AcquireSkillTypeFishing, false, Ct));
        Assert.Equal(adena - 1000, p.Inventory.CountOf(57));
        Assert.Equal(1, p.Player.KnownSkills[FishingProtocol.FishingSkillId]);
        Assert.Contains(p.SavedRecord().LearnedSkills!, s => s.SkillId == FishingProtocol.FishingSkillId && s.Level == 1);

        // Level 2 of Pumping needs level 4 and level 1 first; level 1 is on offer, level 2 is not.
        Assert.Equal("bad_request", await rig.World.TryAcquireSkillAsync(p.Session, FishingProtocol.PumpingSkillId, 2,
            FishingProtocol.AcquireSkillTypeFishing, false, Ct));

        // Not enough adena.
        Assert.True(p.Inventory.TryTake(57, p.Inventory.CountOf(57), new List<Fineage.Game.Quests.BagTouch>()));
        Assert.Null(await rig.World.TryAcquireSkillAsync(p.Session, FishingProtocol.PumpingSkillId, 1, FishingProtocol.AcquireSkillTypeFishing, false, Ct));
        Assert.Contains(276, Lines(rig.Bench, p.ObjectId));
        Assert.False(p.Player.KnownSkills.ContainsKey(FishingProtocol.PumpingSkillId));
    }

    static Fineage.Zone.NpcHtml.BypassCall Call(string command) => new()
    {
        NpcObjectId = 1,
        NpcId = Fisherman,
        NpcName = "Fisher",
        NpcType = "Fisherman",
        PlayerName = "A",
        Command = command
    };

    [Fact]
    public async Task Fisherman_bypasses_route_to_the_skill_window_and_the_championship_page()
    {
        var rig = Open();
        var registry = rig.World.Bypasses;
        Assert.NotNull(registry.Find("FishSkillList"));
        Assert.NotNull(registry.Find("FishingChampionship"));
        var reply = registry.Find("FishSkillList")!.Handle(Call("FishSkillList"));
        Assert.Equal(NpcServiceIntent.FishSkillList, reply.Service);
        var page = registry.Find("FishingChampionship")!.Handle(Call("FishingChampionship"));
        Assert.Contains("Championship", page.Html);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Opening_a_fish_rolls_one_group_or_nothing()
    {
        var rig = Open();
        var p = await FisherAsync(rig, extra: new StoredItem(GreenFish, 2, -1));
        // Green fish: 27.6% → 6 × 6908, then 16.56% → 1 × 6913, the rest is empty.
        rig.Rnd.Doubles.Enqueue(0.10);
        Assert.Null(await p.Use(p.Slot(GreenFish)));
        Assert.Equal(6, p.Inventory.CountOf(6908));
        Assert.Contains(ManorProtocol.ObtainedMany, Lines(rig.Bench, p.ObjectId));
        rig.Rnd.Doubles.Enqueue(0.90);
        Assert.Null(await p.Use(p.Slot(GreenFish)));
        Assert.Contains(FishingProtocol.NothingInside, Lines(rig.Bench, p.ObjectId));
        Assert.Equal(0, p.Inventory.CountOf(GreenFish));
    }

    [Fact]
    public void Fishing_event_codec_round_trips()
    {
        var m = new FishingEventNetMsg
        {
            Kind = 2,
            FisherObjectId = 77,
            FishGroup = -1,
            X = 1,
            Y = -2,
            Z = 3,
            NightLure = true,
            Time = 19,
            Hp = 150,
            Mode = 1,
            LureType = 2,
            Deceptive = 1,
            GoodUse = 2,
            Anim = 1,
            Penalty = 4,
            Win = true
        };
        var back = NetCodec.DecodeFishingEvent(NetCodec.EncodeFishingEvent(m));
        Assert.Equivalent(m, back);
        Assert.Equal(0x03BC, NetOpcode.FishingEvent);
    }

    [Fact]
    public void Datapack_tables_and_rules_match_mobius()
    {
        var d = FishingData.Default;
        Assert.Equal(278, d.Fish.Count);
        Assert.Equal(6, d.Rods.Count());
        Assert.Equal(8, d.Monsters.Count);
        Assert.Equal(43, d.Zones.Count);
        Assert.Equal(27, d.SkillPower.Count);
        Assert.Equal((20, 20.0), (d.Rod(Rod)!.Level, d.Rod(Rod)!.Damage));
        Assert.Equal(18319, d.MonsterFor(6)!.NpcId);
        Assert.Null(d.MonsterFor(5));
        var z0 = d.Zones[0];
        Assert.True(d.InFishingZone((z0.MinX + z0.MaxX) / 2, (z0.MinY + z0.MaxY) / 2) || z0.Nodes.Length >= 3);
        Assert.False(d.InFishingZone(0, 0));

        Assert.Equal(0, FishingRules.Grade(7807));
        Assert.Equal(2, FishingRules.Grade(8506));
        Assert.Equal(1, FishingRules.Grade(GreenLure));
        Assert.Equal(1, FishingRules.Group(GreenLure, 1, 54));
        Assert.Equal(0, FishingRules.Group(GreenLure, 1, 74));
        Assert.Equal(2, FishingRules.Group(GreenLure, 1, 94));
        Assert.Equal(3, FishingRules.Group(GreenLure, 1, 95));
        Assert.Equal(3, FishingRules.Group(7610, 1, 0));
        Assert.Equal(9, FishingRules.Group(8506, 2, 99));
        Assert.Equal(5, FishingRules.Group(7807, 0, 0));
        Assert.Equal((1, 1, 2, 27), (FishingRules.Level(0, 0), FishingRules.Level(1, 70), FishingRules.Level(1, 99), FishingRules.Level(27, 99)));
        Assert.Equal((665, 500, 330), (FishingRules.CheckDelayMs(6519, 5), FishingRules.CheckDelayMs(6520, 5), FishingRules.CheckDelayMs(6521, 5)));
        Assert.True(FishingRules.IsNightLure(8485) && FishingRules.IsNightLure(8513) && !FishingRules.IsNightLure(8484));
        // Skill three levels above expertise: 5% penalty.
        Assert.Equal((95, 5), FishingRules.Damage(40, 20, 5, 5, false, expertiseLevel: 1, skillLevel: 4));
        Assert.Equal((200, 0), FishingRules.Damage(40, 20, 5, 5, true, expertiseLevel: 2, skillLevel: 4));

        var items = GameData.LoadDefault().Items;
        Assert.True(items.TryGet(GreenLure, out var lure) && lure.EtcType == EtcItemType.Lure);
        Assert.True(items.TryGet(ShotD, out var shot) && shot.Crystal == CrystalGrade.D);
    }
}
