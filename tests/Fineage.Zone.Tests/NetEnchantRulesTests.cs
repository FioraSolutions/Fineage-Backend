using Fineage.Game;
using Fineage.Accounts;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius enchant <em>roll</em> rules on the world layer: what each scroll's fail mode does to the item, the bag
/// and the paperdoll. The Net transport cases live in <see cref="NetEnchantTests"/>; these assert the outcome
/// through <see cref="IWorldEventPublisher"/> instead of reading MessagePack frames.
/// </summary>
public sealed class NetEnchantRulesTests
{
    const int Saber = 123;
    const int NormalD = 955;
    const int BlessedD = 6575;
    const int CrystalD = 957;
    const int ArmorD = 956;
    const int NormalC = 951;
    const int Neck = 911;
    const int Full = 2390;
    const int ArmorB = 948;
    const int CrystalS = 961;
    const int Blade = 6364;
    const int CrystalAdena = 1458;

    static StoredItem Bag(int itemId, int count = 1) => new(itemId, count, -1);

    /// <summary>A world whose enchant roll replays <paramref name="values"/>, with the Net fanout recorded.</summary>
    sealed class Bench
    {
        public MemoryAccountStore Store { get; } = MemoryAccountStore.InMemory();
        public ZoneWorld World { get; private set; } = null!;
        public NetBridgeRecorder Rec { get; private set; } = null!;

        public Bench(params double[] rolls) => Build(rolls, extra: null);

        /// <summary>With one extra item template appended to the shipped catalog (custom scrolls / fail modes).</summary>
        public Bench(double roll, ItemTemplate extra) => Build([roll], extra);

        void Build(double[] rolls, ItemTemplate? extra)
        {
            GameData? game = null;
            if (extra != null)
            {
                var data = GameData.LoadDefault();
                var templates = data.Items.Templates.ToList();
                templates.Add(extra);
                game = GameData.From(new ItemCatalog(templates), data.StarterKits);
            }

            World = new ZoneWorld(game: game, accounts: Store, enchantRandom: new SequenceEnchantRandom(rolls));
            var (bridge, rec) = NetBridgeRecorder.Create();
            World.NetBridge = bridge;
            Rec = rec;
        }

        public async Task<(ZoneSession Session, ZoneEntity Player)> EnterAsync(
            string account, string name, params StoredItem[] items)
        {
            var login = new LoginService(Store, new HandoffTokens(HandoffTokens.DefaultSecret));
            var ticket = login.Play(account, "pass1234", name, HumanClasses.Fighter);
            Assert.True(ticket.Ok && ticket.Value != null, ticket.Message);
            if (items.Length > 0)
                Assert.True(Store.TryReplaceItems(ticket.Value.Account, ticket.Value.CharacterId, items));

            var session = new ZoneSession();
            session.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
            Assert.True(await World.TryEnterAsync(session, CancellationToken.None));
            Assert.True(World.TryGetLivePlayer(session, out var player));
            Rec.Calls.Clear();
            return (session, player);
        }
    }

    static int SlotFor(ZoneEntity player, int itemId) =>
        player.Inventory!.FindByItemId(itemId)!.ObjectId;

    static async Task<int> OpenScrollAsync(Bench bench, ZoneSession session, ZoneEntity player, int scrollId)
    {
        int scroll = SlotFor(player, scrollId);
        Assert.Null(await bench.World.TryEnchantRequestAsync(session, scroll, default));
        return scroll;
    }

    static EnchantResultNetMsg LastResult(NetBridgeRecorder rec) =>
        Assert.IsType<EnchantResultNetMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutEnchantResult)).Last()[1]);

    static IReadOnlyList<SystemMessageMsg> Lines(NetBridgeRecorder rec) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Select(args => Assert.IsType<SystemMessageMsg>(args[1]))
            .ToList();

    [Fact]
    public async Task Normal_failure_destroys_the_item_and_pays_mobius_crystals()
    {
        var bench = new Bench(0.99);
        var (session, player) = await bench.EnterAsync("ench", "Alden",
            new StoredItem(Saber, 1, -1, 3), Bag(NormalD), Bag(CrystalAdena, 2));

        await OpenScrollAsync(bench, session, player, NormalD);
        Assert.Null(await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Saber), default));

        var result = LastResult(bench.Rec);
        Assert.Equal(EnchantProtocol.ResultCrystallize, result.Result);
        Assert.Equal(0, result.EnchantLevel);
        Assert.Equal(CrystalAdena, result.CrystalItemId);
        Assert.Equal(641, result.CrystalCount);

        var update = Assert.IsType<InventoryUpdateMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutInventoryUpdate)).Last()[1]);
        Assert.Contains(update.Changes, row => row.Change == InventoryChange.Remove &&
                                               row.ItemId == Saber && row.EnchantLevel == 3 && row.Count == 0);
        Assert.Contains(update.Changes, row => row.Change == InventoryChange.Modify &&
                                               row.ItemId == CrystalAdena && row.Count == 643);

        var lines = Lines(bench.Rec);
        Assert.Contains(lines, l => l.Id == EnchantProtocol.CrystallizedLevel &&
                                    l.Text == "The enchantment has failed. Your +3 Saber has been crystallized.");
        Assert.Contains(lines, l => l.Id == EnchantProtocol.Earned &&
                                    l.Params.SequenceEqual(["641", "Crystal: D-Grade"]));

        var stored = bench.Store.FindById(session.Account, session.ObjectId)!.Items!;
        Assert.DoesNotContain(stored, item => item.ItemId == Saber);
        Assert.Contains(stored, item => item.ItemId == CrystalAdena && item.Count == 643);
    }

    [Fact]
    public async Task Blessed_failure_resets_to_zero_and_a_keep_scroll_leaves_the_level_alone()
    {
        var bench = new Bench(0.99);
        var (session, player) = await bench.EnterAsync("ench", "Alden",
            new StoredItem(Saber, 1, -1, 3), Bag(BlessedD));

        await OpenScrollAsync(bench, session, player, BlessedD);
        Assert.Null(await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Saber), default));

        var result = LastResult(bench.Rec);
        Assert.Equal(EnchantProtocol.ResultReset, result.Result);
        Assert.Equal(0, result.EnchantLevel);
        var update = Assert.IsType<InventoryUpdateMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutInventoryUpdate)).Last()[1]);
        Assert.Contains(update.Changes, row => row.ItemId == Saber &&
                                               row.EnchantLevel == 0 && row.Change == InventoryChange.Modify);
        Assert.Contains(Lines(bench.Rec), l => l.Id == EnchantProtocol.Reset);
        Assert.Contains(bench.Store.FindById(session.Account, session.ObjectId)!.Items!,
            item => item.ItemId == Saber && item.EnchantLevel == 0);
    }

    [Fact]
    public async Task Keep_scroll_failure_keeps_the_level_and_still_eats_the_scroll()
    {
        // Build a keep scroll explicitly: the shipped 957/961 crystal scrolls never fail.
        var keep = new ItemTemplate(
            9001, "Test Crystal Scroll", ItemKind.Etc, BodySlot.None, "icon.test",
            Scroll: new EnchantScroll(true, CrystalGrade.D, EnchantFail.Keep, 65535, 0));
        var bench = new Bench(0.99, keep);
        var (session, player) = await bench.EnterAsync("ench", "Alden",
            new StoredItem(Saber, 1, -1, 3), Bag(9001));

        await OpenScrollAsync(bench, session, player, 9001);
        Assert.Null(await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Saber), default));

        var result = LastResult(bench.Rec);
        Assert.Equal(EnchantProtocol.ResultKeep, result.Result);
        Assert.Equal(3, result.EnchantLevel);
        Assert.Equal(SlotFor(player, Saber), result.ObjectId);

        var update = Assert.IsType<InventoryUpdateMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutInventoryUpdate)).Last()[1]);
        Assert.DoesNotContain(update.Changes, row => row.ItemId == Saber);
        Assert.Contains(update.Changes, row => row.Change == InventoryChange.Remove && row.ItemId == 9001);
        Assert.Contains(Lines(bench.Rec), l => l.Id == EnchantProtocol.Kept);
        Assert.Contains(bench.Store.FindById(session.Account, session.ObjectId)!.Items!,
            item => item.ItemId == Saber && item.EnchantLevel == 3);
    }

    [Fact]
    public async Task Grade_slot_and_max_enchant_reject_without_consuming_the_scroll()
    {
        var bench = new Bench(0);
        var (session, player) = await bench.EnterAsync("ench", "Alden",
            Bag(Saber), Bag(Neck), Bag(5588), new StoredItem(Saber, 1, -1, 16), Bag(NormalD), Bag(NormalC), Bag(ArmorD));

        // Two sabers: the +16 one is the capped target, the fresh one is anything else.
        var sabers = player.Inventory!.Items.Where(item => item.ItemId == Saber).ToList();
        int capped = sabers.Single(s => s.EnchantLevel == 16).ObjectId;
        int fresh = sabers.Single(s => s.EnchantLevel != 16).ObjectId;

        await OpenScrollAsync(bench, session, player, NormalC);
        Assert.Equal(EnchantProtocol.GradeMismatch, await bench.World.TryEnchantConfirmAsync(session, fresh, default));
        Assert.Contains(bench.Store.FindById(session.Account, session.ObjectId)!.Items!, item => item.ItemId == NormalC);

        await OpenScrollAsync(bench, session, player, NormalD);
        Assert.Equal(EnchantProtocol.SlotMismatch, await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Neck), default));

        await OpenScrollAsync(bench, session, player, NormalD);
        Assert.Equal(EnchantProtocol.NotEnchantable, await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, 5588), default));

        await OpenScrollAsync(bench, session, player, ArmorD);
        Assert.Null(await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Neck), default));
        Assert.Equal(1, LastResult(bench.Rec).EnchantLevel);

        await OpenScrollAsync(bench, session, player, NormalD);
        Assert.Equal(EnchantProtocol.MaxEnchant, await bench.World.TryEnchantConfirmAsync(session, capped, default));
        Assert.Contains(bench.Store.FindById(session.Account, session.ObjectId)!.Items!,
            item => item.ItemId == Saber && item.EnchantLevel == 16);
    }

    [Fact]
    public async Task Equipped_success_updates_the_paperdoll_for_the_owner_and_peers()
    {
        var bench = new Bench(0, 0.99);
        var (session, player) = await bench.EnterAsync("ench", "Alden",
            new StoredItem(Saber, 1, 7, 0), Bag(NormalD), Bag(BlessedD));
        await bench.EnterAsync("enchb", "Bram", Bag(Neck));

        await OpenScrollAsync(bench, session, player, NormalD);
        Assert.Null(await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Saber), default));

        var info = Assert.IsType<UserInfoNetMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutUserInfo)).Last()[1]);
        Assert.Equal(Saber, info.Paperdoll[7].ItemId);
        Assert.Equal(1, info.Paperdoll[7].EnchantLevel);
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutCharInfo)));
        Assert.Contains(Lines(bench.Rec), l => l.Id == EnchantProtocol.SuccessPlain);
    }

    [Fact]
    public async Task Equipped_normal_failure_unequips_before_the_item_is_destroyed()
    {
        var bench = new Bench(0.99);
        var (session, player) = await bench.EnterAsync("ench", "Alden",
            new StoredItem(Saber, 1, 7, 3), Bag(NormalD));
        await bench.EnterAsync("enchb", "Bram", Bag(Neck));

        await OpenScrollAsync(bench, session, player, NormalD);
        Assert.Null(await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Saber), default));

        Assert.Contains(Lines(bench.Rec), l => l.Id == EnchantProtocol.EquipmentRemoved);
        var info = Assert.IsType<UserInfoNetMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutUserInfo)).Last()[1]);
        Assert.Equal(0, info.Paperdoll[7].ItemId);
        Assert.Equal(0, info.Paperdoll[7].EnchantLevel);
        Assert.Contains(bench.Store.FindById(session.Account, session.ObjectId)!.Items!,
            item => item.ItemId == CrystalAdena && item.Count == 641);
    }

    [Fact]
    public async Task Shipped_crystal_scroll_past_safe_succeeds_and_never_passes_plus_sixteen()
    {
        var bench = new Bench(0.99, 0.99);
        var (session, player) = await bench.EnterAsync("ench", "Alden",
            new StoredItem(Saber, 1, -1, 3), Bag(CrystalD),
            new StoredItem(Blade, 1, -1, 16), Bag(CrystalS));

        await OpenScrollAsync(bench, session, player, CrystalD);
        Assert.Null(await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Saber), default));
        Assert.Equal(4, LastResult(bench.Rec).EnchantLevel);

        bench.Rec.Calls.Clear();
        await OpenScrollAsync(bench, session, player, CrystalS);
        Assert.Null(await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Blade), default));
        Assert.Equal(16, LastResult(bench.Rec).EnchantLevel);
        var update = Assert.IsType<InventoryUpdateMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutInventoryUpdate)).Last()[1]);
        Assert.Contains(update.Changes, row => row.ItemId == Blade && row.EnchantLevel == 16);
        Assert.DoesNotContain(bench.Store.FindById(session.Account, session.ObjectId)!.Items!,
            item => item.ItemId == CrystalS);
    }

    [Fact]
    public async Task Full_armor_at_plus_three_is_still_a_safe_step()
    {
        var bench = new Bench(0.99);
        var (session, player) = await bench.EnterAsync("ench", "Alden",
            new StoredItem(Full, 1, -1, 3), Bag(ArmorB));

        await OpenScrollAsync(bench, session, player, ArmorB);
        Assert.Null(await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Full), default));
        Assert.Equal(4, LastResult(bench.Rec).EnchantLevel);
        Assert.Contains(bench.Store.FindById(session.Account, session.ObjectId)!.Items!,
            item => item.ItemId == Full && item.EnchantLevel == 4);
    }

    [Fact]
    public async Task Request_cancel_and_confirm_walk_the_scroll_then_the_target()
    {
        var bench = new Bench(0);
        var (session, player) = await bench.EnterAsync("ench", "Alden",
            Bag(Saber), Bag(NormalD), Bag(NormalC));

        Assert.Equal(EnchantProtocol.NoRequest, await bench.World.TryEnchantCancelAsync(session, default));
        Assert.Equal(InventoryProtocol.NotOwned, await bench.World.TryEnchantRequestAsync(session, 999, default));
        Assert.Equal(EnchantProtocol.NotScroll, await bench.World.TryEnchantRequestAsync(session, SlotFor(player, Saber), default));

        await OpenScrollAsync(bench, session, player, NormalD);
        var opened = Assert.Single(Lines(bench.Rec));
        Assert.Equal(EnchantProtocol.SelectItem, opened.Id);
        Assert.Equal("Select an item to enchant.", opened.Text);
        Assert.Equal([SlotFor(player, NormalD).ToString(), NormalD.ToString()], opened.Params);

        Assert.Null(await bench.World.TryEnchantCancelAsync(session, default));
        var cancelled = LastResult(bench.Rec);
        Assert.Equal(EnchantProtocol.ResultCancel, cancelled.Result);
        Assert.Equal(0, cancelled.ObjectId);
        Assert.Contains(Lines(bench.Rec), l => l.Id == EnchantProtocol.Cancelled);

        bench.Rec.Calls.Clear();
        await OpenScrollAsync(bench, session, player, NormalD);
        Assert.Null(await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Saber), default));
        var success = LastResult(bench.Rec);
        Assert.Equal(EnchantProtocol.ResultSuccess, success.Result);
        Assert.Equal(1, success.EnchantLevel);
        Assert.Equal(SlotFor(player, Saber), success.ObjectId);
        Assert.Contains(Lines(bench.Rec), l => l.Id == EnchantProtocol.SuccessPlain);

        Assert.Equal(EnchantProtocol.NoRequest, await bench.World.TryEnchantConfirmAsync(session, SlotFor(player, Saber), default));
    }
}
