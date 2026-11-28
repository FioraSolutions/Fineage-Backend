using Fineage.Game;
using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Interlude calcMCrit / calcMagicSuccess / calcMagicDam failure branch.</summary>
public sealed class ZoneMagicCritFailTests
{
    sealed class Seq(params int[] values) : Random
    {
        int _i;
        public override int Next(int maxValue) => values[_i++ % values.Length] % maxValue;
    }

    static readonly double Base = 91d * Math.Sqrt(100) / 50 * 20;

    [Fact]
    public void Mcrit_rate_is_floor_wit_bonus_times_ten_and_unarmed_is_ten()
    {
        Assert.Equal(10, ZoneCombat.PlayerMCritRate(false, 1.39));
        Assert.Equal(10, ZoneCombat.PlayerMCritRate(true, 1.39));
        Assert.Equal(20, ZoneCombat.PlayerMCritRate(true, 2.05));
    }

    [Fact]
    public void Mcrit_rate_folds_mCritRate_buff_mul_and_add()
    {
        var mul = new CombatStatMods();
        mul.Add(new SkillStatFunc(CombatStat.MCritRate, 'm', [1.5]), 1);
        // armed wit 1.39 → (int)(1.39·1.5)=2 → 20 ‰
        Assert.Equal(20, ZoneCombat.PlayerMCritRate(true, 1.39, mul));

        var add = new CombatStatMods();
        add.Add(new SkillStatFunc(CombatStat.MCritRate, 'a', [2]), 1);
        // Wild Magic-style add: (int)(1.39+2)=3 → 30 ‰
        Assert.Equal(30, ZoneCombat.PlayerMCritRate(true, 1.39, add));
        // unarmed stays base 1 + add
        Assert.Equal(30, ZoneCombat.PlayerMCritRate(false, 1.39, add));
    }

    [Fact]
    public void Magic_success_rate_uses_1_3_pow_level_gap()
    {
        // same level: 100 - 1 = 99 → roll 98 succeeds, 99 fails
        Assert.True(ZoneCombat.RollMagicSuccess(new Seq(98), 20, 20));
        Assert.False(ZoneCombat.RollMagicSuccess(new Seq(99), 20, 20));
        // +5 levels: round(1.3^5 = 3.71) = 4 → rate 96
        Assert.True(ZoneCombat.RollMagicSuccess(new Seq(95), 25, 20));
        Assert.False(ZoneCombat.RollMagicSuccess(new Seq(96), 25, 20));
    }

    [Fact]
    public void Normal_crit_half_and_resist_outcomes()
    {
        // rolls: mcrit(1000), success(100)[, second success(100)]
        var n = ZoneCombat.RollMagicDamage(new Seq(999, 0), 100, 50, 20, 20, 20, 20, 10);
        Assert.Equal((int)Base, n.Damage); Assert.Equal(ZoneCombat.MagicOutcome.Normal, n.Outcome);

        var c = ZoneCombat.RollMagicDamage(new Seq(5, 0), 100, 50, 20, 20, 20, 20, 10);
        Assert.Equal((int)(Base * 3), c.Damage); Assert.Equal(ZoneCombat.MagicOutcome.Critical, c.Outcome);

        var h = ZoneCombat.RollMagicDamage(new Seq(5, 99, 0), 100, 50, 20, 20, 20, 20, 10);
        Assert.Equal((int)(Base / 2), h.Damage); Assert.Equal(ZoneCombat.MagicOutcome.HalfFail, h.Outcome);

        var r = ZoneCombat.RollMagicDamage(new Seq(999, 99, 99), 100, 50, 20, 20, 20, 20, 10);
        Assert.Equal(1, r.Damage); Assert.Equal(ZoneCombat.MagicOutcome.Resisted, r.Outcome);
    }

    [Fact]
    public void Magic_crit_damage_stat_multiplies_only_on_crit()
    {
        var crit = ZoneCombat.RollMagicDamage(new Seq(5, 0), 100, 50, 20, 20, 20, 20, 10, magicCritDmg: 1.5);
        Assert.Equal((int)(Base * 3 * 1.5), crit.Damage);
        Assert.Equal(ZoneCombat.MagicOutcome.Critical, crit.Outcome);

        var normal = ZoneCombat.RollMagicDamage(new Seq(999, 0), 100, 50, 20, 20, 20, 20, 10, magicCritDmg: 1.5);
        Assert.Equal((int)Base, normal.Damage);
        Assert.Equal(ZoneCombat.MagicOutcome.Normal, normal.Outcome);
    }

    [Fact]
    public void Second_roll_success_still_resists_when_target_ten_levels_above()
    {
        var r = ZoneCombat.RollMagicDamage(new Seq(999, 99, 0), 100, 50, 20, 20, 20, 30, 10);
        Assert.Equal(ZoneCombat.MagicOutcome.Resisted, r.Outcome);
    }

    [Fact]
    public void Mcrit_rate_is_capped_at_Mobius_MaxMCritRate()
    {
        // rate 5000 ‰ is capped to 200 ‰: a roll of 199 crits, 200 does not (calcMCrit is rate > Rnd.get(1000)).
        Assert.Equal(ZoneCombat.MaxMCritRate, 200);
        var crit = ZoneCombat.RollMagicDamage(new Seq(199, 0), 100, 50, 20, 20, 20, 20, mCritRate: 5000);
        Assert.Equal(ZoneCombat.MagicOutcome.Critical, crit.Outcome);
        var normal = ZoneCombat.RollMagicDamage(new Seq(200, 0), 100, 50, 20, 20, 20, 20, mCritRate: 5000);
        Assert.Equal(ZoneCombat.MagicOutcome.Normal, normal.Outcome);
    }

    [Fact]
    public void Npc_caster_failed_roll_keeps_full_damage_and_never_crits()
    {
        // Mobius only halves/resists inside `if (attacker.isPlayer())`; an NPC caster that fails the roll is unchanged.
        var failed = ZoneCombat.RollMagicDamage(new Seq(5, 99), 100, 50, 20, 20, 20, 20, 10, casterIsPlayer: false);
        Assert.Equal((int)Base, failed.Damage);
        Assert.Equal(ZoneCombat.MagicOutcome.Normal, failed.Outcome);
    }

    [Fact]
    public void Crit_multiplier_is_2_5_only_for_player_vs_player()
    {
        var pvp = ZoneCombat.RollMagicDamage(new Seq(5, 0), 100, 50, 20, 20, 20, 20, 10, targetIsPlayer: true);
        Assert.Equal((int)(Base * 2.5), pvp.Damage);
        var npcCaster = ZoneCombat.RollMagicDamage(new Seq(5, 0), 100, 50, 20, 20, 20, 20, 10,
            targetIsPlayer: true, casterIsPlayer: false);
        Assert.Equal((int)(Base * 3), npcCaster.Damage);
    }

    [Fact]
    public void Random_damage_multiplier_spreads_and_resisted_can_truncate_to_zero()
    {
        // Rnd.get(-20, 20): draw 0 → ×0.80, draw 40 → ×1.20
        var low = ZoneCombat.RollMagicDamage(new Seq(999, 0, 0), 100, 50, 20, 20, 20, 20, 10, randomPercent: 20);
        Assert.Equal((int)(Base * 0.8), low.Damage);
        var high = ZoneCombat.RollMagicDamage(new Seq(999, 0, 40), 100, 50, 20, 20, 20, 20, 10, randomPercent: 20);
        Assert.Equal((int)(Base * 1.2), high.Damage);
        // MagicalDamage effect: (int)(1 × 0.95) = 0 means no hit at all, and (int)(1 × 1.05) = 1.
        var none = ZoneCombat.RollMagicDamage(new Seq(999, 99, 99, 0), 100, 50, 20, 20, 20, 20, 10, randomPercent: 5);
        Assert.Equal(0, none.Damage);
        var one = ZoneCombat.RollMagicDamage(new Seq(999, 99, 99, 10), 100, 50, 20, 20, 20, 20, 10, randomPercent: 5);
        Assert.Equal(1, one.Damage);
    }

    [Fact]
    public void Random_percent_is_weapon_stat_or_five_plus_sqrt_level_unarmed()
    {
        Assert.Equal(20, ZoneCombat.MagicRandomPercent(true, 20, 40));
        Assert.Equal(11, ZoneCombat.MagicRandomPercent(false, 20, 40));
        Assert.Equal(6, ZoneCombat.MagicRandomPercent(false, 20, 1));
    }

    [Fact]
    public void Magic_success_rate_matches_calcMagicSuccess_including_negative_rates()
    {
        Assert.Equal(100, ZoneCombat.MagicSuccessRate(1, 14));
        Assert.Equal(92, ZoneCombat.MagicSuccessRate(46, 38));
        Assert.Equal(-817, ZoneCombat.MagicSuccessRate(40, 14));
    }

    [Fact]
    public void Npc_mAtk_and_mDef_are_folded_like_Mobius_FuncMAtkMod_FuncMDefMod()
    {
        var npcs = Fineage.Zone.Npcs.NpcCatalog.LoadDefault();
        var bonuses = ClassCatalog.LoadStatBonuses(Fineage.Zone.Spawns.GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)!);
        // Gremlin 20001 (L1): XML mAtk 5.78704, mDef 29.5916164, int 21, men 10, levelMod 0.9.
        var gremlin = npcs.Get(20001);
        double intB = bonuses.Bonus(StatSlot.Int, 21);
        double menB = bonuses.Bonus(StatSlot.Men, 10);
        Assert.Equal(5.78704 * intB * intB * 0.9 * 0.9, gremlin.MAtk, 3);
        Assert.Equal(29.5916164 * menB * 0.9, gremlin.MDef, 3);
        // A caster-type NPC with non-default INT/MEN keeps its own attributes (Skeleton Royal Guard is default; a mage is not).
        Assert.Contains(npcs.ById.Values, n => n.Int != Fineage.Zone.Npcs.NpcCatalog.DefaultInt);
        Assert.Contains(npcs.ById.Values, n => n.Men != Fineage.Zone.Npcs.NpcCatalog.DefaultMen);
    }

    [Fact]
    public void Skill_combat_catalog_carries_mobius_magic_levels()
    {
        var cat = SkillCombatCatalog.LoadDefault();
        Assert.True(cat.TryGet(1177, out var wind));
        Assert.Equal([1, 4, 7, 11, 14], wind.MagicLevel);
        Assert.Equal(1, wind.MagicLevelAt(1));
        Assert.Equal(14, wind.MagicLevelAt(5));
    }

    [Fact]
    public void Stat_effect_catalog_includes_wild_magic_mCritRate()
    {
        var cat = SkillStatEffectCatalog.LoadDefault();
        Assert.True(cat.TryGet(1303, out var wild));
        Assert.Contains(wild.Funcs, f => f.Stat == CombatStat.MCritRate && f.Op == 'a');
    }
}
