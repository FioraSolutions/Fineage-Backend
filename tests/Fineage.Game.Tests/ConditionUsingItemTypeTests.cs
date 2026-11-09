using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class ConditionUsingItemTypeTests
{
    static readonly ItemTemplate HeavyChest = new(26, "Bronze Breastplate", ItemKind.Armor, BodySlot.Chest, "icon", ArmorType: ArmorType.Heavy, PDef: 47);
    static readonly ItemTemplate HeavyLegs = new(34, "Bronze Gaiters", ItemKind.Armor, BodySlot.Legs, "icon", ArmorType: ArmorType.Heavy, PDef: 29);
    static readonly ItemTemplate FullLight = new(23, "Wooden Breastplate", ItemKind.Armor, BodySlot.FullArmor, "icon", ArmorType: ArmorType.Light, PDef: 47);
    static readonly ItemTemplate Sword = new(2, "Long Sword", ItemKind.Weapon, BodySlot.RightHand, "icon", WeaponType.Sword, PAtk: 24);
    static readonly ItemTemplate Shield = new(102, "Round Shield", ItemKind.Armor, BodySlot.LeftHand, "icon", ArmorType: ArmorType.None, ShieldDef: 47, ShieldRate: 20);

    [Fact]
    public void Heavy_needs_chest_and_legs_unless_fullbody()
    {
        Assert.False(ConditionUsingItemType.Matches(Sword, HeavyChest, null, null, ["HEAVY"]));
        Assert.True(ConditionUsingItemType.Matches(Sword, HeavyChest, HeavyLegs, null, ["HEAVY"]));
        Assert.True(ConditionUsingItemType.Matches(null, FullLight, null, null, ["LIGHT"]));
        Assert.False(ConditionUsingItemType.Matches(Sword, HeavyChest, HeavyLegs, null, ["LIGHT", "BOW"]));
    }

    [Fact]
    public void Weapon_and_shield_kinds()
    {
        Assert.True(ConditionUsingItemType.Matches(Sword, null, null, null, ["SWORD"]));
        Assert.False(ConditionUsingItemType.Matches(Sword, null, null, null, ["BOW"]));
        Assert.True(ConditionUsingItemType.Matches(Sword, null, null, Shield, ["SHIELD"]));
        Assert.False(ConditionUsingItemType.Matches(Sword, null, null, null, ["SHIELD"]));
    }
}
