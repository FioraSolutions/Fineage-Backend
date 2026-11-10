using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class PlayerStatLayersTests
{
    [Fact]
    public void Effective_attributes_add_henna_before_bonus()
    {
        var catalog = GameData.LoadDefault().Classes!;
        Assert.True(catalog.TryGet(0, out var fighter) && fighter != null);
        var attrs = PlayerEffectiveAttributes.From(
            fighter, new HennaStatBonus(Str: 1, Con: -3, Dex: 0, Int: 0, Men: 0, Wit: 0), catalog.StatBonuses);
        Assert.Equal(fighter.Str + 1, attrs.Str);
        Assert.Equal(fighter.Con - 3, attrs.Con);
        Assert.Equal(catalog.StatBonuses.Bonus(StatSlot.Str, fighter.Str + 1), attrs.StrBonus);
    }

    [Fact]
    public void LevelMod_matches_mobius()
    {
        Assert.Equal(0.90d, PlayerGearBases.LevelMod(1), 5);
        Assert.Equal(1.09d, PlayerGearBases.LevelMod(20), 5);
    }

    [Fact]
    public void Formula_bases_match_mobius_core_math()
    {
        var attrs = new PlayerEffectiveAttributes(
            Str: 40, Dex: 30, Con: 43, Int: 21, Wit: 11, Men: 25,
            StrBonus: 1.1, DexBonus: 1.05, ConBonus: 1.2, IntBonus: 1.0, WitBonus: 1.0, MenBonus: 1.0);
        var gear = new PlayerGearBases(
            PAtk: 100, MAtk: 50, PAtkSpd: 300, CritRate: 40, AttackRange: 40, RandomDamage: 10,
            PDef: 80, MDef: 60, Armed: true, Weapon: WeaponType.Sword, WeaponGrade: CrystalGrade.D,
            Soulshots: 1, Shielded: false, ShieldDef: 0, ShieldRate: 0, ShieldEvasionPenalty: 0);
        var bases = PlayerFormulaBases.From(attrs, gear, templateMAtkSpd: 333, level: 20);
        Assert.Equal(PlayerCombatFormulas.PhysicalAttack(100, 1.1, 1.09), bases.PAtk);
        Assert.Equal(PlayerCombatFormulas.MagicalAttack(50, 1.0, 1.09), bases.MAtk);
        Assert.Equal(PlayerCombatFormulas.Accuracy(30, 20), bases.Accuracy);
        Assert.Equal(1.0d, bases.MagicCritBase);
    }

    [Fact]
    public void Passive_sources_prefer_higher_level()
    {
        var merged = PlayerPassiveSources.Merge(
            new Dictionary<int, int> { [376] = 1, [100] = 5 },
            new Dictionary<int, int> { [376] = 3, [200] = 1 });
        Assert.Equal(3, merged[376]);
        Assert.Equal(5, merged[100]);
        Assert.Equal(1, merged[200]);
    }
}
