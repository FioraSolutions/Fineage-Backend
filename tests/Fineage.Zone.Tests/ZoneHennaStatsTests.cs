using Fineage.Game;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius FuncHenna: dye deltas fold into base STR/DEX before the bonus table.</summary>
public sealed class ZoneHennaStatsTests
{
    [Fact]
    public void Str_henna_raises_pAtk()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        // ClassId 1 = Warrior: dye 1 lists it; Human Fighter (0) does not.
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 1, Level = 20, Inventory = bag };
        bag.Equip(bag.Add(219)); // Sword Breaker — fist base truncates STR+1 away
        var world = new ZoneWorld();
        int before = world.PlayerStats(player).PAtk;

        // dyeId 1: str+1 con-3 (from henna.json / hennaList.xml)
        Assert.True(world.TryWearHenna(player, 1, out int slot));
        Assert.Equal(0, slot);
        Assert.Equal(1, player.HennaStr);
        Assert.Equal(-3, player.HennaCon);
        // 27 * 1.20 * 1.09 = 35 → 27 * 1.24 * 1.09 = 36
        Assert.True(world.PlayerStats(player).PAtk > before);
    }

    [Fact]
    public void Henna_appears_on_userinfo_vitals()
    {
        var player = new ZoneEntity
        {
            ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 1, Level = 20,
            Inventory = new PlayerInventory(GameData.LoadDefault().Items)
        };
        var world = new ZoneWorld();
        world.TryWearHenna(player, 1, out _);
        var info = world.BuildUserInfo(player);
        // Warrior inherits Human Fighter bases: STR 40 + 1, CON 43 - 3
        Assert.Equal(41, info.Str);
        Assert.Equal(40, info.Con);
    }
}
