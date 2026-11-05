using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius stat pipeline: STR/DEX bonus, level mod, weapon set-stats, armor slot replacement.</summary>
public sealed class ZonePlayerStatsTests
{
    static (ZoneWorld World, ZoneEntity Player, PlayerInventory Bag) HumanFighter(int level = 1)
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = level, Inventory = bag };
        return (new ZoneWorld(), player, bag);
    }

    [Fact]
    public void Naked_level1_fighter_matches_mobius()
    {
        var (world, p, _) = HumanFighter();
        var s = world.PlayerStats(p);
        Assert.Equal(4, s.PAtk);    // 4 * STR40 1.2 * 0.90
        Assert.Equal(72, s.PDef);   // 80 * 0.90
        Assert.Equal(330, s.PAtkSpd); // 300 * DEX30 1.1
        Assert.Equal(ZoneCombat.MeleeRange, ZoneCombat.PlayerAttackReach(s.AttackRange));
    }

    [Fact]
    public void Starter_kit_weapon_and_armor_replace_base_stats()
    {
        var (world, p, bag) = HumanFighter();
        foreach (int id in new[] { 219, 43, 26, 34 }) // Sword Breaker, Wooden Helmet, Bronze Breastplate, Bronze Gaiters
            bag.Equip(bag.Add(id));
        var s = world.PlayerStats(p);
        Assert.Equal(29, s.PAtk);   // 27 * 1.2 * 0.9
        Assert.Equal(476, s.PAtkSpd); // 433 * 1.1
        Assert.Equal(134, s.PDef);  // (80 - 12 - 31 - 18 + 19 + 68 + 43) * 0.9
        Assert.Equal(1050, ZoneCombat.AttackTimeMs(s.PAtkSpd));
    }

    [Fact]
    public void Level_mod_and_bow_range()
    {
        var (world, p, bag) = HumanFighter(level: 20);
        Assert.Equal(5, world.PlayerStats(p).PAtk); // 4 * 1.2 * 1.09
        bag.Equip(bag.Add(14)); // Bow, attack_range 500
        var s = world.PlayerStats(p);
        Assert.Equal(WeaponType.Bow, s.Weapon);
        Assert.Equal(540, ZoneCombat.PlayerAttackReach(s.AttackRange));
    }
}
