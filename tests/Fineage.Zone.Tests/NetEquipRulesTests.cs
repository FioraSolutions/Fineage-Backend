using Fineage.Game;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Equip and unequip rules on the zone world: which paperdoll slot each item lands in, what a swap
/// displaces, and the bag/paperdoll rows the client receives. Ported off the V2 wire; the same rules are
/// asserted through <see cref="INetPresenceBridge"/>, which is what a real client renders.
/// </summary>
public sealed class NetEquipRulesTests
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

    static WorldBench Open() => new(GearData());

    [Fact]
    public async Task UseItem_equips_then_toggles_off_and_persists_before_leave()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gear", "Alden", [Bag(Sword)]);

        Assert.Null(await actor.Use(actor.Slot(Sword)));
        var (update, user) = LastGear(bench.Rec);
        var row = Assert.Single(update.Changes);
        Assert.Equal(InventoryChange.Modify, row.Change);
        Assert.Equal(actor.Slot(Sword), row.ObjectId);
        Assert.Equal(Sword, row.ItemId);
        Assert.Equal(1, row.Count);
        Assert.Equal(7, row.EquippedSlot);
        Assert.Equal(17, user.Paperdoll.Length);
        Assert.Equal(actor.Slot(Sword), user.Paperdoll[7].ObjectId);
        Assert.Equal(Sword, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[8].ItemId);
        Assert.Contains(actor.Saved(), item => item.ItemId == Sword && item.EquippedSlot == 7);

        Assert.Null(await actor.Use(actor.Slot(Sword)));
        (update, user) = LastGear(bench.Rec);
        row = Assert.Single(update.Changes);
        Assert.Equal(InventoryChange.Modify, row.Change);
        Assert.Equal(-1, row.EquippedSlot);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[7].ObjectId);
        Assert.Contains(actor.Saved(), item => item.ItemId == Sword && item.EquippedSlot == -1);

        await actor.World.LeaveAsync(actor.Session, CancellationToken.None);
        Assert.Contains(actor.Saved(), item => item.ItemId == Sword && item.EquippedSlot == -1);

        // Re-entering restores the persisted paperdoll state, not a fresh kit default.
        BenchPlayer again = await bench.EnterAsync("gear", "Alden", []);
        Assert.False(again.Inventory.Items.Single(item => item.ItemId == Sword).EquippedSlot.HasValue);
    }

    [Fact]
    public async Task Two_handed_weapon_frees_the_left_hand_and_a_shield_frees_it()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gear", "Alden", [Bag(Sword), Bag(Shield), Bag(Great)]);

        Assert.Null(await actor.Use(actor.Slot(Sword)));
        Assert.Null(await actor.Use(actor.Slot(Shield)));
        var (_, wearing) = LastGear(bench.Rec);
        Assert.Equal(Sword, wearing.Paperdoll[7].ItemId);
        Assert.Equal(Shield, wearing.Paperdoll[8].ItemId);

        Assert.Null(await actor.Use(actor.Slot(Great)));
        var (update, user) = LastGear(bench.Rec);
        AssertModify(update, actor.Slot(Great), 7);
        AssertModify(update, actor.Slot(Sword), -1);
        AssertModify(update, actor.Slot(Shield), -1);
        Assert.Equal(Great, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[8].ItemId);

        Assert.Null(await actor.Use(actor.Slot(Shield)));
        (update, user) = LastGear(bench.Rec);
        AssertModify(update, actor.Slot(Great), -1);
        AssertModify(update, actor.Slot(Shield), 8);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
        Assert.Equal(Shield, user.Paperdoll[8].ItemId);
    }

    [Fact]
    public async Task Arrows_stay_with_a_bow_and_drop_when_the_bow_is_unequipped()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gear", "Alden", [Bag(Bow), Stack(Arrow, 20), Bag(Shield)]);

        Assert.Null(await actor.Use(actor.Slot(Bow)));
        Assert.Null(await actor.Use(actor.Slot(Arrow)));
        var (update, user) = LastGear(bench.Rec);
        var arrows = Assert.Single(update.Changes);
        Assert.Equal(8, arrows.EquippedSlot);
        Assert.Equal(20, arrows.Count);
        Assert.Equal(Bow, user.Paperdoll[7].ItemId);
        Assert.Equal(Arrow, user.Paperdoll[8].ItemId);

        Assert.Null(await actor.Unequip(8));
        (update, user) = LastGear(bench.Rec);
        AssertModify(update, actor.Slot(Arrow), -1);
        Assert.Equal(Bow, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[8].ItemId);

        Assert.Null(await actor.Use(actor.Slot(Arrow)));
        Assert.Null(await actor.Use(actor.Slot(Bow)));
        (update, user) = LastGear(bench.Rec);
        AssertModify(update, actor.Slot(Bow), -1);
        AssertModify(update, actor.Slot(Arrow), -1);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
        Assert.Equal(0, user.Paperdoll[8].ItemId);

        Assert.Null(await actor.Use(actor.Slot(Bow)));
        Assert.Null(await actor.Use(actor.Slot(Arrow)));
        Assert.Null(await actor.Use(actor.Slot(Shield)));
        (update, user) = LastGear(bench.Rec);
        AssertModify(update, actor.Slot(Bow), -1);
        AssertModify(update, actor.Slot(Arrow), -1);
        AssertModify(update, actor.Slot(Shield), 8);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
        Assert.Equal(Shield, user.Paperdoll[8].ItemId);
    }

    [Fact]
    public async Task Lure_stays_with_a_fishing_rod()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gear", "Alden", [Bag(Rod), Stack(Lure, 3)]);

        Assert.Null(await actor.Use(actor.Slot(Rod)));
        Assert.Null(await actor.Use(actor.Slot(Lure)));
        var (_, user) = LastGear(bench.Rec);
        Assert.Equal(Rod, user.Paperdoll[7].ItemId);
        Assert.Equal(Lure, user.Paperdoll[8].ItemId);
    }

    [Fact]
    public async Task Full_armor_frees_legs_and_legs_are_blocked_until_it_comes_off()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gear", "Alden", [Bag(Pants), Bag(Full), Bag(Shirt)]);

        Assert.Null(await actor.Use(actor.Slot(Pants)));
        Assert.Null(await actor.Use(actor.Slot(Full)));
        var (update, user) = LastGear(bench.Rec);
        AssertModify(update, actor.Slot(Pants), -1);
        AssertModify(update, actor.Slot(Full), 10);
        Assert.Equal(Full, user.Paperdoll[10].ItemId);
        Assert.Equal(0, user.Paperdoll[11].ItemId);

        Assert.Equal(InventoryProtocol.Blocked, await actor.Use(actor.Slot(Pants)));
        Assert.Contains(actor.Saved(), item => item.ItemId == Full && item.EquippedSlot == 10);
        Assert.Contains(actor.Saved(), item => item.ItemId == Pants && item.EquippedSlot == -1);

        Assert.Null(await actor.Unequip(10));
        Assert.Null(await actor.Use(actor.Slot(Pants)));
        Assert.Null(await actor.Use(actor.Slot(Shirt)));
        (update, user) = LastGear(bench.Rec);
        AssertModify(update, actor.Slot(Shirt), 10);
        Assert.DoesNotContain(update.Changes, row => row.ObjectId == actor.Slot(Pants));
        Assert.Equal(Shirt, user.Paperdoll[10].ItemId);
        Assert.Equal(Pants, user.Paperdoll[11].ItemId);
    }

    [Fact]
    public async Task Formal_wear_blocks_legs_and_a_weapon_replaces_the_dress()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gear", "Alden", [Bag(Dress), Bag(Pants), Bag(Sword)]);

        Assert.Null(await actor.Use(actor.Slot(Dress)));
        Assert.Equal(InventoryProtocol.Blocked, await actor.Use(actor.Slot(Pants)));

        Assert.Null(await actor.Use(actor.Slot(Sword)));
        var (update, user) = LastGear(bench.Rec);
        AssertModify(update, actor.Slot(Dress), -1);
        AssertModify(update, actor.Slot(Sword), 7);
        Assert.Equal(0, user.Paperdoll[10].ItemId);
        Assert.Equal(Sword, user.Paperdoll[7].ItemId);
    }

    [Fact]
    public async Task Earrings_and_rings_fill_the_open_slot_then_replace_the_left()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync(
            "gear", "Alden",
            [Bag(Earring), Bag(Earring), Bag(Earring), Bag(Ring), Bag(Ring), Bag(Ring)]);

        int[] ears = actor.Slots(Earring);
        int[] rings = actor.Slots(Ring);

        Assert.Null(await actor.Use(ears[0]));
        Assert.Null(await actor.Use(ears[1]));
        var (_, user) = LastGear(bench.Rec);
        Assert.Equal(ears[0], user.Paperdoll[1].ObjectId);
        Assert.Equal(ears[1], user.Paperdoll[2].ObjectId);

        Assert.Null(await actor.Use(ears[2]));
        var (update, replaced) = LastGear(bench.Rec);
        AssertModify(update, ears[2], 1);
        AssertModify(update, ears[0], -1);
        Assert.DoesNotContain(update.Changes, row => row.ObjectId == ears[1]);
        Assert.Equal(ears[2], replaced.Paperdoll[1].ObjectId);
        Assert.Equal(ears[1], replaced.Paperdoll[2].ObjectId);

        Assert.Null(await actor.Use(rings[0]));
        Assert.Null(await actor.Use(rings[1]));
        Assert.Null(await actor.Use(rings[2]));
        (update, user) = LastGear(bench.Rec);
        AssertModify(update, rings[2], 4);
        AssertModify(update, rings[0], -1);
        Assert.Equal(rings[2], user.Paperdoll[4].ObjectId);
        Assert.Equal(rings[1], user.Paperdoll[5].ObjectId);
    }

    [Fact]
    public async Task Underwear_head_gloves_feet_and_neck_map_onto_their_paperdoll_slots()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync(
            "gear", "Alden",
            [Bag(Under), Bag(Neck), Bag(Head), Bag(Gloves), Bag(Feet)]);

        await ExpectSlot(bench, actor, Under, 0);
        await ExpectSlot(bench, actor, Neck, 3);
        await ExpectSlot(bench, actor, Head, 6);
        await ExpectSlot(bench, actor, Gloves, 9);
        await ExpectSlot(bench, actor, Feet, 12);
    }

    [Fact]
    public async Task Rejects_unknown_items_non_equippable_items_and_a_dead_player()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gear", "Alden", [Bag(Sword), Bag(Guide), Stack(Adena, 5)]);
        BenchPlayer other = await bench.EnterAsync("gearb", "Bram", [Bag(Shield)]);

        Assert.Equal(InventoryProtocol.NotOwned, await actor.Use(1));
        Assert.Equal(InventoryProtocol.NotOwned, await actor.Use(other.Slot(Shield)));
        Assert.Equal(InventoryProtocol.NotEquippable, await actor.Use(actor.Slot(Guide)));
        Assert.Equal(InventoryProtocol.NotEquippable, await actor.Use(actor.Slot(Adena)));

        Assert.Null(await actor.Use(actor.Slot(Sword)));
        Assert.True(bench.World.TrySetPlayerDead(actor.Ticket.CharacterId, true));
        Assert.False(bench.World.TrySetPlayerDead(999_999, true));
        Assert.Equal(InventoryProtocol.Dead, await actor.Use(actor.Slot(Sword)));
        Assert.Equal(InventoryProtocol.Dead, await actor.Unequip(7));
        Assert.Contains(actor.Saved(), item => item.ItemId == Sword && item.EquippedSlot == 7);

        Assert.True(bench.World.TrySetPlayerDead(actor.Ticket.CharacterId, false));
        Assert.Null(await actor.Unequip(7));
        var (_, user) = LastGear(bench.Rec);
        Assert.Equal(0, user.Paperdoll[7].ItemId);
    }

    [Fact]
    public async Task Unequip_rejects_a_bad_or_empty_slot()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gear", "Alden", [Bag(Sword)]);

        Assert.Equal(InventoryProtocol.BadSlot, await actor.Unequip(-1));
        Assert.Equal(InventoryProtocol.BadSlot, await actor.Unequip(17));
        Assert.Equal(InventoryProtocol.Empty, await actor.Unequip(7));

        var idle = new ZoneSession();
        idle.Bind(actor.Ticket.CharacterId, actor.Ticket.Account, actor.Ticket.CharacterName, actor.Ticket.ClassId);
        Assert.Equal("world", await bench.World.TryUseItemAsync(idle, new UseItemMsg { ObjectId = 1 }, CancellationToken.None));
        Assert.Equal("world", await bench.World.TryUnequipItemAsync(idle, new UnequipItemMsg { PaperdollSlot = 0 }, CancellationToken.None));
    }

    [Fact]
    public async Task CharInfo_goes_to_known_players_only()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gear", "Alden", [Bag(Sword)]);
        BenchPlayer near = await bench.EnterAsync("gearb", "Bram", [Bag(Shield)]);
        BenchPlayer far = await bench.EnterAsync("gearc", "Cira", [Bag(Head)], x: 50_000);
        bench.Rec.Calls.Clear();

        Assert.Null(await actor.Use(actor.Slot(Sword)));
        var (_, user) = LastGear(bench.Rec);

        var charInfo = bench.Rec.Of(nameof(INetPresenceBridge.FanoutCharInfo));
        Assert.NotEmpty(charInfo);
        Assert.Contains(charInfo, args => args[0] is IReadOnlyList<int> watchers && watchers.Contains(near.ObjectId));
        Assert.DoesNotContain(charInfo, args => args[0] is IReadOnlyList<int> watchers && watchers.Contains(far.ObjectId));
        Assert.Equal(Sword, user.Paperdoll[7].ItemId);
    }

    [Fact]
    public async Task GiveItem_adds_to_the_bag_stacks_and_persists()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gear", "Alden", [Stack(Adena, 10)]);

        Assert.Equal("world", await bench.World.TryGiveItemAsync(
            new ZoneSession(),
            new GiveItemMsg { ItemId = Adena, Count = 1 },
            CancellationToken.None));

        var unbound = new ZoneSession();
        unbound.Bind(50, "missing", "Nope", HumanClasses.Fighter);
        Assert.Equal("world", await bench.World.TryGiveItemAsync(unbound, new GiveItemMsg { ItemId = Adena, Count = 1 }, CancellationToken.None));

        Assert.Equal("bad_request", await Give(actor, Adena, 0));
        Assert.Equal("bad_request", await Give(actor, 999, 1));
        Assert.Equal("bad_request", await Give(actor, Sword, ZoneWorld.MaxGrantCount + 1));

        Assert.Null(await Give(actor, Adena, 5));
        var added = LastInventory(bench.Rec);
        var row = Assert.Single(added.Changes);
        Assert.Equal(InventoryChange.Add, row.Change);
        Assert.Equal(Adena, row.ItemId);
        Assert.Equal(15, row.Count);
        Assert.Equal(-1, row.EquippedSlot);
        Assert.Equal(15, actor.Saved().Single(item => item.ItemId == Adena).Count);

        Assert.Null(await Give(actor, Sword, 1));
        added = LastInventory(bench.Rec);
        row = Assert.Single(added.Changes);
        Assert.Equal(InventoryChange.Add, row.Change);
        Assert.Equal(Sword, row.ItemId);
        Assert.Equal(-1, row.EquippedSlot);
        Assert.Contains(actor.Saved(), item => item.ItemId == Sword && item.EquippedSlot == -1);
    }

    static Task<string?> Give(BenchPlayer actor, int itemId, int count) =>
        actor.World.TryGiveItemAsync(actor.Session, new GiveItemMsg { ItemId = itemId, Count = count }, CancellationToken.None);

    static async Task ExpectSlot(WorldBench bench, BenchPlayer actor, int itemId, int slot)
    {
        Assert.Null(await actor.Use(actor.Slot(itemId)));
        var (update, user) = LastGear(bench.Rec);
        AssertModify(update, actor.Slot(itemId), slot);
        Assert.Equal(itemId, user.Paperdoll[slot].ItemId);
        Assert.Equal(actor.Slot(itemId), user.Paperdoll[slot].ObjectId);
    }

    static void AssertModify(InventoryUpdateNetMsg update, int objectId, int slot)
    {
        var row = Assert.Single(update.Changes, change => change.ObjectId == objectId);
        Assert.Equal(InventoryChange.Modify, row.Change);
        Assert.Equal(slot, row.EquippedSlot);
    }

    /// <summary>The bag update and the owner's UserInfo a gear change emits, in that order.</summary>
    static (InventoryUpdateNetMsg Update, UserInfoNetMsg User) LastGear(NetBridgeRecorder rec) =>
        (LastInventory(rec), LastUserInfo(rec));

    static InventoryUpdateNetMsg LastInventory(NetBridgeRecorder rec) =>
        Assert.IsType<InventoryUpdateNetMsg>(
            rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate)).Last()[1]);

    static UserInfoNetMsg LastUserInfo(NetBridgeRecorder rec) =>
        Assert.IsType<UserInfoNetMsg>(
            rec.Of(nameof(INetPresenceBridge.FanoutUserInfo)).Last()[1]);

    static StoredItem Bag(int itemId) => new(itemId, 1, -1);

    static StoredItem Stack(int itemId, int count) => new(itemId, count, -1);

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

    static ItemTemplate Weapon(int id, string name, BodySlot slot, WeaponType type) =>
        new(id, name, ItemKind.Weapon, slot, "icon.test", type);

    static ItemTemplate Armor(int id, string name, BodySlot slot) =>
        new(id, name, ItemKind.Armor, slot, "icon.test");

    static ItemTemplate Etc(int id, string name, BodySlot slot, EtcItemType etc, bool stackable) =>
        new(id, name, ItemKind.Etc, slot, "icon.test", EtcType: etc, Stackable: stackable);
}
