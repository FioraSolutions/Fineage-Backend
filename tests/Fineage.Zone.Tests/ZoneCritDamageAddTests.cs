using Fineage.Game;
using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>Stat.CRITICAL_DAMAGE_ADD</c> ("critDmgAdd"): Vicious Stance (312) and Critical Power (193) add a flat
/// term on top of the multiplied crit instead of scaling it — <c>· 6.1 · 77 / pDef</c> for blow/phys-skill crits and
/// <c>· 77 / pDef</c> for a normal melee crit.
/// </summary>
public sealed class ZoneCritDamageAddTests
{
    sealed class Seq(params int[] values) : Random
    {
        int _i;
        public override int Next(int maxValue) => values[_i++ % values.Length] % maxValue;
        public override int Next(int minValue, int maxValue) => minValue + values[_i++ % values.Length] % (maxValue - minValue);
    }

    [Fact]
    public void Catalogs_carry_critDmgAdd_for_stance_and_critical_power()
    {
        var effects = SkillStatEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(312, out var stance)); // Vicious Stance toggle
        var add = Assert.Single(stance.Funcs, f => f.Stat == CombatStat.CritDmgAdd);
        Assert.Equal(35, add.ValueAt(1));
        Assert.Equal(609, add.ValueAt(20));

        var passives = SkillStatEffectCatalog.LoadPassives();
        Assert.True(passives.TryGet(193, out var critPower)); // Critical Power
        Assert.Contains(critPower.Funcs, f => f.Stat == CombatStat.CritDmgAdd && f.ValueAt(6) == 384);
    }

    [Fact]
    public void Blow_crit_adds_the_flat_term_after_the_multipliers()
    {
        // FatalBlow: 77·(power 200 + pAtk 100 · ss 1.458)/pDef 50 · proximity 1.2 (behind) = 638.9 → ·2 crit = 1277.8
        const double baseDamage = 77d * (200 + 100 * 1.458) / 50 * 1.2 * 2;
        var plain = ZoneCombat.RollBlowDamage(new Seq(0), BlowEffect.FatalBlow, 100, 50, 200, true, false, true, 0, 20, 1.2);
        Assert.Equal(ZoneCombat.BlowOutcome.Critical, plain.Outcome);
        Assert.Equal((int)baseDamage, plain.Damage);

        // Same roll with critDmgAdd 100: + 100 · 6.1 · 77 / 50 = +939.4 after the multipliers.
        var boosted = ZoneCombat.RollBlowDamage(new Seq(0), BlowEffect.FatalBlow, 100, 50, 200, true, false, true, 0, 20, 1.2,
            critDamageAdd: 100);
        Assert.Equal((int)(baseDamage + 100 * 6.1 * 77 / 50), boosted.Damage);
    }

    [Fact]
    public void Crit_add_does_not_touch_a_non_critical_blow()
    {
        // crit roll 999 ‰ ≥ 20 · 10 · 1.2 = 240 ‰ → not a crit, so the flat add must not appear.
        var plain = ZoneCombat.RollBlowDamage(new Seq(999), BlowEffect.FatalBlow, 100, 50, 200, true, false, true, 0, 20, 1.2,
            critDamageAdd: 100);
        var without = ZoneCombat.RollBlowDamage(new Seq(999), BlowEffect.FatalBlow, 100, 50, 200, true, false, true, 0, 20, 1.2);
        Assert.Equal(ZoneCombat.BlowOutcome.Hit, plain.Outcome);
        Assert.Equal(without.Damage, plain.Damage);
    }

    [Fact]
    public void Melee_crit_adds_the_flat_term_after_the_multipliers()
    {
        // hit (0), no shield (99), crit (0): 76·100/50 = 152 → ·2 = 304; + 100 · 77 / 50 = +154
        var plain = ZoneCombat.RollMelee(new Seq(0, 99, 0), 100, 50, 100, 0, 500, 0, false, 0, 0, 0, false, 1d, 1d, 1d);
        Assert.Equal(304, plain.Damage);

        var boosted = ZoneCombat.RollMelee(new Seq(0, 99, 0), 100, 50, 100, 0, 500, 0, false, 0, 0, 0, false, 1d, 1d, 1d, 100);
        Assert.Equal((int)(304 + 100d * 77 / 50), boosted.Damage);
    }

    [Fact]
    public void Physical_skill_crit_adds_the_flat_term_after_the_multipliers()
    {
        // 76·(100 + 50)/40 = 285 → ·2 = 570; + 100 · 6.1 · 77 / 40 = +1174.25
        var plain = ZoneCombat.PhysicalSkillDamage(100, 40, 50, soulshot: false, crit: true);
        Assert.Equal((int)(76d * 150 / 40 * 2), plain);

        var boosted = ZoneCombat.PhysicalSkillDamage(100, 40, 50, soulshot: false, crit: true, critDamageAdd: 100);
        Assert.Equal((int)(76d * 150 / 40 * 2 + 100 * 6.1 * 77 / 40), boosted);
    }
}
