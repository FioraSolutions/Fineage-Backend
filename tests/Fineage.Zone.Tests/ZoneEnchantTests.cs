using System.Net;
using System.Net.Sockets;
using Fineage.Game;
using Fineage.Zone;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Interest;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Enchant request, confirm, and cancel on the zone world.</summary>
public sealed class ZoneEnchantTests
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

    [Fact]
    public async Task Request_confirm_and_cancel_follow_the_scroll_then_the_target()
    {
        var (login, world, store) = Open(new SequenceEnchantRandom(0));
        await using var actor = await EnterAsync(login, world, store, "ench", "Alden",
            [Bag(Saber), Bag(NormalD), Bag(NormalC)]);

        var idle = new ZoneSession(new MemoryStream());
        idle.Bind(actor.Ticket.CharacterId, actor.Ticket.Account, actor.Ticket.CharacterName, actor.Ticket.ClassId);
        Assert.Equal("world", await world.TryEnchantRequestAsync(idle, new EnchantRequestMsg { ObjectId = 1 }, CancellationToken.None));

        Assert.Equal(EnchantProtocol.NoRequest, await actor.Cancel());
        Assert.Equal(InventoryProtocol.NotOwned, await actor.Request(999));
        Assert.Equal(EnchantProtocol.NotScroll, await actor.Request(actor.ObjectId(Saber)));
        await actor.ExpectQuietAsync();

        Assert.Null(await actor.Request(actor.ObjectId(NormalD)));
        var opened = await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(EnchantProtocol.SelectItem, opened.Id);
        Assert.Equal("Select an item to enchant.", opened.Text);
        Assert.Equal([actor.ObjectId(NormalD).ToString(), NormalD.ToString()], opened.Params);

        Assert.Null(await actor.Cancel());
        var frames = await actor.FramesAsync();
        Assert.Equal((ushort)MsgType.EnchantResult, frames[0].Type);
        Assert.Equal((ushort)MsgType.SystemMessage, frames[1].Type);
        var cancelled = MessagePackSerializer.Deserialize<EnchantResultMsg>(frames[0].Body);
        var line = MessagePackSerializer.Deserialize<SystemMessageMsg>(frames[1].Body);
        Assert.Equal(EnchantProtocol.ResultCancel, cancelled.Result);
        Assert.Equal(0, cancelled.ObjectId);
        Assert.Equal(EnchantProtocol.Cancelled, line.Id);

        Assert.Null(await actor.Request(actor.ObjectId(NormalD)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Null(await actor.Confirm(actor.ObjectId(Saber)));
        var done = await actor.FramesAsync();
        var result = MessagePackSerializer.Deserialize<EnchantResultMsg>(done[0].Body);
        var update = MessagePackSerializer.Deserialize<InventoryUpdateMsg>(done[1].Body);
        var success = MessagePackSerializer.Deserialize<SystemMessageMsg>(done[2].Body);
        Assert.Equal(3, done.Count);
        Assert.Equal(EnchantProtocol.ResultSuccess, result.Result);
        Assert.Equal(1, result.EnchantLevel);
        Assert.Equal(actor.ObjectId(Saber), result.ObjectId);
        Assert.Contains(update.Changes, row => row.Change == InventoryChange.Remove && row.ObjectId == actor.ObjectId(NormalD) && row.Count == 0);
        Assert.Contains(update.Changes, row => row.Change == InventoryChange.Modify && row.ObjectId == actor.ObjectId(Saber) && row.EnchantLevel == 1);
        Assert.Equal(EnchantProtocol.SuccessPlain, success.Id);
        Assert.Equal("Your Saber has been successfully enchanted.", success.Text);
        Assert.Contains(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == Saber && item.EnchantLevel == 1);
        Assert.DoesNotContain(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == NormalD);

        Assert.Equal(EnchantProtocol.NoRequest, await actor.Confirm(actor.ObjectId(Saber)));
    }

    [Fact]
    public async Task Normal_failure_destroys_the_item_and_pays_mobius_crystals()
    {
        var (login, world, store) = Open(new SequenceEnchantRandom(0.99));
        await using var actor = await EnterAsync(login, world, store, "ench", "Alden",
            [new StoredItem(Saber, 1, -1, 3), Bag(NormalD), Bag(1458, 2)]);

        Assert.Null(await actor.Request(actor.ObjectId(NormalD)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Null(await actor.Confirm(actor.ObjectId(Saber)));
        var frames = await actor.FramesAsync();
        var result = MessagePackSerializer.Deserialize<EnchantResultMsg>(frames[0].Body);
        var update = MessagePackSerializer.Deserialize<InventoryUpdateMsg>(frames[1].Body);
        var crystallized = MessagePackSerializer.Deserialize<SystemMessageMsg>(frames[2].Body);
        var earned = MessagePackSerializer.Deserialize<SystemMessageMsg>(frames[3].Body);
        Assert.Equal(EnchantProtocol.ResultCrystallize, result.Result);
        Assert.Equal(0, result.EnchantLevel);
        Assert.Equal(1458, result.CrystalItemId);
        Assert.Equal(641, result.CrystalCount);
        Assert.Contains(update.Changes, row => row.Change == InventoryChange.Remove && row.ItemId == Saber && row.EnchantLevel == 3 && row.Count == 0);
        Assert.Contains(update.Changes, row => row.Change == InventoryChange.Modify && row.ItemId == 1458 && row.Count == 643);
        Assert.Equal(EnchantProtocol.CrystallizedLevel, crystallized.Id);
        Assert.Equal("The enchantment has failed. Your +3 Saber has been crystallized.", crystallized.Text);
        Assert.Equal(EnchantProtocol.Earned, earned.Id);
        Assert.Equal(["641", "Crystal: D-Grade"], earned.Params);
        Assert.DoesNotContain(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == Saber);
        Assert.Contains(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == 1458 && item.Count == 643);
    }

    [Fact]
    public async Task Blessed_failure_resets_to_zero_and_crystal_failure_keeps_the_level()
    {
        var (login, world, store) = Open(new SequenceEnchantRandom(0.99, 0.99));
        await using var actor = await EnterAsync(login, world, store, "ench", "Alden",
            [new StoredItem(Saber, 1, -1, 3), Bag(BlessedD)]);

        Assert.Null(await actor.Request(actor.ObjectId(BlessedD)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Null(await actor.Confirm(actor.ObjectId(Saber)));
        var frames = await actor.FramesAsync();
        var result = MessagePackSerializer.Deserialize<EnchantResultMsg>(frames[0].Body);
        var update = MessagePackSerializer.Deserialize<InventoryUpdateMsg>(frames[1].Body);
        var reset = MessagePackSerializer.Deserialize<SystemMessageMsg>(frames[2].Body);
        Assert.Equal(EnchantProtocol.ResultReset, result.Result);
        Assert.Equal(0, result.EnchantLevel);
        Assert.Contains(update.Changes, row => row.ItemId == Saber && row.EnchantLevel == 0 && row.Change == InventoryChange.Modify);
        Assert.Equal(EnchantProtocol.Reset, reset.Id);
        Assert.Contains(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == Saber && item.EnchantLevel == 0);

        var keepScroll = new ItemTemplate(
            9001, "Test Crystal Scroll", ItemKind.Etc, BodySlot.None, "icon.test",
            Scroll: new EnchantScroll(true, CrystalGrade.D, EnchantFail.Keep, 65535, 0));
        var custom = CustomWorld(new SequenceEnchantRandom(0.99), keepScroll);
        await using var keeper = await EnterAsync(custom.Login, custom.World, custom.Store, "keep", "Bram",
            [new StoredItem(Saber, 1, -1, 3), Bag(9001)]);
        Assert.Null(await keeper.Request(keeper.ObjectId(9001)));
        await keeper.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Null(await keeper.Confirm(keeper.ObjectId(Saber)));
        frames = await keeper.FramesAsync();
        result = MessagePackSerializer.Deserialize<EnchantResultMsg>(frames[0].Body);
        update = MessagePackSerializer.Deserialize<InventoryUpdateMsg>(frames[1].Body);
        var kept = MessagePackSerializer.Deserialize<SystemMessageMsg>(frames[2].Body);
        Assert.Equal(EnchantProtocol.ResultKeep, result.Result);
        Assert.Equal(3, result.EnchantLevel);
        Assert.Equal(keeper.ObjectId(Saber), result.ObjectId);
        Assert.DoesNotContain(update.Changes, row => row.ItemId == Saber);
        Assert.Contains(update.Changes, row => row.Change == InventoryChange.Remove && row.ItemId == 9001);
        Assert.Equal(EnchantProtocol.Kept, kept.Id);
        Assert.Contains(custom.Store.FindById(keeper.Ticket.Account, keeper.Ticket.CharacterId)!.Items!, item => item.ItemId == Saber && item.EnchantLevel == 3);
    }

    [Fact]
    public async Task Grade_slot_and_max_enchant_reject_without_consuming_the_scroll()
    {
        var (login, world, store) = Open(new SequenceEnchantRandom(0));
        await using var actor = await EnterAsync(login, world, store, "ench", "Alden",
        [
            Bag(Saber), Bag(Neck), Bag(5588), new StoredItem(Saber, 1, -1, 16), Bag(NormalD), Bag(NormalC), Bag(ArmorD)
        ]);

        int capped = actor.ObjectIds(Saber).Single(id => actor.Items.Items.Single(item => item.ObjectId == id).EnchantLevel == 16);
        int fresh = actor.ObjectIds(Saber).Single(id => id != capped);

        Assert.Null(await actor.Request(actor.ObjectId(NormalC)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(EnchantProtocol.GradeMismatch, await actor.Confirm(fresh));
        await actor.ExpectQuietAsync();
        Assert.Contains(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == NormalC);

        Assert.Null(await actor.Request(actor.ObjectId(NormalD)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(EnchantProtocol.SlotMismatch, await actor.Confirm(actor.ObjectId(Neck)));
        Assert.Null(await actor.Request(actor.ObjectId(NormalD)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(EnchantProtocol.NotEnchantable, await actor.Confirm(actor.ObjectId(5588)));
        Assert.Null(await actor.Request(actor.ObjectId(ArmorD)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Null(await actor.Confirm(actor.ObjectId(Neck)));
        var neck = await actor.FramesAsync();
        Assert.Equal(1, MessagePackSerializer.Deserialize<EnchantResultMsg>(neck[0].Body).EnchantLevel);

        Assert.Null(await actor.Request(actor.ObjectIds(NormalD).Single()));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(EnchantProtocol.MaxEnchant, await actor.Confirm(capped));
        Assert.Contains(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == Saber && item.EnchantLevel == 16);
    }

    [Fact]
    public async Task Equipped_success_updates_paperdoll_and_a_normal_failure_clears_it()
    {
        var (login, world, store) = Open(new SequenceEnchantRandom(0, 0.99));
        await using var actor = await EnterAsync(login, world, store, "ench", "Alden",
            [new StoredItem(Saber, 1, 7, 0), Bag(NormalD), Bag(BlessedD)], x: 0);
        await using var near = await EnterAsync(login, world, store, "enchb", "Bram", [Bag(Neck)], x: 0);

        actor.ClearWire();
        near.ClearWire();
        Assert.Null(await actor.Request(actor.ObjectId(NormalD)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Null(await actor.Confirm(actor.ObjectId(Saber)));
        var frames = await actor.FramesAsync();
        Assert.Equal((ushort)MsgType.UserInfo, frames[^1].Type);
        var user = MessagePackSerializer.Deserialize<UserInfoMsg>(frames[^1].Body);
        var success = MessagePackSerializer.Deserialize<SystemMessageMsg>(frames[2].Body);
        Assert.Equal(EnchantProtocol.SuccessPlain, success.Id);
        Assert.Equal(1, user.Paperdoll[7].EnchantLevel);
        Assert.Equal(Saber, user.Paperdoll[7].ItemId);
        var seen = await near.ExpectCharAsync();
        Assert.Equal(Saber, seen.Paperdoll[7]);
        Assert.Equal(1, seen.Enchant[7]);
        Assert.Equal(17, seen.Enchant.Length);

        actor.ClearWire();
        near.ClearWire();
        world.TrySetPlayerDead(actor.Ticket.CharacterId, true);
        Assert.Equal(InventoryProtocol.Dead, await actor.Request(actor.ObjectId(BlessedD)));
        world.TrySetPlayerDead(actor.Ticket.CharacterId, false);

        Assert.Null(await actor.Request(actor.ObjectId(BlessedD)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        // The next sample is 0.99, but +1 is still inside the safe steps, so this succeeds to +2.
        Assert.Null(await actor.Confirm(actor.ObjectId(Saber)));
        frames = await actor.FramesAsync();
        var result = MessagePackSerializer.Deserialize<EnchantResultMsg>(frames[0].Body);
        Assert.Equal(EnchantProtocol.ResultSuccess, result.Result);
        Assert.Equal(2, result.EnchantLevel);
        var leveled = MessagePackSerializer.Deserialize<SystemMessageMsg>(frames[2].Body);
        Assert.Equal(EnchantProtocol.SuccessLevel, leveled.Id);
        Assert.Equal(["1", "Saber"], leveled.Params);
        Assert.Equal("Your +1 Saber has been successfully enchanted.", leveled.Text);

        await world.LeaveAsync(actor.Session, CancellationToken.None);
        await using var again = await EnterAsync(login, world, store, "ench", "Alden", []);
        var worn = again.Items.Items.Single(item => item.ItemId == Saber);
        Assert.Equal(2, worn.EnchantLevel);
        Assert.Equal(7, worn.EquippedSlot);
    }

    [Fact]
    public async Task Equipped_normal_failure_unequips_before_the_item_is_destroyed()
    {
        var (login, world, store) = Open(new SequenceEnchantRandom(0.99));
        await using var actor = await EnterAsync(login, world, store, "ench", "Alden",
            [new StoredItem(Saber, 1, 7, 3), Bag(NormalD)], x: 0);
        await using var near = await EnterAsync(login, world, store, "enchb", "Bram", [Bag(Neck)], x: 0);
        actor.ClearWire();
        near.ClearWire();

        Assert.Null(await actor.Request(actor.ObjectId(NormalD)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Null(await actor.Confirm(actor.ObjectId(Saber)));
        var frames = await actor.FramesAsync();
        var removed = MessagePackSerializer.Deserialize<SystemMessageMsg>(frames[2].Body);
        var user = MessagePackSerializer.Deserialize<UserInfoMsg>(frames[^1].Body);
        Assert.Equal(EnchantProtocol.EquipmentRemoved, removed.Id);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[7].EnchantLevel);
        var seen = await near.ExpectCharAsync();
        Assert.Equal(0, seen.Paperdoll[7]);
        Assert.Equal(0, seen.Enchant[7]);
        Assert.Contains(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == 1458 && item.Count == 641);
    }

    [Fact]
    public async Task A_shipped_crystal_scroll_succeeds_past_safe_and_does_not_pass_plus_16()
    {
        var (login, world, store) = Open(new SequenceEnchantRandom(0.99, 0.99));
        await using var actor = await EnterAsync(login, world, store, "ench", "Alden",
            [new StoredItem(Saber, 1, -1, 3), Bag(CrystalD), new StoredItem(Blade, 1, -1, 16), Bag(CrystalS)]);

        Assert.Null(await actor.Request(actor.ObjectId(CrystalD)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Null(await actor.Confirm(actor.ObjectId(Saber)));
        var frames = await actor.FramesAsync();
        var result = MessagePackSerializer.Deserialize<EnchantResultMsg>(frames[0].Body);
        Assert.Equal(EnchantProtocol.ResultSuccess, result.Result);
        Assert.Equal(4, result.EnchantLevel);

        Assert.Null(await actor.Request(actor.ObjectId(CrystalS)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Null(await actor.Confirm(actor.ObjectId(Blade)));
        frames = await actor.FramesAsync();
        result = MessagePackSerializer.Deserialize<EnchantResultMsg>(frames[0].Body);
        var update = MessagePackSerializer.Deserialize<InventoryUpdateMsg>(frames[1].Body);
        Assert.Equal(EnchantProtocol.ResultSuccess, result.Result);
        Assert.Equal(16, result.EnchantLevel);
        Assert.Contains(update.Changes, row => row.ItemId == Blade && row.EnchantLevel == 16);
        Assert.Contains(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == Blade && item.EnchantLevel == 16);
        Assert.DoesNotContain(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == CrystalS);
    }

    [Fact]
    public async Task Full_armor_at_plus_three_is_still_safe()
    {
        var (login, world, store) = Open(new SequenceEnchantRandom(0.99));
        await using var actor = await EnterAsync(login, world, store, "ench", "Alden",
            [new StoredItem(Full, 1, -1, 3), Bag(ArmorB)]);
        Assert.Null(await actor.Request(actor.ObjectId(ArmorB)));
        await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Null(await actor.Confirm(actor.ObjectId(Full)));
        var frames = await actor.FramesAsync();
        var result = MessagePackSerializer.Deserialize<EnchantResultMsg>(frames[0].Body);
        Assert.Equal(4, result.EnchantLevel);
        Assert.Contains(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == Full && item.EnchantLevel == 4);
    }

    [Fact]
    public async Task The_zone_port_rejects_a_bad_body_and_enchants_plus_zero_to_plus_one()
    {
        await using var host = await EnchantHost.StartAsync();
        var ticket = host.Login.Play("wire", "pass1234", "Alden", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null, ticket.Message);
        Assert.True(host.Store.TryReplaceItems(ticket.Value.Account, ticket.Value.CharacterId, [new StoredItem(Saber, 1, -1), new StoredItem(NormalD, 1, -1)]));

        await using var actor = await EnchantPeer.ConnectAsync(host.Port);
        await actor.AuthAsync(ticket.Value);
        await actor.SendAsync(MsgType.EnchantRequest, new EnchantRequestMsg { ObjectId = 1 });
        Assert.Equal("world", (await actor.ExpectErrorAsync()).Code);

        var entered = await actor.EnterAsync();
        int saber = entered.Items.Single(item => item.ItemId == Saber).ObjectId;
        int scroll = entered.Items.Single(item => item.ItemId == NormalD).ObjectId;

        await actor.SendRawAsync((ushort)MsgType.EnchantConfirm, []);
        Assert.Equal("bad_request", (await actor.ExpectErrorAsync()).Code);

        await actor.SendAsync(MsgType.EnchantRequest, new EnchantRequestMsg { ObjectId = scroll });
        var opened = await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(EnchantProtocol.SelectItem, opened.Id);

        await actor.SendAsync(MsgType.EnchantConfirm, new EnchantConfirmMsg { ObjectId = saber });
        var result = await actor.ExpectAsync<EnchantResultMsg>(MsgType.EnchantResult);
        var update = await actor.ExpectAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate);
        var line = await actor.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(EnchantProtocol.ResultSuccess, result.Result);
        Assert.Equal(1, result.EnchantLevel);
        Assert.Equal(saber, result.ObjectId);
        Assert.Contains(update.Changes, row => row.ObjectId == saber && row.EnchantLevel == 1);
        Assert.Equal(EnchantProtocol.SuccessPlain, line.Id);
        Assert.Contains(host.Store.FindById(ticket.Value.Account, ticket.Value.CharacterId)!.Items!, item => item.ItemId == Saber && item.EnchantLevel == 1);
        Assert.DoesNotContain(host.Store.FindById(ticket.Value.Account, ticket.Value.CharacterId)!.Items!, item => item.ItemId == NormalD);
    }

    static (LoginService Login, ZoneWorld World, JsonAccountStore Store) Open(IEnchantRandom random, GameData? game = null)
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var store = JsonAccountStore.InMemory();
        var world = new ZoneWorld(new RangeAoi(1_000), game ?? GameData.LoadDefault(), accounts: store, enchantRandom: random);
        return (new LoginService(store, tokens), world, store);
    }

    static (LoginService Login, ZoneWorld World, JsonAccountStore Store) CustomWorld(IEnchantRandom random, ItemTemplate extra)
    {
        var data = GameData.LoadDefault();
        var templates = data.Items.Templates.ToList();
        templates.Add(extra);
        return Open(random, GameData.From(new ItemCatalog(templates), data.StarterKits));
    }

    static StoredItem Bag(int itemId, int count = 1) => new(itemId, count, -1);

    static async Task<EnchantSession> EnterAsync(
        LoginService login,
        ZoneWorld world,
        JsonAccountStore store,
        string account,
        string name,
        StoredItem[] items,
        int x = 0)
    {
        var ticket = login.Play(account, "pass1234", name, HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null, ticket.Message);
        if (items.Length > 0)
            Assert.True(store.TryReplaceItems(ticket.Value.Account, ticket.Value.CharacterId, items));
        Assert.True(store.TrySavePresence(ticket.Value.Account, ticket.Value.CharacterId, x, 0, 0, null));

        var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        stream.Position = 0;
        UserInfoMsg? user = null;
        ItemListMsg? list = null;
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            if (frame == null)
                break;
            if (frame.Value.Type == (ushort)MsgType.UserInfo)
                user = MessagePackSerializer.Deserialize<UserInfoMsg>(frame.Value.Body);
            else if (frame.Value.Type == (ushort)MsgType.ItemList)
                list = MessagePackSerializer.Deserialize<ItemListMsg>(frame.Value.Body);
        }

        Assert.NotNull(user);
        Assert.NotNull(list);
        stream.SetLength(0);
        return new EnchantSession(world, session, stream, ticket.Value, user, list);
    }

    sealed class EnchantSession : IAsyncDisposable
    {
        public EnchantSession(ZoneWorld world, ZoneSession session, MemoryStream stream, HandoffTicket ticket, UserInfoMsg user, ItemListMsg items)
        {
            World = world;
            Session = session;
            Stream = stream;
            Ticket = ticket;
            User = user;
            Items = items;
        }

        public ZoneWorld World { get; }
        public ZoneSession Session { get; }
        public MemoryStream Stream { get; }
        public HandoffTicket Ticket { get; }
        public UserInfoMsg User { get; }
        public ItemListMsg Items { get; }

        public int ObjectId(int itemId) => Items.Items.Single(item => item.ItemId == itemId).ObjectId;

        public int[] ObjectIds(int itemId) => Items.Items.Where(item => item.ItemId == itemId).Select(item => item.ObjectId).ToArray();

        public void ClearWire()
        {
            Stream.SetLength(0);
            Stream.Position = 0;
        }

        public Task<string?> Request(int objectId)
        {
            ClearWire();
            return World.TryEnchantRequestAsync(Session, new EnchantRequestMsg { ObjectId = objectId }, CancellationToken.None);
        }

        public Task<string?> Confirm(int objectId)
        {
            ClearWire();
            return World.TryEnchantConfirmAsync(Session, new EnchantConfirmMsg { ObjectId = objectId }, CancellationToken.None);
        }

        public Task<string?> Cancel()
        {
            ClearWire();
            return World.TryEnchantCancelAsync(Session, CancellationToken.None);
        }

        public async Task<List<(ushort Type, byte[] Body)>> FramesAsync()
        {
            Stream.Position = 0;
            var frames = new List<(ushort, byte[])>();
            while (true)
            {
                var frame = await FrameIO.ReadAsync(Stream);
                if (frame == null)
                    break;
                frames.Add(frame.Value);
            }

            return frames;
        }

        public async Task<T> ExpectAsync<T>(MsgType type)
        {
            var frames = await FramesAsync();
            var frame = Assert.Single(frames);
            Assert.Equal((ushort)type, frame.Type);
            return MessagePackSerializer.Deserialize<T>(frame.Body);
        }

        public async Task<CharInfoMsg> ExpectCharAsync()
        {
            var frames = await FramesAsync();
            var frame = Assert.Single(frames);
            Assert.Equal((ushort)MsgType.CharInfo, frame.Type);
            return MessagePackSerializer.Deserialize<CharInfoMsg>(frame.Body);
        }

        public async Task ExpectQuietAsync() => Assert.Empty(await FramesAsync());

        public async ValueTask DisposeAsync()
        {
            await World.LeaveAsync(Session, CancellationToken.None);
            await Stream.DisposeAsync();
        }
    }

    sealed class EnchantHost : IAsyncDisposable
    {
        readonly TcpListener _listener;
        readonly CancellationTokenSource _cts;
        readonly Task _accept;

        EnchantHost(TcpListener listener, CancellationTokenSource cts, Task accept, LoginService login, JsonAccountStore store)
        {
            _listener = listener;
            _cts = cts;
            _accept = accept;
            Login = login;
            Store = store;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public int Port { get; }
        public LoginService Login { get; }
        public JsonAccountStore Store { get; }

        public static async Task<EnchantHost> StartAsync()
        {
            var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
            var store = JsonAccountStore.InMemory();
            var world = new ZoneWorld(accounts: store, enchantRandom: new SequenceEnchantRandom(0.99));
            var server = new ZoneServer(tokens, world);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var cts = new CancellationTokenSource();
            var accept = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    TcpClient accepted;
                    try
                    {
                        accepted = await listener.AcceptTcpClientAsync(cts.Token);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                    {
                        break;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await using var stream = accepted.GetStream();
                            await server.HandleAsync(stream, cts.Token);
                        }
                        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                        {
                        }
                        finally
                        {
                            accepted.Dispose();
                        }
                    });
                }
            });
            await Task.Yield();
            return new EnchantHost(listener, cts, accept, new LoginService(store, tokens), store);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            try
            {
                await _accept.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
            }
        }
    }

    sealed class EnchantPeer : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;

        EnchantPeer(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public static async Task<EnchantPeer> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new EnchantPeer(client, client.GetStream());
        }

        public async Task AuthAsync(HandoffTicket ticket)
        {
            await SendAsync(MsgType.Hello, new HelloMsg());
            await ExpectAsync<HelloAckMsg>(MsgType.HelloAck);
            await SendAsync(MsgType.Auth, ticket.ToZoneAuth());
            await ExpectAsync<AuthOkMsg>(MsgType.AuthOk);
        }

        public async Task<ItemListMsg> EnterAsync()
        {
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg());
            await ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
            var items = await ExpectAsync<ItemListMsg>(MsgType.ItemList);
            await ExpectAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned);
            for (int i = 0; i < TalkingIsland.Gremlins.Length; i++)
                await ExpectAsync<NpcInfoMsg>(MsgType.NpcInfo);
            await ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
            return items;
        }

        public async Task SendAsync<T>(MsgType type, T message)
        {
            byte[] body = MessagePackSerializer.Serialize(message);
            await SendRawAsync((ushort)type, body);
        }

        public Task SendRawAsync(ushort type, byte[] body) => FrameIO.SendAsync(_stream, type, body);

        public async Task<T> ExpectAsync<T>(MsgType expected)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = await FrameIO.ReadAsync(_stream, cts.Token);
            Assert.NotNull(frame);
            if (frame!.Value.Type != (ushort)expected)
                Assert.Fail($"expected {expected}, got {(MsgType)frame.Value.Type}");
            return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
        }

        public Task<ErrorMsg> ExpectErrorAsync() => ExpectAsync<ErrorMsg>(MsgType.Error);

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
