using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public class StarterKitTests
{
    // Pinned to Mobius CT0 initialEquipment.xml (stats/players/initialEquipment.xml).
    static readonly (int ClassId, StarterKitEntry[] Entries)[] OfficialKits =
    [
        (0, Fighter()),
        (10, Mystic()),
        (18, Fighter()),
        (25, Mystic()),
        (31, Fighter()),
        (38, Mystic()),
        (44, OrcFighter()),
        (49, OrcMystic()),
        (53, Dwarf()),
    ];

    static StarterKitEntry[] Fighter() =>
    [
        new(2369, 1, true),  // Squire's Sword
        new(10, 1, false),   // Dagger
        new(1146, 1, true),  // Squire's Shirt
        new(1147, 1, true),  // Squire's Pants
        new(5588, 1, false), // Tutorial Guide
    ];

    static StarterKitEntry[] Mystic() =>
    [
        new(6, 1, true),     // Apprentice's Wand
        new(425, 1, true),   // Apprentice's Tunic
        new(461, 1, true),   // Apprentice's Stockings
        new(5588, 1, false),
    ];

    static StarterKitEntry[] OrcFighter() =>
    [
        new(2369, 1, false), // Squire's Sword stays in the bag
        new(2368, 1, true),  // Training Gloves
        new(1146, 1, true),
        new(1147, 1, true),
        new(5588, 1, false),
    ];

    static StarterKitEntry[] OrcMystic() =>
    [
        new(2368, 1, true),  // Training Gloves; no wand in the official table
        new(425, 1, true),
        new(461, 1, true),
        new(5588, 1, false),
    ];

    static StarterKitEntry[] Dwarf() =>
    [
        new(2370, 1, true),  // Guild Member's Club
        new(10, 1, false),
        new(1146, 1, true),
        new(1147, 1, true),
        new(5588, 1, false),
    ];

    public static IEnumerable<object[]> OfficialKitCases() =>
        OfficialKits.Select(k => new object[] { k.ClassId, k.Entries });

    [Fact]
    public void Datapack_exposes_mobius_starting_adena_default()
    {
        var data = GameData.LoadDefault();
        Assert.Equal(0, data.StarterKits.MobiusStartingAdena);
        Assert.Contains("starter-kits.json", data.StarterKits.SourcePath);
    }

    [Theory]
    [MemberData(nameof(OfficialKitCases))]
    public void Base_class_items_and_equipped_slots_match_mobius_initial_equipment(int classId, StarterKitEntry[] expected)
    {
        var data = GameData.LoadDefault();
        var kit = data.StarterKits.ForClass(classId);
        Assert.Equal(expected, kit.Entries);

        var inventory = data.CreateStarterInventory(classId);
        Assert.Equal(expected.Select(e => e.ItemId).ToArray(), inventory.Items.Select(item => item.ItemId).ToArray());
        Assert.Equal(PlayerInventory.FirstObjectId, inventory.Items[0].ObjectId);
        Assert.Equal(inventory.Items.Select(item => item.ObjectId).Distinct().Count(), inventory.Items.Count);

        foreach (var entry in expected)
        {
            var item = inventory.Items.Single(i => i.ItemId == entry.ItemId);
            Assert.Equal(entry.Count, item.Count);
            if (entry.Equipped)
            {
                Assert.NotNull(item.EquippedSlot);
                Assert.Equal(item, inventory.GetEquipped(item.EquippedSlot.Value));
            }
            else
            {
                Assert.Null(item.EquippedSlot);
            }
        }

        int weaponId = expected.Single(e => e.Equipped && data.Items.Get(e.ItemId).Kind == ItemKind.Weapon).ItemId;
        var weapon = inventory.GetEquipped(PaperdollSlot.RHand);
        Assert.NotNull(weapon);
        Assert.Equal(weaponId, weapon.ItemId);

        if (expected.Any(e => e.ItemId is 1146 or 425 && e.Equipped))
        {
            int chestId = expected.Single(e => e.ItemId is 1146 or 425).ItemId;
            int legsId = expected.Single(e => e.ItemId is 1147 or 461).ItemId;
            Assert.Equal(chestId, inventory.GetEquipped(PaperdollSlot.Chest)!.ItemId);
            Assert.Equal(legsId, inventory.GetEquipped(PaperdollSlot.Legs)!.ItemId);
        }
    }

    [Fact]
    public void Human_fighter_equips_squires_sword_and_keeps_dagger_and_guide_in_bag()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(0);
        Assert.Equal(2369, inventory.GetEquipped(PaperdollSlot.RHand)!.ItemId);
        Assert.Equal(1146, inventory.GetEquipped(PaperdollSlot.Chest)!.ItemId);
        Assert.Equal(1147, inventory.GetEquipped(PaperdollSlot.Legs)!.ItemId);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 10).EquippedSlot);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 5588).EquippedSlot);
        Assert.Null(inventory.GetEquipped(PaperdollSlot.LHand));
        Assert.Null(inventory.GetEquipped(PaperdollSlot.Head));
    }

    [Fact]
    public void Human_mystic_equips_apprentice_wand()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(10);
        Assert.Equal(6, inventory.GetEquipped(PaperdollSlot.RHand)!.ItemId);
        Assert.True(inventory.GetEquipped(PaperdollSlot.RHand)!.Template.MagicWeapon);
        Assert.Equal(425, inventory.GetEquipped(PaperdollSlot.Chest)!.ItemId);
        Assert.Equal(461, inventory.GetEquipped(PaperdollSlot.Legs)!.ItemId);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 5588).EquippedSlot);
    }

    [Fact]
    public void Orc_fighter_equips_training_gloves_and_keeps_squires_sword_in_bag()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(44);
        Assert.Equal(2368, inventory.GetEquipped(PaperdollSlot.RHand)!.ItemId);
        Assert.Equal(BodySlot.LrHand, inventory.GetEquipped(PaperdollSlot.RHand)!.Template.BodySlot);
        Assert.Equal(1146, inventory.GetEquipped(PaperdollSlot.Chest)!.ItemId);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 2369).EquippedSlot);
    }

    [Fact]
    public void Dwarf_fighter_equips_guild_members_club()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(53);
        Assert.Equal(2370, inventory.GetEquipped(PaperdollSlot.RHand)!.ItemId);
        Assert.Equal(1146, inventory.GetEquipped(PaperdollSlot.Chest)!.ItemId);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 10).EquippedSlot);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(19)]
    public void Missing_class_throws_naming_class_id_and_path(int classId)
    {
        var data = GameData.LoadDefault();
        var error = Assert.Throws<InvalidDataException>(() => data.StarterKits.ForClass(classId));
        Assert.Contains(classId.ToString(), error.Message);
        Assert.Contains("starter-kits.json", error.Message);

        var inventoryError = Assert.Throws<InvalidDataException>(() => data.CreateStarterInventory(classId));
        Assert.Contains(classId.ToString(), inventoryError.Message);
        Assert.Contains("starter-kits.json", inventoryError.Message);
    }

    [Fact]
    public void Custom_items_from_the_old_pack_are_not_granted()
    {
        int[] custom =
        [
            43, 26, 34, 219, 68, 1835, 1538, 4627, 1539, 728, 736, 737,
            44, 1101, 1104, 177, 3947, 257, 87
        ];
        var data = GameData.LoadDefault();
        foreach (var kit in data.StarterKits.All)
            Assert.DoesNotContain(kit.Entries, entry => custom.Contains(entry.ItemId));
    }

    [Fact]
    public void One_allocator_keeps_item_ids_unique_across_kits()
    {
        var data = GameData.LoadDefault();
        var ids = new ItemObjectIdAllocator();
        var fighter = data.CreateStarterInventory(0, ids);
        var mystic = data.CreateStarterInventory(10, ids);
        var objects = fighter.Items.Select(item => item.ObjectId).Concat(mystic.Items.Select(item => item.ObjectId)).ToArray();
        Assert.Equal(objects.Length, objects.Distinct().Count());
        Assert.Equal(PlayerInventory.FirstObjectId, objects.Min());
        Assert.DoesNotContain(objects, id => id is >= 50001 and <= 50003);
    }

    [Fact]
    public void Defined_kits_cover_every_base_class()
    {
        var data = GameData.LoadDefault();
        var ids = data.StarterKits.All.Select(kit => kit.ClassId).OrderBy(id => id).ToArray();
        Assert.Equal(data.Classes!.BaseClassIds, ids);
    }
}
