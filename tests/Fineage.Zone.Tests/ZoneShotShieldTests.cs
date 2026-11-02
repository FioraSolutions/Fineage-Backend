using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius soulshot ×2 P.Atk (flag 0x10) and calcShldUse block / perfect block (flag 0x40).</summary>
public sealed class ZoneShotShieldTests
{
    sealed class Scripted(params int[] values) : Random
    {
        int _i;
        public override int Next(int maxValue) => values[_i++];
        public override int Next(int minValue, int maxValue) => values[_i++];
    }

    [Fact]
    public void Soulshot_doubles_patk_and_sets_flag_with_grade()
    {
        // hit, no crit (0 > 999 false), spread 0
        var (dmg, flags) = ZoneCombat.RollMelee(new Scripted(0, 999, 0), 29, 44.444, 10, 10, 0, 10, true, 1, 0, 0, false);
        Assert.Equal((int)(70d * 58 / 44.444), dmg);
        Assert.Equal(ZoneCombat.HitFlagSoulshot | 1, flags);
    }

    [Fact]
    public void Shield_block_adds_sdef_and_perfect_block_deals_one()
    {
        // hit, shield: perfect? 10 (no), block 5 < 20 → block, no crit, spread 0
        var (blocked, bf) = ZoneCombat.RollMelee(new Scripted(0, 10, 5, 999, 0), 8.475, 134, 10, 10, 0, 30, false, 0, 20, 47, false);
        Assert.Equal((int)(70d * 8.475 / (134 + 47)), blocked);
        Assert.Equal(ZoneCombat.HitFlagShield, bf);
        // hit, perfect (99 > 95)
        var (perfect, pf) = ZoneCombat.RollMelee(new Scripted(0, 99), 100, 10, 10, 10, 0, 0, false, 0, 20, 47, false);
        Assert.Equal(1, perfect);
        Assert.Equal(ZoneCombat.HitFlagShield, pf);
        // no shield → no extra rolls
        Assert.Equal(ZoneCombat.ShieldResult.None, ZoneCombat.RollShield(new Scripted(), 0, false));
    }

    [Fact]
    public void Leather_shield_feeds_player_stats_and_weapon_has_soulshot_count()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var p = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 1, Inventory = bag };
        var world = new ZoneWorld();
        var naked = world.PlayerStats(p);
        bag.Equip(bag.Add(219)); // Sword Breaker
        bag.Equip(bag.Add(18));  // Leather Shield: sDef 47, rShld 20, rEvas −8
        var s = world.PlayerStats(p);
        Assert.Equal(47, s.ShieldDef);
        Assert.Equal(20 * 1.1, s.ShieldRate, 3); // × DEX30 bonus
        Assert.Equal(naked.Evasion - 8, s.Evasion);
        Assert.Equal(2, s.Soulshots);
        Assert.True(game.Items.TryGet(1835, out var ss) && ss!.IsSoulshot && ss.ShotGrade == CrystalGrade.None);
    }
}
