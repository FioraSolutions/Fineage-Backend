using Fineage.Game;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Interest;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Equip and unequip rules on the zone world, including the packets they emit.</summary>
public sealed class ZoneEquipRulesTests
{
    const int Sword = 1;
    const int Great = 2;
    const int Shield = 3;
    const int Bow = 4;
    const int Arrow = 5;
    const int Full = 6;
    const int Pants = 7;
    const int Shirt = 8;
    const int Earring = 9;
    const int Ring = 10;
    const int Under = 11;
    const int Head = 12;
    const int Gloves = 13;
    const int Feet = 14;
    const int Neck = 15;
    const int Guide = 16;
    const int Adena = 17;
    const int Dress = 18;
    const int Rod = 19;
    const int Lure = 20;

    [Fact]
    public async Task UseItem_equips_then_toggles_off_and_persists_before_leave()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(login, world, store, "gear", "Alden", [Bag(Sword)]);

        Assert.Null(await actor.Use(actor.ObjectId(Sword)));
        var (update, user) = await actor.ExpectGearAsync();
        var row = Assert.Single(update.Changes);
        Assert.Equal(InventoryChange.Modify, row.Change);
        Assert.Equal(actor.ObjectId(Sword), row.ObjectId);
        Assert.Equal(Sword, row.ItemId);
        Assert.Equal(1, row.Count);
        Assert.Equal(7, row.EquippedSlot);
        Assert.Equal("rhand", row.BodySlot);
        Assert.Equal("weapon", row.Kind);
        Assert.Equal("sword", row.WeaponType);
        Assert.Equal(17, user.Paperdoll.Length);
        Assert.Equal(actor.ObjectId(Sword), user.Paperdoll[7].ObjectId);
        Assert.Equal(Sword, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[8].ItemId);
        AssertEquipped(store, actor, Sword, 7);

        Assert.Null(await actor.Use(actor.ObjectId(Sword)));
        (update, user) = await actor.ExpectGearAsync();
        row = Assert.Single(update.Changes);
        Assert.Equal(InventoryChange.Modify, row.Change);
        Assert.Equal(-1, row.EquippedSlot);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[7].ObjectId);
        AssertEquipped(store, actor, Sword, -1);

        await world.LeaveAsync(actor.Session, CancellationToken.None);
        AssertEquipped(store, actor, Sword, -1);

        await using var again = await EnterAsync(login, world, store, "gear", "Alden", []);
        Assert.Equal(-1, again.Items.Items.Single(item => item.ItemId == Sword).EquippedSlot);
    }

    [Fact]
    public async Task Two_handed_weapon_frees_the_left_hand_and_a_shield_frees_it()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(login, world, store, "gear", "Alden", [Bag(Sword), Bag(Shield), Bag(Great)]);

        Assert.Null(await actor.Use(actor.ObjectId(Sword)));
        Assert.Null(await actor.Use(actor.ObjectId(Shield)));
        var (_, wearing) = await actor.ExpectGearAsync();
        Assert.Equal(Sword, wearing.Paperdoll[7].ItemId);
        Assert.Equal(Shield, wearing.Paperdoll[8].ItemId);

        Assert.Null(await actor.Use(actor.ObjectId(Great)));
        var (update, user) = await actor.ExpectGearAsync();
        AssertModify(update, actor.ObjectId(Great), 7);
        AssertModify(update, actor.ObjectId(Sword), -1);
        AssertModify(update, actor.ObjectId(Shield), -1);
        Assert.Equal(Great, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[8].ItemId);
        Assert.Equal(["lrhand"], update.Changes.Where(row => row.ObjectId == actor.ObjectId(Great)).Select(row => row.BodySlot));

        Assert.Null(await actor.Use(actor.ObjectId(Shield)));
        (update, user) = await actor.ExpectGearAsync();
        AssertModify(update, actor.ObjectId(Great), -1);
        AssertModify(update, actor.ObjectId(Shield), 8);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
        Assert.Equal(Shield, user.Paperdoll[8].ItemId);
    }

    [Fact]
    public async Task Arrows_stay_with_a_bow_and_drop_when_the_bow_is_unequipped()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(login, world, store, "gear", "Alden", [Bag(Bow), Stack(Arrow, 20), Bag(Shield)]);

        Assert.Null(await actor.Use(actor.ObjectId(Bow)));
        Assert.Null(await actor.Use(actor.ObjectId(Arrow)));
        var (update, user) = await actor.ExpectGearAsync();
        var arrows = Assert.Single(update.Changes);
        Assert.Equal(8, arrows.EquippedSlot);
        Assert.Equal(20, arrows.Count);
        Assert.Equal("lhand", arrows.BodySlot);
        Assert.Equal(Bow, user.Paperdoll[7].ItemId);
        Assert.Equal(Arrow, user.Paperdoll[8].ItemId);

        Assert.Null(await actor.Unequip(8));
        (update, user) = await actor.ExpectGearAsync();
        AssertModify(update, actor.ObjectId(Arrow), -1);
        Assert.Equal(Bow, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[8].ItemId);

        Assert.Null(await actor.Use(actor.ObjectId(Arrow)));
        Assert.Null(await actor.Use(actor.ObjectId(Bow)));
        (update, user) = await actor.ExpectGearAsync();
        AssertModify(update, actor.ObjectId(Bow), -1);
        AssertModify(update, actor.ObjectId(Arrow), -1);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[8].ItemId);

        Assert.Null(await actor.Use(actor.ObjectId(Bow)));
        Assert.Null(await actor.Use(actor.ObjectId(Arrow)));
        Assert.Null(await actor.Use(actor.ObjectId(Shield)));
        (update, user) = await actor.ExpectGearAsync();
        AssertModify(update, actor.ObjectId(Bow), -1);
        AssertModify(update, actor.ObjectId(Arrow), -1);
        AssertModify(update, actor.ObjectId(Shield), 8);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
        Assert.Equal(Shield, user.Paperdoll[8].ItemId);
    }

    [Fact]
    public async Task Lure_stays_with_a_fishing_rod()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(login, world, store, "gear", "Alden", [Bag(Rod), Stack(Lure, 3)]);

        Assert.Null(await actor.Use(actor.ObjectId(Rod)));
        Assert.Null(await actor.Use(actor.ObjectId(Lure)));
        var (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(Rod, user.Paperdoll[7].ItemId);
        Assert.Equal(Lure, user.Paperdoll[8].ItemId);
    }

    [Fact]
    public async Task Full_armor_frees_legs_and_legs_are_blocked_until_it_comes_off()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(login, world, store, "gear", "Alden", [Bag(Pants), Bag(Full), Bag(Shirt)]);

        Assert.Null(await actor.Use(actor.ObjectId(Pants)));
        Assert.Null(await actor.Use(actor.ObjectId(Full)));
        var (update, user) = await actor.ExpectGearAsync();
        AssertModify(update, actor.ObjectId(Pants), -1);
        AssertModify(update, actor.ObjectId(Full), 10);
        Assert.Equal(Full, user.Paperdoll[10].ItemId);
        Assert.Equal(0, user.Paperdoll[11].ItemId);
        Assert.Equal("fullarmor", update.Changes.Single(row => row.ObjectId == actor.ObjectId(Full)).BodySlot);

        Assert.Equal(InventoryProtocol.Blocked, await actor.Use(actor.ObjectId(Pants)));
        await actor.ExpectQuietAsync();
        AssertEquipped(store, actor, Full, 10);
        AssertEquipped(store, actor, Pants, -1);

        Assert.Null(await actor.Unequip(10));
        Assert.Null(await actor.Use(actor.ObjectId(Pants)));
        Assert.Null(await actor.Use(actor.ObjectId(Shirt)));
        (update, user) = await actor.ExpectGearAsync();
        AssertModify(update, actor.ObjectId(Shirt), 10);
        Assert.DoesNotContain(update.Changes, row => row.ObjectId == actor.ObjectId(Pants));
        Assert.Equal(Shirt, user.Paperdoll[10].ItemId);
        Assert.Equal(Pants, user.Paperdoll[11].ItemId);
    }

    [Fact]
    public async Task Formal_wear_blocks_legs_and_a_weapon_replaces_the_dress()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(login, world, store, "gear", "Alden", [Bag(Dress), Bag(Pants), Bag(Sword)]);

        Assert.Null(await actor.Use(actor.ObjectId(Dress)));
        Assert.Equal(InventoryProtocol.Blocked, await actor.Use(actor.ObjectId(Pants)));
        await actor.ExpectQuietAsync();

        Assert.Null(await actor.Use(actor.ObjectId(Sword)));
        var (update, user) = await actor.ExpectGearAsync();
        AssertModify(update, actor.ObjectId(Dress), -1);
        AssertModify(update, actor.ObjectId(Sword), 7);
        Assert.Equal(0, user.Paperdoll[10].ItemId);
        Assert.Equal(Sword, user.Paperdoll[7].ItemId);
    }

    [Fact]
    public async Task Earrings_and_rings_fill_the_open_slot_then_replace_the_left()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(
            login, world, store, "gear", "Alden",
            [Bag(Earring), Bag(Earring), Bag(Earring), Bag(Ring), Bag(Ring), Bag(Ring)]);

        int[] ears = actor.ObjectIds(Earring);
        int[] rings = actor.ObjectIds(Ring);

        Assert.Null(await actor.Use(ears[0]));
        Assert.Null(await actor.Use(ears[1]));
        var (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(ears[0], user.Paperdoll[1].ObjectId);
        Assert.Equal(ears[1], user.Paperdoll[2].ObjectId);

        Assert.Null(await actor.Use(ears[2]));
        var (update, replaced) = await actor.ExpectGearAsync();
        AssertModify(update, ears[2], 1);
        AssertModify(update, ears[0], -1);
        Assert.DoesNotContain(update.Changes, row => row.ObjectId == ears[1]);
        Assert.Equal(ears[2], replaced.Paperdoll[1].ObjectId);
        Assert.Equal(ears[1], replaced.Paperdoll[2].ObjectId);

        Assert.Null(await actor.Use(rings[0]));
        Assert.Null(await actor.Use(rings[1]));
        Assert.Null(await actor.Use(rings[2]));
        (update, user) = await actor.ExpectGearAsync();
        AssertModify(update, rings[2], 4);
        AssertModify(update, rings[0], -1);
        Assert.Equal(rings[2], user.Paperdoll[4].ObjectId);
        Assert.Equal(rings[1], user.Paperdoll[5].ObjectId);
    }

    [Fact]
    public async Task Underwear_head_gloves_feet_and_neck_map_onto_their_paperdoll_slots()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(
            login, world, store, "gear", "Alden",
            [Bag(Under), Bag(Neck), Bag(Head), Bag(Gloves), Bag(Feet)]);

        await ExpectSlot(actor, Under, 0);
        await ExpectSlot(actor, Neck, 3);
        await ExpectSlot(actor, Head, 6);
        await ExpectSlot(actor, Gloves, 9);
        await ExpectSlot(actor, Feet, 12);
    }

    [Fact]
    public async Task Rejects_unknown_items_non_equippable_items_and_a_dead_player()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(login, world, store, "gear", "Alden", [Bag(Sword), Bag(Guide), Stack(Adena, 5)]);
        await using var other = await EnterAsync(login, world, store, "gearb", "Bram", [Bag(Shield)], x: 0);

        Assert.Equal(InventoryProtocol.NotOwned, await actor.Use(1));
        Assert.Equal(InventoryProtocol.NotOwned, await actor.Use(other.ObjectId(Shield)));
        await actor.ExpectQuietAsync();
        await other.ExpectQuietAsync();

        Assert.Equal(InventoryProtocol.NotEquippable, await actor.Use(actor.ObjectId(Guide)));
        Assert.Equal(InventoryProtocol.NotEquippable, await actor.Use(actor.ObjectId(Adena)));
        await actor.ExpectQuietAsync();

        Assert.Null(await actor.Use(actor.ObjectId(Sword)));
        Assert.True(world.TrySetPlayerDead(actor.Ticket.CharacterId, true));
        Assert.False(world.TrySetPlayerDead(999_999, true));
        Assert.Equal(InventoryProtocol.Dead, await actor.Use(actor.ObjectId(Sword)));
        Assert.Equal(InventoryProtocol.Dead, await actor.Unequip(7));
        await actor.ExpectQuietAsync();
        AssertEquipped(store, actor, Sword, 7);

        Assert.True(world.TrySetPlayerDead(actor.Ticket.CharacterId, false));
        Assert.Null(await actor.Unequip(7));
        var (_, user) = await actor.ExpectGearAsync();
        Assert.Equal(0, user.Paperdoll[7].ItemId);
    }

    [Fact]
    public async Task Unequip_rejects_a_bad_or_empty_slot()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(login, world, store, "gear", "Alden", [Bag(Sword)]);

        Assert.Equal(InventoryProtocol.BadSlot, await actor.Unequip(-1));
        Assert.Equal(InventoryProtocol.BadSlot, await actor.Unequip(17));
        Assert.Equal(InventoryProtocol.Empty, await actor.Unequip(7));
        await actor.ExpectQuietAsync();

        var idle = new ZoneSession(new MemoryStream());
        idle.Bind(actor.Ticket.CharacterId, actor.Ticket.Account, actor.Ticket.CharacterName, actor.Ticket.ClassId);
        Assert.Equal("world", await world.TryUseItemAsync(idle, new UseItemMsg { ObjectId = 1 }, CancellationToken.None));
        Assert.Equal("world", await world.TryUnequipItemAsync(idle, new UnequipItemMsg { PaperdollSlot = 0 }, CancellationToken.None));
    }

    [Fact]
    public async Task CharInfo_goes_to_known_players_only()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(login, world, store, "gear", "Alden", [Bag(Sword)], x: 0);
        await using var near = await EnterAsync(login, world, store, "gearb", "Bram", [Bag(Shield)], x: 0);
        await using var far = await EnterAsync(login, world, store, "gearc", "Cira", [Bag(Head)], x: 50_000);

        actor.ClearWire();
        near.ClearWire();
        far.ClearWire();

        Assert.Null(await actor.Use(actor.ObjectId(Sword)));
        var (_, user) = await actor.ExpectGearAsync();
        var seen = await near.ExpectCharAsync();
        Assert.Equal(actor.Ticket.CharacterId, seen.ObjectId);
        Assert.Equal(actor.Ticket.CharacterName, seen.Name);
        Assert.Equal(17, seen.Paperdoll.Length);
        Assert.Equal(Sword, seen.Paperdoll[7]);
        Assert.Equal(0, seen.Paperdoll[8]);
        Assert.Equal(user.X, seen.X);
        Assert.Equal(user.Y, seen.Y);
        Assert.Equal(user.Z, seen.Z);
        await far.ExpectQuietAsync();
    }

    [Fact]
    public async Task GiveItem_adds_to_the_bag_stacks_and_persists()
    {
        var (login, world, store) = Open();
        await using var actor = await EnterAsync(login, world, store, "gear", "Alden", [Stack(Adena, 10)]);

        Assert.Equal("world", await world.TryGiveItemAsync(
            new ZoneSession(new MemoryStream()),
            new GiveItemMsg { ItemId = Adena, Count = 1 },
            CancellationToken.None));

        var unbound = new ZoneSession(new MemoryStream());
        unbound.Bind(50, "missing", "Nope", HumanClasses.Fighter);
        Assert.Equal("world", await world.TryGiveItemAsync(unbound, new GiveItemMsg { ItemId = Adena, Count = 1 }, CancellationToken.None));

        Assert.Equal("bad_request", await actor.Give(Adena, 0));
        Assert.Equal("bad_request", await actor.Give(999, 1));
        Assert.Equal("bad_request", await actor.Give(Sword, ZoneWorld.MaxGrantCount + 1));
        await actor.ExpectQuietAsync();

        Assert.Null(await actor.Give(Adena, 5));
        var frames = await actor.FramesAsync();
        var added = Assert.Single(frames);
        Assert.Equal((ushort)MsgType.InventoryUpdate, added.Type);
        var update = MessagePackSerializer.Deserialize<InventoryUpdateMsg>(added.Body);
        var row = Assert.Single(update.Changes);
        Assert.Equal(InventoryChange.Add, row.Change);
        Assert.Equal(Adena, row.ItemId);
        Assert.Equal(15, row.Count);
        Assert.Equal(-1, row.EquippedSlot);
        var saved = store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId);
        Assert.Equal(15, saved!.Items!.Single(item => item.ItemId == Adena).Count);

        Assert.Null(await actor.Give(Sword, 1));
        frames = await actor.FramesAsync();
        added = Assert.Single(frames);
        update = MessagePackSerializer.Deserialize<InventoryUpdateMsg>(added.Body);
        row = Assert.Single(update.Changes);
        Assert.Equal(InventoryChange.Add, row.Change);
        Assert.Equal(Sword, row.ItemId);
        Assert.Equal(-1, row.EquippedSlot);
        Assert.Contains(store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId)!.Items!, item => item.ItemId == Sword && item.EquippedSlot == -1);
    }

    static async Task ExpectSlot(GearSession actor, int itemId, int slot)
    {
        Assert.Null(await actor.Use(actor.ObjectId(itemId)));
        var (update, user) = await actor.ExpectGearAsync();
        AssertModify(update, actor.ObjectId(itemId), slot);
        Assert.Equal(itemId, user.Paperdoll[slot].ItemId);
        Assert.Equal(actor.ObjectId(itemId), user.Paperdoll[slot].ObjectId);
    }

    static void AssertModify(InventoryUpdateMsg update, int objectId, int slot)
    {
        Assert.Equal(update.Changes.Select(row => row.ObjectId).OrderBy(id => id).ToArray(), update.Changes.Select(row => row.ObjectId).ToArray());
        var row = Assert.Single(update.Changes, change => change.ObjectId == objectId);
        Assert.Equal(InventoryChange.Modify, row.Change);
        Assert.Equal(slot, row.EquippedSlot);
    }

    static void AssertEquipped(JsonAccountStore store, GearSession actor, int itemId, int slot)
    {
        var saved = store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId);
        Assert.Contains(saved!.Items!, item => item.ItemId == itemId && item.EquippedSlot == slot);
    }

    static (LoginService Login, ZoneWorld World, JsonAccountStore Store) Open()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var store = JsonAccountStore.InMemory();
        var world = new ZoneWorld(new RangeAoi(1_000), GearData(), accounts: store);
        return (new LoginService(store, tokens), world, store);
    }

    static async Task<GearSession> EnterAsync(
        LoginService login,
        ZoneWorld world,
        JsonAccountStore store,
        string account,
        string name,
        StoredItem[] items,
        int x = 0,
        int y = 0)
    {
        var ticket = login.Play(account, "pass1234", name, HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null, ticket.Message);
        if (items.Length > 0)
            Assert.True(store.TryReplaceItems(ticket.Value.Account, ticket.Value.CharacterId, items));
        Assert.True(store.TrySavePresence(ticket.Value.Account, ticket.Value.CharacterId, x, y, 0, null));

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
        return new GearSession(world, store, session, stream, ticket.Value, user, list);
    }

    static GameData GearData()
    {
        var items = new ItemCatalog(
        [
            Weapon(Sword, "Sword", BodySlot.RightHand, WeaponType.Sword),
            Weapon(Great, "Great Sword", BodySlot.LrHand, WeaponType.Sword),
            Armor(Shield, "Shield", BodySlot.LeftHand),
            Weapon(Bow, "Bow", BodySlot.LrHand, WeaponType.Bow),
            Etc(Arrow, "Arrow", BodySlot.LeftHand, EtcItemType.Arrow, stackable: true),
            Armor(Full, "Breastplate", BodySlot.FullArmor),
            Armor(Pants, "Pants", BodySlot.Legs),
            Armor(Shirt, "Shirt", BodySlot.Chest),
            Armor(Earring, "Earring", BodySlot.BothEars),
            Armor(Ring, "Ring", BodySlot.BothFingers),
            Armor(Under, "Underwear", BodySlot.Underwear),
            Armor(Head, "Helmet", BodySlot.Head),
            Armor(Gloves, "Gloves", BodySlot.Gloves),
            Armor(Feet, "Shoes", BodySlot.Feet),
            Armor(Neck, "Necklace", BodySlot.Neck),
            new ItemTemplate(Guide, "Guide", ItemKind.Etc, BodySlot.None, "icon.test"),
            new ItemTemplate(Adena, "Adena", ItemKind.Etc, BodySlot.None, "icon.test", Stackable: true),
            Armor(Dress, "Wedding Dress", BodySlot.AllDress),
            Weapon(Rod, "Rod", BodySlot.LrHand, WeaponType.FishingRod),
            Etc(Lure, "Lure", BodySlot.LeftHand, EtcItemType.Lure, stackable: true)
        ]);
        return GameData.From(items, new StarterKits([]));
    }

    static StoredItem Bag(int itemId) => new(itemId, 1, -1);

    static StoredItem Stack(int itemId, int count) => new(itemId, count, -1);

    static ItemTemplate Weapon(int id, string name, BodySlot slot, WeaponType type) =>
        new(id, name, ItemKind.Weapon, slot, "icon.test", type);

    static ItemTemplate Armor(int id, string name, BodySlot slot) =>
        new(id, name, ItemKind.Armor, slot, "icon.test");

    static ItemTemplate Etc(int id, string name, BodySlot slot, EtcItemType etc, bool stackable) =>
        new(id, name, ItemKind.Etc, slot, "icon.test", EtcType: etc, Stackable: stackable);

    sealed class GearSession : IAsyncDisposable
    {
        public GearSession(
            ZoneWorld world,
            JsonAccountStore store,
            ZoneSession session,
            MemoryStream stream,
            HandoffTicket ticket,
            UserInfoMsg user,
            ItemListMsg items)
        {
            World = world;
            Store = store;
            Session = session;
            Stream = stream;
            Ticket = ticket;
            User = user;
            Items = items;
        }

        public ZoneWorld World { get; }
        public JsonAccountStore Store { get; }
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

        public Task<string?> Use(int objectId)
        {
            ClearWire();
            return World.TryUseItemAsync(Session, new UseItemMsg { ObjectId = objectId }, CancellationToken.None);
        }

        public Task<string?> Unequip(int slot)
        {
            ClearWire();
            return World.TryUnequipItemAsync(Session, new UnequipItemMsg { PaperdollSlot = slot }, CancellationToken.None);
        }

        public Task<string?> Give(int itemId, int count)
        {
            ClearWire();
            return World.TryGiveItemAsync(Session, new GiveItemMsg { ItemId = itemId, Count = count }, CancellationToken.None);
        }

        public async Task<List<(ushort Type, byte[] Body)>> FramesAsync()
        {
            Stream.Position = 0;
            var frames = new List<(ushort Type, byte[] Body)>();
            while (true)
            {
                var frame = await FrameIO.ReadAsync(Stream);
                if (frame == null)
                    break;
                frames.Add(frame.Value);
            }

            return frames;
        }

        public async Task<(InventoryUpdateMsg Update, UserInfoMsg User)> ExpectGearAsync()
        {
            var frames = await FramesAsync();
            Assert.Equal(2, frames.Count);
            Assert.Equal((ushort)MsgType.InventoryUpdate, frames[0].Type);
            Assert.Equal((ushort)MsgType.UserInfo, frames[1].Type);
            return (
                MessagePackSerializer.Deserialize<InventoryUpdateMsg>(frames[0].Body),
                MessagePackSerializer.Deserialize<UserInfoMsg>(frames[1].Body));
        }

        public async Task<CharInfoMsg> ExpectCharAsync()
        {
            var frames = await FramesAsync();
            var frame = Assert.Single(frames);
            Assert.Equal((ushort)MsgType.CharInfo, frame.Type);
            return MessagePackSerializer.Deserialize<CharInfoMsg>(frame.Body);
        }

        public async Task ExpectQuietAsync()
        {
            Assert.Empty(await FramesAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await World.LeaveAsync(Session, CancellationToken.None);
            await Stream.DisposeAsync();
        }
    }
}
