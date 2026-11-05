using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius calcBlowSuccess / calcBlowDamage / calcBackstabDamage / Lethal effect.</summary>
public sealed class ZoneBlowSkillTests
{
    sealed class Seq(params int[] values) : Random
    {
        int _i;
        public override int Next(int maxValue) => values[_i++ % values.Length] % maxValue;
        public override int Next(int minValue, int maxValue) => minValue + values[_i++ % values.Length] % (maxValue - minValue);
    }

    [Fact]
    public void Blow_chance_scales_by_dex_and_position()
    {
        // 20 · 1.2 · front 1 = 24 → roll 23 hits, 24 fails; behind ×2 = 48; side ×1.5 = 36
        Assert.True(ZoneCombat.RollBlowSuccess(new Seq(23), 20, 1.2, true, false));
        Assert.False(ZoneCombat.RollBlowSuccess(new Seq(24), 20, 1.2, true, false));
        Assert.True(ZoneCombat.RollBlowSuccess(new Seq(47), 20, 1.2, false, true));
        Assert.True(ZoneCombat.RollBlowSuccess(new Seq(35), 20, 1.2, false, false));
        Assert.False(ZoneCombat.RollBlowSuccess(new Seq(36), 20, 1.2, false, false));
    }

    [Fact]
    public void Fatal_blow_damage_with_soulshot_and_position()
    {
        // rolls: random spread Next(-r, r+1) → 0 at offset r; crit roll 999 (no crit)
        var front = ZoneCombat.RollBlowDamage(new Seq(0, 999), BlowEffect.FatalBlow, 100, 50, 200, true, true, false, 0, 0, 1);
        Assert.Equal((int)(77d * (200 + 100 * 1.458) / 50), front.Damage);
        var behind = ZoneCombat.RollBlowDamage(new Seq(0, 999), BlowEffect.FatalBlow, 100, 50, 200, false, false, true, 0, 0, 1);
        Assert.Equal((int)(77d * 300 / 50 * 1.2), behind.Damage);
        var side = ZoneCombat.RollBlowDamage(new Seq(0, 999), BlowEffect.FatalBlow, 100, 50, 200, false, false, false, 0, 0, 1);
        Assert.Equal((int)(77d * 300 / 50 * 1.1), side.Damage);
    }

    [Fact]
    public void Backstab_applies_soulshot_to_whole_base_and_skill_crit_doubles()
    {
        var r = ZoneCombat.RollBlowDamage(new Seq(0, 0), BlowEffect.Backstab, 100, 50, 200, true, false, true, 0, 20, 1.2);
        Assert.Equal(ZoneCombat.BlowOutcome.Critical, r.Outcome);
        Assert.Equal((int)(77d * 300 / 50 * 1.458 * 1.2 * 2), r.Damage);
    }

    [Fact]
    public void Lethal_full_half_and_magic_level_gate()
    {
        // roll = Next(1e6)/1e4: 30000 → 3.0 % < full 4 → HP 1
        Assert.Equal((999, ZoneCombat.LethalOutcome.Full), ZoneCombat.RollLethal(new Seq(30000), 4, 1, 40, 40, 1000));
        // 45000 → 4.5 % < 5 → half: 1000 → 500
        Assert.Equal((500, ZoneCombat.LethalOutcome.Half), ZoneCombat.RollLethal(new Seq(45000), 4, 1, 40, 40, 1000));
        Assert.Equal(ZoneCombat.LethalOutcome.None, ZoneCombat.RollLethal(new Seq(60000), 4, 1, 40, 40, 1000).Outcome);
        // magicLvl 40 < target 47 − 6 → no lethal
        Assert.Equal(ZoneCombat.LethalOutcome.None, ZoneCombat.RollLethal(new Seq(0), 4, 1, 40, 47, 1000).Outcome);
        Assert.Equal(ZoneCombat.LethalOutcome.None, ZoneCombat.RollLethal(new Seq(0), 4, 1, 40, 40, 1000, lethalable: false).Outcome);
    }

    [Fact]
    public void Catalog_carries_mobius_blow_data()
    {
        var cat = SkillCombatCatalog.LoadDefault();
        Assert.True(cat.TryGet(30, out var backstab));
        Assert.Equal(BlowEffect.Backstab, backstab.Blow);
        Assert.Equal(30, backstab.BlowChance);
        Assert.Equal((4, 1), (backstab.FullLethal, backstab.HalfLethal));
        Assert.Equal(38, backstab.MagicLevelAt(1));
        Assert.True(cat.TryGet(16, out var mortal));
        Assert.Equal(BlowEffect.FatalBlow, mortal.Blow);
        Assert.Equal(20, mortal.BlowChance);
    }
}
