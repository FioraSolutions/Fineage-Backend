using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius CRITICAL_DAMAGE / BLOW_RATE / P_SKILL_EVASION folds + GameTime night.</summary>
public sealed class ZonePhysCritModsTests
{
    sealed class Seq(params int[] values) : Random
    {
        int _i;
        public override int Next(int maxValue) => values[_i++ % values.Length] % maxValue;
        public override int Next(int minValue, int maxValue) => minValue + values[_i++ % values.Length] % (maxValue - minValue);
    }

    [Fact]
    public void Blow_rate_mod_scales_success_chance()
    {
        // base 24; ×1.5 → 36
        Assert.True(ZoneCombat.RollBlowSuccess(new Seq(35), 20, 1.2, true, false, 1.5));
        Assert.False(ZoneCombat.RollBlowSuccess(new Seq(36), 20, 1.2, true, false, 1.5));
    }

    [Fact]
    public void Crit_damage_mod_multiplies_blow_and_melee_crit()
    {
        var blow = ZoneCombat.RollBlowDamage(new Seq(0, 0), BlowEffect.Backstab, 100, 50, 200, true, false, true, 0, 20, 1.2,
            critDamageMod: 1.5);
        Assert.Equal(ZoneCombat.BlowOutcome.Critical, blow.Outcome);
        Assert.Equal((int)(77d * 300 / 50 * 1.458 * 1.2 * 2 * 1.5), blow.Damage);

        // hit, then crit; no random spread
        var melee = ZoneCombat.RollMelee(new Seq(0, 0, 0), 100, 50, 100, 0, 500, 0, false, 0, 0, 0, false, 1d, 1.5);
        Assert.Equal(ZoneCombat.HitFlagCrit, (byte)(melee.Flags & ZoneCombat.HitFlagCrit));
        Assert.Equal((int)(70d * 100 / 50 * 2 * 1.5), melee.Damage);
    }

    [Fact]
    public void Physical_skill_evasion_and_skill_crit()
    {
        Assert.True(ZoneCombat.RollPhysicalSkillEvasion(new Seq(5), 10));
        Assert.False(ZoneCombat.RollPhysicalSkillEvasion(new Seq(10), 10));
        Assert.False(ZoneCombat.RollPhysicalSkillEvasion(new Seq(0), 0));

        Assert.True(ZoneCombat.RollSkillCrit(new Seq(0), 20, 1.2)); // 240 ‰
        Assert.False(ZoneCombat.RollSkillCrit(new Seq(240), 20, 1.2));
        Assert.False(ZoneCombat.RollSkillCrit(new Seq(0), 0, 2));

        Assert.Equal((int)(70d * (100 * 2 + 50) / 40 * 2 * 1.25),
            ZoneCombat.PhysicalSkillDamage(100, 40, 50, true, crit: true, critDamageMod: 1.25));
    }

    [Fact]
    public void Game_time_night_is_first_six_ig_hours()
    {
        const long midnight = 1_700_000_000_000L;
        var clock = new ZoneGameTime(midnight);
        Assert.True(clock.IsNight(midnight)); // IG hour 0
        // 6 IG hours = 6/24 of a 4-real-hour IG day = 1 real hour
        Assert.False(clock.IsNight(midnight + 3_600_000));
        Assert.True(clock.IsNight(midnight + 3_600_000 - 1));
        Assert.Equal(0, clock.GameHour(midnight));
        Assert.Equal(6, clock.GameHour(midnight + 3_600_000));
        Assert.Equal(0.90, ZoneCombat.HitConditionBonus(100, 0, 0, 0, 0, 0, 0, night: true), 6);
    }

    [Fact]
    public void Catalogs_carry_critDmg_blowRate_pSkillEvas()
    {
        var effects = SkillStatEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(76, out var duel)); // Duelist Spirit / critDmg
        Assert.Contains(duel.Funcs, f => f.Stat == CombatStat.CritDmg);
        Assert.True(effects.TryGet(355, out var focusChance));
        Assert.Contains(focusChance.Funcs, f => f.Stat == CombatStat.BlowRate);
        Assert.True(effects.TryGet(111, out var ultimateEvasion));
        Assert.Contains(ultimateEvasion.Funcs, f => f.Stat == CombatStat.PSkillEvasion);

        var passives = SkillStatEffectCatalog.LoadPassives();
        Assert.True(passives.TryGet(432, out var daggerMastery));
        Assert.Contains(daggerMastery.Funcs, f => f.Stat == CombatStat.BlowRate);
    }
}
