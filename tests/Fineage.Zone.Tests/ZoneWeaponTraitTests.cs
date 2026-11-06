using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius <c>Formulas.calcWeaponTraitBonus</c> + NPC defenceTraits from passives.</summary>
public sealed class ZoneWeaponTraitTests
{
    [Fact]
    public void WeaponTraitBonus_resist_reduces_damage_factor()
    {
        // DefenceTrait SWORD +20 → defence 1.2 → bonus 0.8
        var traits = new Dictionary<string, double> { ["sword"] = 1.2 };
        Assert.Equal(0.8, ZoneCombat.WeaponTraitBonus(WeaponType.Sword, traits), 3);
        Assert.Equal(1.0, ZoneCombat.WeaponTraitBonus(WeaponType.Bow, traits), 3);
    }

    [Fact]
    public void WeaponTraitBonus_weakness_increases_damage_factor()
    {
        // DefenceTrait BOW −11 → defence 0.89 → bonus 1.11
        var traits = new Dictionary<string, double> { ["bow"] = 0.89 };
        Assert.Equal(1.11, ZoneCombat.WeaponTraitBonus(WeaponType.Bow, traits), 3);
    }

    [Fact]
    public void WeaponTraitBonus_full_resist_zeros_damage()
    {
        var traits = new Dictionary<string, double> { ["sword"] = 2.0 };
        Assert.Equal(0.0, ZoneCombat.WeaponTraitBonus(WeaponType.Sword, traits), 3);
        var (dmg, _) = ZoneCombat.RollMelee(new Seq(0, 999, 0), 100, 50, 100, 0, 0, 0,
            false, 0, 0, 0, false, 1d, 1d, 0d);
        Assert.Equal(0, dmg);
    }

    [Fact]
    public void Fist_prefers_dualfist_trait()
    {
        var traits = new Dictionary<string, double> { ["dualfist"] = 1.5, ["fist"] = 1.1 };
        Assert.Equal(0.5, ZoneCombat.WeaponTraitBonus(WeaponType.Fist, traits), 3);
    }

    [Fact]
    public void Catalog_loads_bow_resist_on_dismal_pole()
    {
        // 21004 Dismal Pole — Resist Bow Weapons lv5 → BOW 1.5
        var npc = NpcCatalog.LoadDefault().Get(21004);
        Assert.NotNull(npc.DefenceTraits);
        Assert.Equal(1.5, npc.DefenceTraits!["bow"], 3);
        Assert.Equal(0.5, ZoneCombat.WeaponTraitBonus(WeaponType.Bow, npc.DefenceTraits), 3);
    }

    [Fact]
    public void Catalog_bow_weak_point_on_magical_eye()
    {
        // 21012 Magical Eye — Bow Weak Point lv1 → BOW 0.89 → bonus 1.11
        var npc = NpcCatalog.LoadDefault().Get(21012);
        Assert.Equal(0.89, npc.DefenceTraits!["bow"], 3);
        Assert.Equal(1.11, ZoneCombat.WeaponTraitBonus(WeaponType.Bow, npc.DefenceTraits), 3);
    }

    [Fact]
    public void Gremlin_has_no_weapon_traits()
    {
        Assert.Null(NpcCatalog.LoadDefault().Get(20001).DefenceTraits);
    }

    [Fact]
    public void RollMelee_applies_weapon_trait_bonus()
    {
        // hit, no shield, no crit, no random: 70*100/50 = 140 → ×0.5 = 70
        var (dmg, _) = ZoneCombat.RollMelee(new Seq(0, 999, 0), 100, 50, 100, 0, 0, 0,
            false, 0, 0, 0, false, 1d, 1d, 0.5);
        Assert.Equal(70, dmg);
    }

    /// <summary>Deterministic Random: Next(1000) then Next for shield/crit/random as needed.</summary>
    sealed class Seq : Random
    {
        readonly Queue<int> _q;
        public Seq(params int[] values) => _q = new Queue<int>(values);
        public override int Next() => _q.Count > 0 ? _q.Dequeue() : 0;
        public override int Next(int maxValue) => Next() % Math.Max(1, maxValue);
        public override int Next(int minValue, int maxValue) => minValue + (Next() % Math.Max(1, maxValue - minValue));
    }
}
