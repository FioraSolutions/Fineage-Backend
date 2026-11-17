using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.SevenSigns;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mammon after Mobius CT_0 (<c>SevenSigns.spawnSevenSignsNPC</c>, <c>AutoSpawnHandler</c>, <c>Npc.showChatWindow</c>,
/// <c>SignsPriest</c> 7/9/17/18): Merchant and Blacksmith of Mammon follow the seals, the strict rule gates their talk,
/// their shops are the Ancient Adena multisells (sealed armor release included), and Ancient Adena trades both ways.
/// </summary>
public sealed class NetMammonTests
{
    const int AA = SevenSignsProtocol.AncientAdenaItemId;
    static readonly DateTimeOffset Start = new(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);

    sealed class Seq(params int[] values) : Random
    {
        readonly Queue<int> _q = new(values);
        public override int Next(int maxValue) => _q.Count > 0 ? _q.Dequeue() % maxValue : 0;
    }

    sealed class Rig
    {
        public required WorldBench Bench { get; init; }
        public required MutableGameClock Clock { get; init; }
        public required SevenSignsEngine Engine { get; init; }
        public required BenchPlayer Dawn { get; init; }
        public required BenchPlayer Dusk { get; init; }

        public void Advance()
        {
            Clock.UtcNow = Engine.NextChange;
            Assert.True(Bench.World.AdvanceSevenSignsAsync(CancellationToken.None).Result);
        }

        public void Mammon(TimeSpan after)
        {
            Clock.UtcNow += after;
            Bench.World.TickMammonForTests();
        }
    }

    /// <summary>
    /// Dawn wins the competition and takes Avarice and Gnosis (half of its members chose each); a Dusk member exists.
    /// Leaves the engine at <paramref name="stopAt"/>.
    /// </summary>
    static async Task<Rig> Open(SevenSignsPeriod stopAt = SevenSignsPeriod.SealValidation, SevenSignsOptions? options = null)
    {
        var clock = new MutableGameClock(Start);
        var engine = new SevenSignsEngine(clock, options);
        var bench = new WorldBench(clock: () => clock.UtcNow, sevenSigns: engine);
        var dawn = await bench.EnterAsync("dawn", "Lord", items: [new StoredItem(57, 1000, -1)], classId: 1);
        var dusk = await bench.EnterAsync("dusk", "Rebel", items: [new StoredItem(57, 1000, -1)], classId: 1);
        var rig = new Rig { Bench = bench, Clock = clock, Engine = engine, Dawn = dawn, Dusk = dusk };
        rig.Advance(); // competition
        var notices = new List<SevenSignsNotice>();
        Assert.Null(engine.TryJoin(dawn.ObjectId, 1, false, SevenSignsCabal.Dawn, SevenSignsSeal.Avarice, null, notices));
        for (int i = 0; i < 4; i++)
            Assert.Null(engine.TryJoin(900_000 + i, 1, false, SevenSignsCabal.Dawn, SevenSignsSeal.Avarice, null, notices));
        for (int i = 0; i < 5; i++)
            Assert.Null(engine.TryJoin(910_000 + i, 1, false, SevenSignsCabal.Dawn, SevenSignsSeal.Gnosis, null, notices));
        Assert.Null(engine.TryJoin(dusk.ObjectId, 1, false, SevenSignsCabal.Dusk, SevenSignsSeal.Strife, null, notices));
        Assert.Null(engine.TryContribute(dawn.ObjectId, 0, 0, 10)); // 100 AA reward, dawn wins
        if (stopAt == SevenSignsPeriod.Competition)
            return rig;
        rig.Advance(); // results
        if (stopAt == SevenSignsPeriod.SealValidation)
            rig.Advance();
        Assert.Equal(stopAt, engine.Period);
        return rig;
    }

    static string LastHtml(WorldBench bench) =>
        Assert.IsType<NpcHtmlMsg>(bench.Rec.Of("FanoutNpcHtml").Last()[1]).Html;

    static List<int> Lines(WorldBench bench, int objectId) =>
        bench.Rec.Of("FanoutSystemMessage")
            .Where(a => ((IReadOnlyList<int>)a[0]!).Contains(objectId))
            .Select(a => ((SystemMessageMsg)a[1]!).Id).ToList();

    static void Near(BenchPlayer p, Fineage.Zone.World.ZoneEntity npc) => p.MoveTo(npc.X + 30, npc.Y, npc.Z);

    [Fact]
    public async Task Mammons_follow_the_seals_appear_after_30s_move_hourly_and_leave_with_the_period()
    {
        var rig = await Open();
        var world = rig.Bench.World;
        world.MammonRandom = new Seq(2, 3, 7, 1);

        world.TickMammonForTests();
        Assert.Null(world.MammonMerchantForTests);
        rig.Mammon(TimeSpan.FromSeconds(29));
        Assert.Null(world.MammonMerchantForTests);
        rig.Mammon(TimeSpan.FromSeconds(1));

        var merchant = world.MammonMerchantForTests!;
        var smith = world.MammonBlacksmithForTests!;
        Assert.Equal(SevenSignsProtocol.MammonMerchantId, merchant.NpcId);
        Assert.Equal(SevenSignsProtocol.MammonBlacksmithId, smith.NpcId);
        Assert.False(merchant.Attackable);
        Assert.Equal((-21657, 77164), (merchant.X, merchant.Y)); // Patriots' Necropolis
        Assert.Equal((-20485, -251008), (smith.X, smith.Y));     // Catacomb of the Apostate

        rig.Mammon(TimeSpan.FromMinutes(59));
        Assert.Same(merchant, world.MammonMerchantForTests);
        rig.Mammon(TimeSpan.FromMinutes(1));
        var moved = world.MammonMerchantForTests!;
        Assert.NotSame(merchant, moved);
        Assert.Equal((172373, -17833), (moved.X, moved.Y)); // Disciples' Necropolis
        Assert.Equal((-53131, -250502), (world.MammonBlacksmithForTests!.X, world.MammonBlacksmithForTests!.Y));

        rig.Advance(); // seal validation ends → recruiting
        world.TickMammonForTests();
        Assert.Null(world.MammonMerchantForTests);
        Assert.Null(world.MammonBlacksmithForTests);
    }

    [Fact]
    public async Task No_mammon_during_competition_and_results_spawn_the_merchant_too()
    {
        var rig = await Open(SevenSignsPeriod.Competition);
        rig.Mammon(TimeSpan.FromSeconds(30));
        rig.Mammon(TimeSpan.FromSeconds(30));
        Assert.Null(rig.Bench.World.MammonMerchantForTests);

        rig.Advance(); // results: Mobius spawns them already
        rig.Mammon(TimeSpan.Zero);
        rig.Mammon(TimeSpan.FromSeconds(30));
        Assert.NotNull(rig.Bench.World.MammonMerchantForTests);
        Assert.NotNull(rig.Bench.World.MammonBlacksmithForTests);
    }

    [Fact]
    public async Task Strict_rule_serves_the_winning_seal_owner_and_refuses_the_rest()
    {
        var rig = await Open();
        rig.Mammon(TimeSpan.Zero);
        rig.Mammon(TimeSpan.FromSeconds(30));
        var merchant = rig.Bench.World.MammonMerchantForTests!;
        var smith = rig.Bench.World.MammonBlacksmithForTests!;

        Near(rig.Dawn, merchant);
        Assert.Null(await rig.Dawn.Talk(merchant.ObjectId));
        Assert.Contains("multisell 311132501", LastHtml(rig.Bench));
        Near(rig.Dawn, smith);
        Assert.Null(await rig.Dawn.Talk(smith.ObjectId));
        Assert.Contains("seven_signs/mammblack_2b.htm", LastHtml(rig.Bench));
        Assert.Null(await rig.Dawn.BypassAsync(smith.ObjectId, "Link seven_signs/mammblack_2b.htm"));
        Assert.Contains("exc_multisell 311262506", LastHtml(rig.Bench));

        int pages = rig.Bench.Rec.Of("FanoutNpcHtml").Count;
        Near(rig.Dusk, merchant);
        Assert.Null(await rig.Dusk.Talk(merchant.ObjectId));
        Assert.Equal(pages, rig.Bench.Rec.Of("FanoutNpcHtml").Count);
        Assert.Contains(SevenSignsProtocol.OnlyDawnMessage, Lines(rig.Bench, rig.Dusk.ObjectId));
    }

    [Fact]
    public async Task Without_strict_anyone_may_trade_with_mammon()
    {
        var rig = await Open(options: new SevenSignsOptions { Strict = false });
        rig.Mammon(TimeSpan.Zero);
        rig.Mammon(TimeSpan.FromSeconds(30));
        var merchant = rig.Bench.World.MammonMerchantForTests!;
        Near(rig.Dusk, merchant);
        Assert.Null(await rig.Dusk.Talk(merchant.ObjectId));
        Assert.Contains("Merchant of Mammon", LastHtml(rig.Bench));
    }

    [Fact]
    public async Task Blacksmith_releases_the_seal_on_a_grade_armor_for_ancient_adena()
    {
        var rig = await Open();
        rig.Mammon(TimeSpan.Zero);
        rig.Mammon(TimeSpan.FromSeconds(30));
        var smith = rig.Bench.World.MammonBlacksmithForTests!;
        var p = rig.Dawn;
        Assert.Null(await rig.Bench.World.TryGrantItemAsync(p.Session, 5287, 1, CancellationToken.None)); // Sealed Dark Crystal Breastplate
        p.Inventory.Add(AA, 400_000); // above the GM grant cap
        Near(p, smith);

        Assert.Null(await p.BypassAsync(smith.ObjectId, "exc_multisell 311262506"));
        var list = Assert.IsType<MultiSellListMsg>(rig.Bench.Rec.Of("FanoutMultiSellList").Last()[1]);
        Assert.Equal(311262506, list.ListId);
        Assert.True(list.InventoryOnly);
        var entry = Assert.Single(list.Entries); // only what the bag can unseal
        Assert.Null(await p.MultiSell(smith.ObjectId, 311262506, entry.EntryId, 1));
        Assert.Equal(0, p.Inventory.CountOf(5287));
        Assert.Equal(1, p.Inventory.CountOf(365));
        Assert.Equal(400_000 - 347_250, p.Inventory.CountOf(AA));
    }

    [Fact]
    public async Task Black_marketeer_swaps_ancient_adena_for_adena_one_to_one()
    {
        var rig = await Open(SevenSignsPeriod.Competition);
        var p = rig.Dawn;
        var npc = rig.Bench.World.SpawnFolkForTests(SevenSignsProtocol.MammonMarketeerId, p.Player.X + 30, p.Player.Y, p.Player.Z);
        Assert.Null(await p.Talk(npc.ObjectId));
        Assert.Contains("seven_signs/blkmrkt_2.htm", LastHtml(rig.Bench));
        Assert.Null(await p.BypassAsync(npc.ObjectId, "Link seven_signs/blkmrkt_2.htm"));
        Assert.Contains("SevenSigns 7 $amount", LastHtml(rig.Bench));

        Assert.Null(await rig.Bench.World.TryGrantItemAsync(p.Session, AA, 500, CancellationToken.None));
        Assert.Null(await p.BypassAsync(npc.ObjectId, "SevenSigns 7 abc"));
        Assert.Contains("invalid value", LastHtml(rig.Bench));
        Assert.Null(await p.BypassAsync(npc.ObjectId, "SevenSigns 7 501"));
        Assert.Contains("don't have enough", LastHtml(rig.Bench));
        Assert.Null(await p.BypassAsync(npc.ObjectId, "SevenSigns 7 200"));
        Assert.Contains("Successful Exchange", LastHtml(rig.Bench));
        Assert.Equal(300, p.Inventory.CountOf(AA));
        Assert.Equal(1200, p.Inventory.CountOf(57));
    }

    [Fact]
    public async Task Priests_buy_seal_stones_for_ancient_adena_and_winners_collect_their_reward_in_validation()
    {
        var rig = await Open(SevenSignsPeriod.Results);
        var world = rig.Bench.World;
        var p = rig.Dawn;
        Assert.Null(await world.TryGrantItemAsync(p.Session, SevenSignsProtocol.BlueSealStoneId, 4, CancellationToken.None));
        Assert.Null(await world.TryGrantItemAsync(p.Session, SevenSignsProtocol.GreenSealStoneId, 2, CancellationToken.None));

        Task<string?> Act(BenchPlayer who, int action, int blue = 0, int green = 0, int red = 0) =>
            world.TrySevenSignsActionAsync(who.Session, new SevenSignsActionRequestMsg
            {
                Action = action, BlueStones = blue, GreenStones = green, RedStones = red
            }, CancellationToken.None);

        Assert.Equal(SevenSignsProtocol.StonesMissing, await Act(p, SevenSignsProtocol.ActionExchangeStones, blue: 5));
        Assert.Null(await Act(p, SevenSignsProtocol.ActionExchangeStones, blue: 4, green: 2));
        Assert.Equal(4 * 3 + 2 * 5, p.Inventory.CountOf(AA));
        Assert.Equal(0, p.Inventory.CountOf(SevenSignsProtocol.BlueSealStoneId));

        // The contribution reward waits for seal validation.
        Assert.Equal(SevenSignsProtocol.PeriodClosed, await Act(p, SevenSignsProtocol.ActionClaimReward));
        rig.Advance();
        Assert.Equal(SevenSignsProtocol.NoReward, await Act(rig.Dusk, SevenSignsProtocol.ActionClaimReward));
        Assert.Null(await Act(p, SevenSignsProtocol.ActionClaimReward));
        Assert.Equal(22 + 100, p.Inventory.CountOf(AA));
        Assert.Equal(0, rig.Engine.StatusFor(p.ObjectId).PlayerAncientAdena);
        Assert.Equal(100, rig.Engine.StatusFor(p.ObjectId).PlayerContribution);
        Assert.Equal(SevenSignsProtocol.NoReward, await Act(p, SevenSignsProtocol.ActionClaimReward));
    }
}
