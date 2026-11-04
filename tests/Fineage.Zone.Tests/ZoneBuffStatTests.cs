using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius buff stat funcs from data/skill-stat-effects.json feed the live combat stats.</summary>
public sealed class ZoneBuffStatTests
{
    static (ZoneWorld World, ZoneEntity Player) Fighter()
    {
        var game = GameData.LoadDefault();
        var p = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 1, Inventory = new PlayerInventory(game.Items) };
        return (new ZoneWorld(), p);
    }

    [Fact]
    public void Catalog_has_mobius_buffs()
    {
        var cat = SkillStatEffectCatalog.LoadDefault();
        Assert.True(cat.Count > 300);
        Assert.True(cat.TryGet(1068, out var might));
        Assert.Equal(1.15, might.Funcs[0].ValueAt(3), 3);
        Assert.True(cat.TryGet(4327, out _)); // Haste for Beginners
    }

    [Fact]
    public void Might_shield_haste_change_live_stats_and_expire()
    {
        var (world, p) = Fighter();
        var before = world.PlayerStats(p);
        ZoneAbnormals.Apply(p.Abnormals, 1068, 3, 1200, 0, 3);   // Might 3: pAtk ×1.15
        ZoneAbnormals.Apply(p.Abnormals, 4323, 4, 3600, 0, 1);   // Shield for Beginners: pDef ×1.15
        ZoneAbnormals.Apply(p.Abnormals, 4327, 2, 3600, 0, 1);   // Haste for Beginners: Pspd ×1.15
        var buffed = world.PlayerStats(p);
        Assert.Equal((int)(before.PDef * 1.15), buffed.PDef);
        Assert.Equal((int)(before.PAtkSpd * 1.15), buffed.PAtkSpd);
        Assert.Equal((int)(before.PAtk * 1.15), buffed.PAtk);
        Assert.True(ZoneCombat.AttackTimeMs(buffed.PAtkSpd) < ZoneCombat.AttackTimeMs(before.PAtkSpd));

        ZoneAbnormals.TickExpired(p.Abnormals, long.MaxValue);
        Assert.Equal(before, world.PlayerStats(p));
    }

    [Fact]
    public void Level_falls_back_to_abnormal_level_and_debuff_sub_applies()
    {
        var (world, p) = Fighter();
        var before = world.PlayerStats(p);
        ZoneAbnormals.Apply(p.Abnormals, 1062, 2, 120, 0);      // Berserker Spirit 2 (no stored level): rEvas −4, pDef ×0.92
        var s = world.PlayerStats(p);
        Assert.Equal(before.Evasion - 4, s.Evasion);
        Assert.Equal((int)(before.PDef * 0.92), s.PDef);
    }
}
