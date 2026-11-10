using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class EnchantCombatBonusTests
{
    static ItemTemplate Dagger(CrystalGrade grade = CrystalGrade.None) =>
        new(1, "t", ItemKind.Weapon, BodySlot.RightHand, "", WeaponType.Dagger, Crystal: grade, PAtk: 27, MAtk: 21);

    static ItemTemplate Bow(CrystalGrade grade) =>
        new(2, "b", ItemKind.Weapon, BodySlot.LrHand, "", WeaponType.Bow, Crystal: grade, PAtk: 100);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 6)]   // 2*3
    [InlineData(4, 10)]  // 2*3 + 4*1
    [InlineData(16, 58)] // 2*3 + 4*13
    public void None_grade_dagger_pAtk(int enchant, int expected) =>
        Assert.Equal(expected, EnchantCombatBonus.PhysicalAttack(Dagger(), enchant));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 6)]   // armour: 3 + 3*1
    [InlineData(16, 42)] // 3 + 3*13
    public void Defence_bonus(int enchant, int expected) =>
        Assert.Equal(expected, EnchantCombatBonus.Defence(enchant));

    [Fact]
    public void S_grade_bow_overenchant_doubles()
    {
        // +4: 10*3 + 20*1 = 50
        Assert.Equal(50, EnchantCombatBonus.PhysicalAttack(Bow(CrystalGrade.S), 4));
    }

    [Fact]
    public void MAtk_A_grade()
    {
        // +4: 3*3 + 6*1 = 15
        Assert.Equal(15, EnchantCombatBonus.MagicAttack(CrystalGrade.A, 4));
    }
}
