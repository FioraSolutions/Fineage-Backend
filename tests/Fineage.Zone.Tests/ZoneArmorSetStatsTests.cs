using Fineage.Game;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius ArmorSetListener: complete wooden set folds skill 3500 pDef ×1.022.</summary>
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
}
