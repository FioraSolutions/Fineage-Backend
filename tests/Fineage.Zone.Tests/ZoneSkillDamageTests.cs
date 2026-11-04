using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius attack-skill formulas and timing scaling.</summary>
public sealed class ZoneSkillDamageTests
{
    [Fact]
    public void Catalog_carries_mobius_power_and_timing()
    {
        var cat = SkillCombatCatalog.LoadDefault();
        Assert.True(cat.Count > 300);
        Assert.True(cat.TryGet(3, out var ps));      // Power Strike
        Assert.Equal(SkillDamageKind.Physical, ps.Kind);
        Assert.Equal(30, ps.PowerAt(1));
        Assert.Equal(3000, ps.ReuseDelayMs);
        Assert.Equal(720, ps.CoolTimeMs);
        Assert.True(cat.TryGet(1177, out var ws));   // Wind Strike
        Assert.True(ws.Magic);
        Assert.Equal(SkillDamageKind.Magical, ws.Kind);
        Assert.Equal(2, ws.MpInitialAt(1));
        Assert.Equal(7, ws.MpAt(1));
    }

    [Fact]
    public void Cast_and_reuse_scale_with_speed()
    {
        Assert.Equal(4000, ZoneCombat.SkillTimeMs(4000, true, 333, 300));
        Assert.Equal(2000, ZoneCombat.SkillTimeMs(4000, true, 666, 300));
        Assert.Equal(1080, ZoneCombat.SkillTimeMs(1080, false, 333, 300));
        Assert.Equal(680, ZoneCombat.SkillTimeMs(1080, false, 333, 476));
        Assert.Equal(2098, ZoneCombat.SkillReuseMs(3000, false, 333, 476));
        Assert.Equal(0, ZoneCombat.SkillTimeMs(0, true, 333, 300));
    }

    [Fact]
    public void Damage_formulas_match_interlude()
    {
        // Power Strike 1, fighter P.Atk 29 vs Gremlin pDef 44.444: 70·(29+30)/44.444
        Assert.Equal((int)(70d * 59 / 44.444), ZoneCombat.PhysicalSkillDamage(29, 44.444, 30, false));
        Assert.Equal((int)(70d * 88 / 44.444), ZoneCombat.PhysicalSkillDamage(29, 44.444, 30, true));
        // Wind Strike 1, M.Atk 6 vs Gremlin mDef 29.59: 91·√6/29.59·12
        Assert.Equal((int)(91d * Math.Sqrt(6) / 29.59 * 12), ZoneCombat.MagicDamage(6, 29.59, 12));
    }
}
