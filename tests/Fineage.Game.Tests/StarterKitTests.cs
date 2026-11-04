using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public class StarterKitTests
{
    static readonly int[] FighterBagIds =
    [
        43, 26, 34, 219, 68, 1835, 1538, 4627, 1539, 728, 736, 737
    ];

    static readonly int[] MysticBagIds =
    [
        44, 1101, 1104, 177, 3947, 1538, 4627, 1539, 728, 736, 737
    ];

    [Fact]
    public void Human_fighter_matches_mobius_initial_equipment()
    {
        var data = GameData.LoadDefault();
        var kit = data.StarterKits.ForClass(0);
        Assert.Equal(
            [
                new StarterKitEntry(43, 1, true),
                new StarterKitEntry(26, 1, true),
                new StarterKitEntry(34, 1, true),
                new StarterKitEntry(219, 1, true),
                new StarterKitEntry(68, 1, false),
                new StarterKitEntry(1835, 5000, false),
                new StarterKitEntry(1538, 50, false),
                new StarterKitEntry(4627, 1, false),
                new StarterKitEntry(1539, 100, false),
                new StarterKitEntry(728, 100, false),
                new StarterKitEntry(736, 10, false),
                new StarterKitEntry(737, 10, false)
            ],
            kit.Entries);

        var inventory = data.CreateStarterInventory(0);
        Assert.Equal(FighterBagIds, inventory.Items.Select(item => item.ItemId).ToArray());
        Assert.Equal(PlayerInventory.FirstObjectId, inventory.Items[0].ObjectId);
        Assert.Equal(inventory.Items.Select(item => item.ObjectId).Distinct().Count(), inventory.Items.Count);

        Assert.Equal(219, inventory.GetEquipped(PaperdollSlot.RHand)!.ItemId);
        Assert.Equal(43, inventory.GetEquipped(PaperdollSlot.Head)!.ItemId);
        Assert.Equal(26, inventory.GetEquipped(PaperdollSlot.Chest)!.ItemId);
        Assert.Equal(34, inventory.GetEquipped(PaperdollSlot.Legs)!.ItemId);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 68).EquippedSlot);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 4627).EquippedSlot);
        Assert.Null(inventory.GetEquipped(PaperdollSlot.LHand));
    }

    [Fact]
    public void Human_mystic_matches_mobius_initial_equipment()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(10);
        Assert.Equal(MysticBagIds, inventory.Items.Select(item => item.ItemId).ToArray());
        Assert.Equal(177, inventory.GetEquipped(PaperdollSlot.RHand)!.ItemId);
        Assert.True(inventory.GetEquipped(PaperdollSlot.RHand)!.Template.MagicWeapon);
        Assert.Equal(BodySlot.LrHand, inventory.GetEquipped(PaperdollSlot.RHand)!.Template.BodySlot);
        Assert.Equal(44, inventory.GetEquipped(PaperdollSlot.Head)!.ItemId);
        Assert.Equal(1101, inventory.GetEquipped(PaperdollSlot.Chest)!.ItemId);
        Assert.Equal(1104, inventory.GetEquipped(PaperdollSlot.Legs)!.ItemId);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 4627).EquippedSlot);
    }

    [Fact]
    public void Orc_fighter_equips_viper_fang_and_keeps_falchion_in_bag()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(44);
        Assert.Equal(257, inventory.GetEquipped(PaperdollSlot.RHand)!.ItemId);
        Assert.Equal(BodySlot.LrHand, inventory.GetEquipped(PaperdollSlot.RHand)!.Template.BodySlot);
        Assert.Equal(26, inventory.GetEquipped(PaperdollSlot.Chest)!.ItemId);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 68).EquippedSlot);
    }

    [Fact]
    public void Dwarf_fighter_equips_iron_hammer()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(53);
        Assert.Equal(87, inventory.GetEquipped(PaperdollSlot.RHand)!.ItemId);
        Assert.Equal(43, inventory.GetEquipped(PaperdollSlot.Head)!.ItemId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(19)]
    public void Non_base_class_gets_an_empty_kit(int classId)
    {
        var data = GameData.LoadDefault();
        var kit = data.StarterKits.ForClass(classId);
        Assert.Equal(classId, kit.ClassId);
        Assert.Empty(kit.Entries);

        var inventory = data.CreateStarterInventory(classId);
        Assert.Empty(inventory.Items);
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
        var catalog = GameData.LoadDefault().Classes!;
        var ids = GameData.LoadDefault().StarterKits.All.Select(kit => kit.ClassId).OrderBy(id => id).ToArray();
        Assert.Equal(catalog.BaseClassIds, ids);
    }
}
