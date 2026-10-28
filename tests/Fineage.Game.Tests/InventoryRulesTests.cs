using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public class InventoryRulesTests
{
    [Fact]
    public void Equipping_a_weapon_unequips_the_one_already_in_the_hand()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(0);
        var sword = inventory.GetEquipped(PaperdollSlot.RHand)!;
        var dagger = inventory.Items.Single(item => item.ItemId == 10);

        inventory.Equip(dagger);

        Assert.Equal(dagger, inventory.GetEquipped(PaperdollSlot.RHand));
        Assert.Equal(PaperdollSlot.RHand, dagger.EquippedSlot);
        Assert.Null(sword.EquippedSlot);
        Assert.Contains(sword, inventory.Items);
        Assert.Equal(1146, inventory.GetEquipped(PaperdollSlot.Chest)!.ItemId);
        Assert.Equal(1147, inventory.GetEquipped(PaperdollSlot.Legs)!.ItemId);
    }

    [Fact]
    public void Tutorial_guide_cannot_be_equipped()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(0);
        var guide = inventory.Items.Single(item => item.ItemId == 5588);
        var error = Assert.Throws<InvalidOperationException>(() => inventory.Equip(guide));
        Assert.Contains("5588", error.Message);
        Assert.False(guide.IsEquipped);
    }

    [Fact]
    public void Two_handed_weapon_clears_the_left_hand_and_a_one_handed_weapon_does_not()
    {
        var catalog = new ItemCatalog(
        [
            Piece(1, "Sword", BodySlot.RightHand, WeaponType.Sword),
            Piece(2, "Great Sword", BodySlot.LrHand, WeaponType.Sword),
            Shield(3)
        ]);
        var inventory = new PlayerInventory(catalog, firstObjectId: 1);
        var sword = inventory.Add(1);
        var great = inventory.Add(2);
        var shield = inventory.Add(3);

        inventory.Equip(sword);
        inventory.Equip(shield);
        Assert.Equal(shield, inventory.GetEquipped(PaperdollSlot.LHand));
        Assert.Equal(sword, inventory.GetEquipped(PaperdollSlot.RHand));

        inventory.Equip(great);
        Assert.Equal(great, inventory.GetEquipped(PaperdollSlot.RHand));
        Assert.Null(inventory.GetEquipped(PaperdollSlot.LHand));
        Assert.Null(shield.EquippedSlot);
        Assert.Null(sword.EquippedSlot);
        Assert.Contains(shield, inventory.Items);
    }

    [Fact]
    public void Shield_replaces_a_two_handed_weapon_but_arrows_stay_with_a_bow()
    {
        var catalog = new ItemCatalog(
        [
            Piece(2, "Great Sword", BodySlot.LrHand, WeaponType.Sword),
            Piece(14, "Bow", BodySlot.LrHand, WeaponType.Bow),
            Shield(3),
            new ItemTemplate(17, "Wooden Arrow", ItemKind.Etc, BodySlot.LeftHand, "icon.test", EtcType: EtcItemType.Arrow, Stackable: true)
        ]);
        var inventory = new PlayerInventory(catalog, firstObjectId: 1);
        var great = inventory.Add(2);
        var bow = inventory.Add(14);
        var shield = inventory.Add(3);
        var arrows = inventory.Add(17, 10);

        inventory.Equip(great);
        inventory.Equip(shield);
        Assert.Null(great.EquippedSlot);
        Assert.Equal(shield, inventory.GetEquipped(PaperdollSlot.LHand));
        Assert.Null(inventory.GetEquipped(PaperdollSlot.RHand));

        inventory.Unequip(shield);
        inventory.Equip(bow);
        inventory.Equip(arrows);
        var moreArrows = inventory.Add(17, 5);
        Assert.Same(arrows, moreArrows);
        Assert.Equal(15, arrows.Count);
        Assert.Equal(bow, inventory.GetEquipped(PaperdollSlot.RHand));
        Assert.Equal(arrows, inventory.GetEquipped(PaperdollSlot.LHand));

        inventory.Unequip(bow);
        Assert.Null(bow.EquippedSlot);
        Assert.Null(arrows.EquippedSlot);
        Assert.Null(inventory.GetEquipped(PaperdollSlot.LHand));
    }

    [Fact]
    public void Full_armor_and_legs_displace_each_other()
    {
        var catalog = new ItemCatalog(
        [
            new ItemTemplate(23, "Wooden Breastplate", ItemKind.Armor, BodySlot.FullArmor, "icon.test", ArmorType: ArmorType.Light, PDef: 47),
            new ItemTemplate(28, "Pants", ItemKind.Armor, BodySlot.Legs, "icon.test", ArmorType: ArmorType.Light, PDef: 20),
            new ItemTemplate(1146, "Shirt", ItemKind.Armor, BodySlot.Chest, "icon.test", ArmorType: ArmorType.Light, PDef: 33)
        ]);
        var inventory = new PlayerInventory(catalog, firstObjectId: 1);
        var breastplate = inventory.Add(23);
        var pants = inventory.Add(28);
        var shirt = inventory.Add(1146);

        inventory.Equip(pants);
        inventory.Equip(breastplate);
        Assert.Equal(breastplate, inventory.GetEquipped(PaperdollSlot.Chest));
        Assert.Null(inventory.GetEquipped(PaperdollSlot.Legs));
        Assert.Null(pants.EquippedSlot);

        inventory.Equip(pants);
        Assert.Equal(pants, inventory.GetEquipped(PaperdollSlot.Legs));
        Assert.Null(breastplate.EquippedSlot);

        inventory.Equip(shirt);
        Assert.Equal(shirt, inventory.GetEquipped(PaperdollSlot.Chest));
        Assert.Equal(pants, inventory.GetEquipped(PaperdollSlot.Legs));
    }

    [Fact]
    public void Adena_stacks_merge_and_gear_does_not()
    {
        var inventory = new PlayerInventory(GameData.LoadDefault().Items, firstObjectId: 1);
        var first = inventory.Add(57, 100);
        var again = inventory.Add(57, 40);
        Assert.Same(first, again);
        Assert.Equal(140, first.Count);
        Assert.Single(inventory.Items);

        inventory.Add(57, int.MaxValue - 140);
        Assert.Throws<OverflowException>(() => inventory.Add(57, 1));

        var fresh = new PlayerInventory(GameData.LoadDefault().Items, firstObjectId: 1);
        var dagger = fresh.Add(10, 2);
        Assert.Equal(1, dagger.Count);
        Assert.Equal(2, fresh.Items.Count);
        Assert.Equal([1, 2], fresh.Items.Select(item => item.ObjectId).ToArray());
        Assert.Equal(10, fresh.FindByItemId(10)!.ItemId);
    }

    [Fact]
    public void Add_rejects_unknown_items_and_empty_counts()
    {
        var inventory = new PlayerInventory(GameData.LoadDefault().Items);
        Assert.Throws<ArgumentException>(() => inventory.Add(999999));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Add(57, 0));
        Assert.Throws<InvalidOperationException>(() => inventory.Equip(42));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.GetEquipped((PaperdollSlot)99));
    }

    [Fact]
    public void Hair_all_clears_the_second_hair_slot_and_formal_wear_blocks_legs()
    {
        var catalog = new ItemCatalog(
        [
            new ItemTemplate(1, "Hairpin", ItemKind.Armor, BodySlot.Hair2, "icon.test"),
            new ItemTemplate(2, "Circlet", ItemKind.Armor, BodySlot.HairAll, "icon.test"),
            new ItemTemplate(3, "Wedding Dress", ItemKind.Armor, BodySlot.AllDress, "icon.test"),
            new ItemTemplate(4, "Pants", ItemKind.Armor, BodySlot.Legs, "icon.test"),
            Piece(5, "Sword", BodySlot.RightHand, WeaponType.Sword)
        ]);
        var inventory = new PlayerInventory(catalog, firstObjectId: 1);
        var pin = inventory.Add(1);
        var circlet = inventory.Add(2);
        inventory.Equip(pin);
        inventory.Equip(circlet);
        Assert.Equal(circlet, inventory.GetEquipped(PaperdollSlot.Hair));
        Assert.Null(inventory.GetEquipped(PaperdollSlot.Hair2));
        Assert.Null(pin.EquippedSlot);

        var dress = inventory.Add(3);
        var pants = inventory.Add(4);
        var sword = inventory.Add(5);
        inventory.Equip(dress);
        Assert.Equal(dress, inventory.GetEquipped(PaperdollSlot.Chest));
        Assert.Throws<InvalidOperationException>(() => inventory.Equip(pants));
        Assert.False(pants.IsEquipped);

        inventory.Equip(sword);
        Assert.Null(dress.EquippedSlot);
        Assert.Equal(sword, inventory.GetEquipped(PaperdollSlot.RHand));
    }

    [Fact]
    public void Earrings_fill_the_empty_ear_then_replace_the_left()
    {
        var catalog = new ItemCatalog(
        [
            new ItemTemplate(1, "Earring", ItemKind.Armor, BodySlot.BothEars, "icon.test")
        ]);
        var inventory = new PlayerInventory(catalog, firstObjectId: 1);
        var left = inventory.Add(1);
        var right = inventory.Add(1);
        var third = inventory.Add(1);
        inventory.Equip(left);
        inventory.Equip(right);
        inventory.Equip(third);
        Assert.Equal(third, inventory.GetEquipped(PaperdollSlot.Lear));
        Assert.Equal(right, inventory.GetEquipped(PaperdollSlot.Rear));
        Assert.Null(left.EquippedSlot);
    }

    static ItemTemplate Piece(int id, string name, BodySlot slot, WeaponType weapon) =>
        new(id, name, ItemKind.Weapon, slot, "icon.test", weapon);

    static ItemTemplate Shield(int id) =>
        new(id, "Shield", ItemKind.Armor, BodySlot.LeftHand, "icon.test");
}
