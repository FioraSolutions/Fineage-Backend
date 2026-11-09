using Fineage.Game;
using Fineage.Zone.Combat;
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

    [Fact]
    public void Arcana_acumen_sa_raises_mAtkSpd_and_drops_on_unequip()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity
        {
            ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 94, Level = 80, Inventory = bag
        };
        var world = new ZoneWorld();
        player.KnownSkills[228] = 1; // Fast Spell Casting — same baseline as live Archmage
        int without = world.PlayerStats(player).MAtkSpd;

        var weapon = bag.Add(6608); // Arcana Mace - Acumen → SA 3047 L2 mAtkSpd ×1.15
        Assert.Equal(6608, weapon.ItemId);
        bag.Equip(weapon);
        Assert.NotNull(bag.GetEquipped(PaperdollSlot.RHand));
        int withSa = world.PlayerStats(player).MAtkSpd;
        Assert.Contains(3047, player.GearSkills.Keys);
        Assert.True(withSa > without, $"Acumen cast {withSa} should exceed bare {without}");

        bag.Unequip(bag.GetEquipped(PaperdollSlot.RHand)!);
        Assert.Equal(without, world.PlayerStats(player).MAtkSpd);
        Assert.DoesNotContain(3047, player.GearSkills.Keys);
    }

    [Fact]
    public void Magic_hit_time_scales_when_dark_crystal_set_breaks()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity
        {
            ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 94, Level = 80, Inventory = bag
        };
        var world = new ZoneWorld();
        player.KnownSkills[228] = 1;
        bag.Equip(bag.Add(2407));
        bag.Equip(bag.Add(5767));
        bag.Equip(bag.Add(5779));
        int incomplete = ZoneCombat.SkillTimeMs(4000, magic: true, world.PlayerStats(player).MAtkSpd, 300);

        bag.Equip(bag.Add(512));
        int complete = ZoneCombat.SkillTimeMs(4000, magic: true, world.PlayerStats(player).MAtkSpd, 300);
        Assert.True(complete < incomplete, $"DC set hit {complete}ms should be faster than incomplete {incomplete}ms");

        bag.Unequip(bag.GetEquipped(PaperdollSlot.Head)!);
        Assert.Equal(incomplete, ZoneCombat.SkillTimeMs(4000, magic: true, world.PlayerStats(player).MAtkSpd, 300));
    }
}
