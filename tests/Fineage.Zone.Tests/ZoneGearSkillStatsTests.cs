using Fineage.Game;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Inventory item_skill (SA) folds through GearSkills into combat mods.</summary>
public sealed class ZoneGearSkillStatsTests
{
    [Fact]
    public void Forgotten_blade_focus_raises_crit_rate()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 1, Inventory = bag };
        var world = new ZoneWorld();

        int without = world.PlayerStats(player).CritRate;
        bag.Equip(bag.Add(6583)); // Forgotten Blade - Focus → SA Focus 3566 +84.3 critRate
        int withSa = world.PlayerStats(player).CritRate;
        Assert.True(withSa > without, $"Focus CritRate {withSa} should exceed bare {without}");
        Assert.Contains(3566, player.GearSkills.Keys);

        bag.Unequip(bag.GetEquipped(PaperdollSlot.RHand)!);
        int after = world.PlayerStats(player).CritRate;
        Assert.Equal(without, after);
        Assert.DoesNotContain(3566, player.GearSkills.Keys);
    }
}
