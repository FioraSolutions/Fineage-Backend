using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius-named combat stats (pAtkRange, maxHp, sDef, pReuse, reflectDam, skillCritical, …)
/// fold through the general pipeline — no per-skill special cases.
/// </summary>
public sealed class ZoneStatPipelineTests
{
    static (ZoneWorld World, ZoneEntity Player, PlayerInventory Bag) Fighter(int level = 1)
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity
        {
            ObjectId = 1,
            Kind = ZoneEntityKind.Player,
            ClassId = 0,
            Level = level,
            Inventory = bag,
            MaxHp = 500,
            CurHp = 500,
            MaxMp = 200,
            CurMp = 200,
            Heading = 0
        };
        return (new ZoneWorld(), player, bag);
    }

    [Fact]
    public void Catalogs_carry_mobius_named_funcs()
    {
        var passives = SkillStatEffectCatalog.LoadPassives();
        Assert.True(passives.TryGet(113, out var longShot));
        Assert.Equal(CombatStat.PAtkRange, longShot.Funcs[0].Stat);
        Assert.Equal(new[] { "BOW" }, longShot.Funcs[0].Using);
        Assert.Equal(200, longShot.Funcs[0].ValueAt(1));
        Assert.Equal(400, longShot.Funcs[0].ValueAt(2));

        Assert.True(passives.TryGet(211, out var boostHp));
        Assert.Equal(CombatStat.MaxHp, boostHp.Funcs[0].Stat);
        Assert.Equal(60, boostHp.Funcs[0].ValueAt(1));
        Assert.Equal(480, boostHp.Funcs[0].ValueAt(10));

        Assert.True(passives.TryGet(290, out var frenzy));
        Assert.Equal(30, frenzy.Funcs[0].HpBelowPercent);
        Assert.True(passives.TryGet(330, out var mastery));
        Assert.Equal(2, mastery.Funcs[0].ValueAt(1));

        var stats = SkillStatEffectCatalog.LoadDefault();
        Assert.True(stats.TryGet(86, out var reflect));
        Assert.Equal(CombatStat.ReflectDam, reflect.Funcs[0].Stat);
        Assert.Equal(10, reflect.Funcs[0].ValueAt(1));
        Assert.True(stats.TryGet(318, out var aegis));
        Assert.Contains(aegis.Funcs, f => f.Stat == CombatStat.SDef && f.Op == 'm' && f.ValueAt(1) == 0.6);
        Assert.Contains(aegis.Funcs, f => f.Stat == CombatStat.ShieldDefAngle && f.Op == '=' && f.ValueAt(1) == 360);
        Assert.True(stats.TryGet(416, out var blessing));
        Assert.Equal(0.9, blessing.Funcs[0].ValueAt(1));
        Assert.True(stats.TryGet(415, out var spirit));
        Assert.Equal(CombatStat.PhysicalMpConsumeRate, spirit.Funcs[0].Stat);
    }

    [Fact]
    public void Long_shot_adds_bow_range_only_when_a_bow_is_equipped()
    {
        var (world, p, bag) = Fighter();
        int unarmed = world.PlayerStats(p).AttackRange;
        p.KnownSkills[113] = 2;
        Assert.Equal(unarmed, world.PlayerStats(p).AttackRange);

        bag.Equip(bag.Add(13)); // Short Bow, attackRange 500
        Assert.Equal(900, world.PlayerStats(p).AttackRange); // 500 + 400

        p.KnownSkills[113] = 1;
        Assert.Equal(700, world.PlayerStats(p).AttackRange);
    }

    [Fact]
    public void Boost_hp_folds_into_maxima()
    {
        var (world, p, _) = Fighter();
        world.RefreshPlayerMaxima(p, fanout: false);
        int baseHp = p.MaxHp;
        p.KnownSkills[211] = 1;
        world.RefreshPlayerMaxima(p, fanout: false);
        Assert.Equal(baseHp + 60, p.MaxHp);
    }

    [Fact]
    public void Final_frenzy_applies_only_at_or_below_30_percent_hp()
    {
        var (world, p, _) = Fighter();
        p.MaxHp = 100;
        p.CurHp = 31;
        p.KnownSkills[290] = 1;
        int full = world.PlayerStats(p).PAtk;
        p.CurHp = 30;
        Assert.True(world.PlayerStats(p).PAtk > full);
    }

    [Fact]
    public void Aegis_stance_halves_shield_def_and_covers_all_angles()
    {
        var (world, p, bag) = Fighter();
        bag.Equip(bag.Add(18)); // Leather Shield
        var before = world.PlayerStats(p);
        Assert.True(before.ShieldDef > 0);
        Assert.Equal(0, before.ShieldDefAngle);

        ZoneAbnormals.Apply(p.Abnormals, 318, 1, 1200, 0, 1);
        var aegis = world.PlayerStats(p);
        Assert.Equal((int)(before.ShieldDef * 0.6), aegis.ShieldDef);
        Assert.Equal(360, aegis.ShieldDefAngle);
        Assert.True(ZoneCombat.ShieldArcCovers(0, 0, 0, -100, 0, aegis.ShieldDefAngle));
    }

    [Fact]
    public void Shield_arc_default_is_a_120_degree_front_cone()
    {
        // Heading 0 faces +X. Attacker to the east is in front; to the west is behind.
        Assert.True(ZoneCombat.ShieldArcCovers(0, 0, 0, 100, 0, 0));
        Assert.False(ZoneCombat.ShieldArcCovers(0, 0, 0, -100, 0, 0));
        Assert.True(ZoneCombat.ShieldArcCovers(0, 0, 0, -100, 0, 360));
    }

    [Fact]
    public void Blessing_of_sagittarius_scales_physical_reuse()
    {
        Assert.Equal(2098, ZoneCombat.SkillReuseMs(3000, false, 333, 476));
        Assert.Equal((int)(3000 * 0.9 * 333d / 476), ZoneCombat.SkillReuseMs(3000, false, 333, 476, 0.9));

        var (world, p, _) = Fighter();
        var template = GameData.LoadDefault().Skills.Find(3); // Power Strike, physical
        Assert.NotNull(template);
        int baseReuse = world.SkillReuseMsFor(p, template!, template!.ReuseDelayMsAt(1));
        ZoneAbnormals.Apply(p.Abnormals, 416, 1, 1200, 0, 1);
        int buffed = world.SkillReuseMsFor(p, template, template.ReuseDelayMsAt(1));
        Assert.True(buffed < baseReuse);
        Assert.Equal((int)(baseReuse * 0.9), buffed);
    }

    [Fact]
    public void Spirit_of_sagittarius_scales_physical_mp_cost()
    {
        var (world, p, _) = Fighter();
        var template = GameData.LoadDefault().Skills.Find(3);
        Assert.NotNull(template);
        Assert.Equal(100, world.SkillMpCost(p, template, 100));
        ZoneAbnormals.Apply(p.Abnormals, 415, 1, 1200, 0, 1);
        Assert.Equal(70, world.SkillMpCost(p, template, 100));
    }

    [Fact]
    public void Reflect_and_absorb_formulas_match_mobius()
    {
        Assert.Equal(20, ZoneCombat.ReflectedDamage(200, 10, 500, attackerBow: false));
        Assert.Equal(0, ZoneCombat.ReflectedDamage(200, 10, 500, attackerBow: true));
        Assert.Equal(50, ZoneCombat.ReflectedDamage(10_000, 20, 50, attackerBow: false));
        Assert.Equal(15, ZoneCombat.AbsorbedDamage(200, 10, 15, attackerBow: false));
        Assert.Equal(0, ZoneCombat.AbsorbedDamage(200, 10, 15, attackerBow: true));
    }

    [Fact]
    public void Skill_mastery_uses_str_for_interlude_value_two()
    {
        var always = new ScriptedRandom(0); // Next(100) == 0, so any chance > 0 lands
        Assert.False(ZoneCombat.RollSkillMastery(always, 0, 1, 2, 2));
        Assert.True(ZoneCombat.RollSkillMastery(always, 2, 1, 2, 99)); // STR path
        Assert.True(ZoneCombat.RollSkillMastery(always, 4, 1, 99, 2)); // INT path
        Assert.False(ZoneCombat.RollSkillMastery(always, 2, 1, 2, 2, isStatic: true));
    }

    [Fact]
    public void Touch_of_life_multiplies_incoming_heal()
    {
        var (world, p, _) = Fighter();
        Assert.Equal(100, world.HealedAmount(p, 100));
        ZoneAbnormals.Apply(p.Abnormals, 341, 1, 1200, 0, 1);
        Assert.Equal(130, world.HealedAmount(p, 100));
    }

    sealed class ScriptedRandom : Random
    {
        readonly int _value;
        public ScriptedRandom(int value) => _value = value;
        public override int Next() => _value;
        public override int Next(int maxValue) => _value;
        public override int Next(int minValue, int maxValue) => _value;
    }
}
