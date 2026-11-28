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
        // 21004 Dismal Pole — Resist Bow Weapons lv5 (BOW 1.5) × CT 0 race passive 4416 (BOW "1" → 1.01)
        var npc = NpcCatalog.LoadDefault().Get(21004);
        Assert.NotNull(npc.DefenceTraits);
        Assert.Equal(1.515, npc.DefenceTraits!["bow"], 3);
        Assert.Equal(0.485, ZoneCombat.WeaponTraitBonus(WeaponType.Bow, npc.DefenceTraits), 3);
    }

    [Fact]
    public void Catalog_bow_weak_point_on_magical_eye()
    {
        // 21012 Magical Eye — Bow Weak Point lv1 (0.89) × CT 0 race passive (1.01) → 0.8989 → bonus 1.1011
        var npc = NpcCatalog.LoadDefault().Get(21012);
        Assert.Equal(0.8989, npc.DefenceTraits!["bow"], 4);
        Assert.Equal(1.1011, ZoneCombat.WeaponTraitBonus(WeaponType.Bow, npc.DefenceTraits), 4);
    }

    [Fact]
    public void Gremlin_carries_only_the_ct0_race_passive()
    {
        // CT 0 race passive 4416 tables hold "1" for neutral traits; DefenceTrait folds (1+100)/100 = 1.01,
        // so Mobius CT 0 gives every raced NPC a 0.99 weapon-trait factor. We mirror the oracle.
        var traits = NpcCatalog.LoadDefault().Get(20001).DefenceTraits;
        Assert.NotNull(traits);
        Assert.Equal(1.01, traits!["sword"], 4);
        Assert.Equal(0.99, ZoneCombat.WeaponTraitBonus(WeaponType.Sword, traits), 4);
    }

    [Fact]
    public void RollMelee_applies_weapon_trait_bonus()
    {
        // hit, no shield, no crit, no random: 76*100/50 = 152 → ×0.5 = 76
        var (dmg, _) = ZoneCombat.RollMelee(new Seq(0, 999, 0), 100, 50, 100, 0, 0, 0,
            false, 0, 0, 0, false, 1d, 1d, 0.5);
        Assert.Equal(76, dmg);
    }

    [Fact]
    public void AttackTraitBonus_detect_insect_vs_bug_weak_point()
    {
        // Detect Insect Weakness (×1.30) vs Insect Weak Point (−15 → 0.85): (1.30−0.85)+1 = 1.45
        var defence = new Dictionary<string, double> { ["bugWeakness"] = 0.85 };
        var attack = AttackTraitCatalog.FromAbnormals([(75, 1)]);
        Assert.Equal(1.45, ZoneCombat.AttackTraitBonus(WeaponType.Sword, defence, attack), 3);
        // Without Detect buff, type-2 requires both sides → 1.0
        Assert.Equal(1.0, ZoneCombat.AttackTraitBonus(WeaponType.Sword, defence, null), 3);
    }

    [Fact]
    public void Catalog_loads_animal_weakness_on_wolf()
    {
        var npc = NpcCatalog.LoadDefault().Get(12077);
        // CT 0 race passive: animal row "0.85" → (0.85+100)/100 = 1.0085 (Mobius CT 0 DefenceTrait).
        Assert.Equal(1.0085, npc.DefenceTraits!["animalWeakness"], 4);
    }

    [Fact]
    public void AttackTraitBonus_multiplies_weapon_and_race()
    {
        // Bow weak 0.89 → weapon 1.11; Detect Animal 1.30 vs animal 0.85 → 1.45; product ≈ 1.6095
        var defence = new Dictionary<string, double> { ["bow"] = 0.89, ["animalWeakness"] = 0.85 };
        var attack = AttackTraitCatalog.FromAbnormals([(87, 1)]);
        Assert.Equal(1.11 * 1.45, ZoneCombat.AttackTraitBonus(WeaponType.Bow, defence, attack), 3);
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
