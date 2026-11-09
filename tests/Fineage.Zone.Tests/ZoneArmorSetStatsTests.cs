using Fineage.Game;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius ArmorSetListener: complete sets fold via GearSkills into combat mods.</summary>
public sealed class ZoneArmorSetStatsTests
{
    [Fact]
    public void Wooden_set_complete_raises_pDef()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 1, Inventory = bag };
        var world = new ZoneWorld();

        bag.Equip(bag.Add(23)); // Wooden Breastplate (set chest)
        int without = world.PlayerStats(player).PDef;

        bag.Equip(bag.Add(2386)); // legs
        bag.Equip(bag.Add(43));   // wooden helmet (head)
        int withSet = world.PlayerStats(player).PDef;
        Assert.True(withSet > without, $"set PDef {withSet} should exceed incomplete {without}");
    }

    [Fact]
    public void Dark_Crystal_robe_set_raises_mAtkSpd_and_drops_when_head_removed()
    {
        // chest 2407 + head 512 + gloves 5767 + feet 5779 → skill 3535 (mAtkSpd ×1.15)
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity
        {
            ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 94, Level = 80, Inventory = bag
        };
        var world = new ZoneWorld();
        bag.Equip(bag.Add(2407));
        bag.Equip(bag.Add(5767));
        bag.Equip(bag.Add(5779));
        int without = world.PlayerStats(player).MAtkSpd;

        bag.Equip(bag.Add(512));
        int withSet = world.PlayerStats(player).MAtkSpd;
        Assert.True(withSet > without, $"complete set cast {withSet} should exceed incomplete {without}");

        bag.Unequip(bag.GetEquipped(PaperdollSlot.Head)!);
        Assert.Equal(without, world.PlayerStats(player).MAtkSpd);
    }

    [Fact]
    public void Major_Arcana_set_raises_mAtk_not_mAtkSpd()
    {
        // chest 6383 + head 6386 + gloves 6384 + feet 6385 → skill 3556 (mAtk ×1.17, no cast)
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity
        {
            ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 94, Level = 80, Inventory = bag
        };
        var world = new ZoneWorld();
        bag.Equip(bag.Add(6383));
        bag.Equip(bag.Add(6384));
        bag.Equip(bag.Add(6385));
        int castWithout = world.PlayerStats(player).MAtkSpd;
        int matkWithout = world.PlayerStats(player).MAtk;

        bag.Equip(bag.Add(6386));
        Assert.Equal(castWithout, world.PlayerStats(player).MAtkSpd);
        Assert.True(world.PlayerStats(player).MAtk > matkWithout);

        bag.Unequip(bag.GetEquipped(PaperdollSlot.Head)!);
        Assert.Equal(matkWithout, world.PlayerStats(player).MAtk);
    }
}
