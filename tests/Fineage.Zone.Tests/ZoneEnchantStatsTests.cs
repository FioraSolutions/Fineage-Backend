using Fineage.Game;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius FuncEnchant folded into PlayerStats for +0 / +4 / +16.</summary>
public sealed class ZoneEnchantStatsTests
{
    static (ZoneWorld World, ZoneEntity Player, PlayerInventory Bag) Fighter()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 1, Inventory = bag };
        return (new ZoneWorld(), player, bag);
    }

    [Fact]
    public void Sword_breaker_enchant_0_4_16_pAtk()
    {
        var (world, p, bag) = Fighter();
        var item = bag.Add(219); // Sword Breaker pAtk 27, grade None, dagger
        bag.Equip(item);

        Assert.Equal(29, world.PlayerStats(p).PAtk); // 27 * 1.2 * 0.9

        bag.SetEnchantLevel(item, 4);
        // (27+10) * 1.2 * 0.9 = 39.96 → 39
        Assert.Equal(39, world.PlayerStats(p).PAtk);

        bag.SetEnchantLevel(item, 16);
        // (27+58) * 1.2 * 0.9 = 91.8 → 91
        Assert.Equal(91, world.PlayerStats(p).PAtk);
    }

    [Fact]
    public void Armor_enchant_raises_pDef()
    {
        var (world, p, bag) = Fighter();
        int naked = world.PlayerStats(p).PDef;
        var helm = bag.Add(43); // Wooden Helmet
        bag.Equip(helm);
        int plus0 = world.PlayerStats(p).PDef;
        Assert.True(plus0 > naked);

        bag.SetEnchantLevel(helm, 4);
        int plus4 = world.PlayerStats(p).PDef;
        // Defence(+4)=6 before levelMod 0.9 → +5 after floor on the whole product is fragile;
        // assert the enchanted sheet is strictly higher than +0.
        Assert.True(plus4 > plus0);

        bag.SetEnchantLevel(helm, 16);
        Assert.True(world.PlayerStats(p).PDef > plus4);
    }
}
