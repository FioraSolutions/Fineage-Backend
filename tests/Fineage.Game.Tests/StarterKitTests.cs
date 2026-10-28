using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public class StarterKitTests
{
    [Fact]
    public void Human_fighter_wears_sword_shirt_and_pants()
    {
        var data = GameData.LoadDefault();
        var kit = data.StarterKits.ForClass(0);
        Assert.Equal(
            [
                new StarterKitEntry(2369, 1, true),
                new StarterKitEntry(10, 1, false),
                new StarterKitEntry(1146, 1, true),
                new StarterKitEntry(1147, 1, true),
                new StarterKitEntry(5588, 1, false)
            ],
            kit.Entries);

        var inventory = data.CreateStarterInventory(0);
        Assert.Equal([2369, 10, 1146, 1147, 5588], inventory.Items.Select(item => item.ItemId).ToArray());
        Assert.Equal(PlayerInventory.FirstObjectId, inventory.Items[0].ObjectId);
        Assert.Equal(inventory.Items.Select(item => item.ObjectId).Distinct().Count(), inventory.Items.Count);

        var sword = inventory.GetEquipped(PaperdollSlot.RHand);
        var shirt = inventory.GetEquipped(PaperdollSlot.Chest);
        var pants = inventory.GetEquipped(PaperdollSlot.Legs);
        Assert.Equal(2369, sword!.ItemId);
        Assert.Equal(PaperdollSlot.RHand, sword.EquippedSlot);
        Assert.Equal(1146, shirt!.ItemId);
        Assert.Equal(1147, pants!.ItemId);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 10).EquippedSlot);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 5588).EquippedSlot);
        Assert.Null(inventory.GetEquipped(PaperdollSlot.LHand));
    }

    [Fact]
    public void Human_mystic_wears_wand_tunic_and_stockings()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(10);
        Assert.Equal([6, 425, 461, 5588], inventory.Items.Select(item => item.ItemId).ToArray());
        Assert.Equal(6, inventory.GetEquipped(PaperdollSlot.RHand)!.ItemId);
        Assert.True(inventory.GetEquipped(PaperdollSlot.RHand)!.Template.MagicWeapon);
        Assert.Equal(425, inventory.GetEquipped(PaperdollSlot.Chest)!.ItemId);
        Assert.Equal(17, inventory.GetEquipped(PaperdollSlot.Chest)!.Template.PDef);
        Assert.Equal(461, inventory.GetEquipped(PaperdollSlot.Legs)!.ItemId);
        Assert.Equal(10, inventory.GetEquipped(PaperdollSlot.Legs)!.Template.PDef);
        Assert.Null(inventory.Items.Single(item => item.ItemId == 5588).EquippedSlot);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(18)]
    [InlineData(44)]
    public void Unknown_class_gets_an_empty_kit(int classId)
    {
        var data = GameData.LoadDefault();
        var kit = data.StarterKits.ForClass(classId);
        Assert.Equal(classId, kit.ClassId);
        Assert.Empty(kit.Entries);

        var inventory = data.CreateStarterInventory(classId);
        Assert.Empty(inventory.Items);
    }

    [Fact]
    public void Defined_kits_are_only_human_fighter_and_mystic()
    {
        var ids = GameData.LoadDefault().StarterKits.All.Select(kit => kit.ClassId).ToArray();
        Assert.Equal([0, 10], ids);
    }
}
