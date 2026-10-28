using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public class EnchantRulesTests
{
    static readonly EnchantRates Rates = EnchantRates.Default;

    [Fact]
    public void Shipped_scrolls_cover_every_interlude_grade_and_fail_mode()
    {
        var items = GameData.LoadDefault().Items;
        (int Id, bool Weapon, CrystalGrade Grade, EnchantFail Fail, int Max, double Bonus)[] expected =
        [
            (955, true, CrystalGrade.D, EnchantFail.Crystallize, 16, 0),
            (956, false, CrystalGrade.D, EnchantFail.Crystallize, 16, 0),
            (6575, true, CrystalGrade.D, EnchantFail.Reset, 16, 0),
            (6576, false, CrystalGrade.D, EnchantFail.Reset, 16, 0),
            (957, true, CrystalGrade.D, EnchantFail.Keep, 65535, 100),
            (958, false, CrystalGrade.D, EnchantFail.Keep, 65535, 100),
            (951, true, CrystalGrade.C, EnchantFail.Crystallize, 16, 0),
            (952, false, CrystalGrade.C, EnchantFail.Crystallize, 16, 0),
            (6573, true, CrystalGrade.C, EnchantFail.Reset, 16, 0),
            (6574, false, CrystalGrade.C, EnchantFail.Reset, 16, 0),
            (953, true, CrystalGrade.C, EnchantFail.Keep, 65535, 100),
            (954, false, CrystalGrade.C, EnchantFail.Keep, 65535, 100),
            (947, true, CrystalGrade.B, EnchantFail.Crystallize, 16, 0),
            (948, false, CrystalGrade.B, EnchantFail.Crystallize, 16, 0),
            (6571, true, CrystalGrade.B, EnchantFail.Reset, 16, 0),
            (6572, false, CrystalGrade.B, EnchantFail.Reset, 16, 0),
            (949, true, CrystalGrade.B, EnchantFail.Keep, 65535, 100),
            (950, false, CrystalGrade.B, EnchantFail.Keep, 65535, 100),
            (729, true, CrystalGrade.A, EnchantFail.Crystallize, 16, 0),
            (730, false, CrystalGrade.A, EnchantFail.Crystallize, 16, 0),
            (6569, true, CrystalGrade.A, EnchantFail.Reset, 16, 0),
            (6570, false, CrystalGrade.A, EnchantFail.Reset, 16, 0),
            (731, true, CrystalGrade.A, EnchantFail.Keep, 65535, 100),
            (732, false, CrystalGrade.A, EnchantFail.Keep, 65535, 100),
            (959, true, CrystalGrade.S, EnchantFail.Crystallize, 16, 0),
            (960, false, CrystalGrade.S, EnchantFail.Crystallize, 16, 0),
            (6577, true, CrystalGrade.S, EnchantFail.Reset, 16, 0),
            (6578, false, CrystalGrade.S, EnchantFail.Reset, 16, 0),
            (961, true, CrystalGrade.S, EnchantFail.Keep, 65535, 100),
            (962, false, CrystalGrade.S, EnchantFail.Keep, 65535, 100)
        ];

        Assert.Equal(30, expected.Length);
        foreach (var row in expected)
        {
            var scroll = items.Get(row.Id);
            Assert.Equal(ItemKind.Etc, scroll.Kind);
            Assert.False(scroll.Stackable);
            Assert.NotNull(scroll.Scroll);
            Assert.Equal(row.Weapon, scroll.Scroll!.Weapon);
            Assert.Equal(row.Grade, scroll.Scroll.Grade);
            Assert.Equal(row.Fail, scroll.Scroll.Fail);
            Assert.Equal(row.Max, scroll.Scroll.MaxEnchant);
            Assert.Equal(row.Bonus, scroll.Scroll.BonusRate);
        }

        Assert.Equal(1458, items.Get(1458).Id);
        Assert.True(items.Get(1458).Stackable);
        Assert.Equal(CrystalGrade.D, items.Get(1458).Crystal);
        Assert.Equal(743, items.Get(123).CrystalCount);
        Assert.True(items.Get(123).Enchantable);
        Assert.False(items.Get(2369).Enchantable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Weapon_and_armor_are_safe_through_plus_three(int level)
    {
        var weapon = Weapon(CrystalGrade.D);
        var armor = Armor(BodySlot.Chest);
        var roll = new SequenceEnchantRandom(0.99);
        Assert.True(EnchantRules.Roll(weapon, level, NormalWeapon(CrystalGrade.D), Rates, roll).Success);
        Assert.True(EnchantRules.Roll(armor, level, NormalArmor(CrystalGrade.D), Rates, new SequenceEnchantRandom(0.99)).Success);
        Assert.Equal(100, EnchantRules.BaseChance(weapon, level, Rates));
    }

    [Fact]
    public void Full_armor_is_safe_through_plus_four_then_uses_the_armor_chance()
    {
        var full = Armor(BodySlot.FullArmor);
        Assert.Equal(4, EnchantRules.SafeLevel(full));
        Assert.Equal(100, EnchantRules.BaseChance(full, 3, Rates));
        Assert.Equal(66, EnchantRules.BaseChance(full, 4, Rates));
        Assert.True(EnchantRules.Roll(full, 3, NormalArmor(CrystalGrade.B), Rates, new SequenceEnchantRandom(0.99)).Success);
        Assert.False(EnchantRules.Roll(full, 4, NormalArmor(CrystalGrade.B), Rates, new SequenceEnchantRandom(0.99)).Success);
        Assert.True(EnchantRules.Roll(full, 4, NormalArmor(CrystalGrade.B), Rates, new SequenceEnchantRandom(0)).Success);
    }

    [Fact]
    public void After_safe_enchant_the_category_chance_is_used()
    {
        var rates = new EnchantRates(Weapon: 66, Armor: 40, Jewelry: 10);
        var weapon = Weapon(CrystalGrade.D);
        var armor = Armor(BodySlot.Chest);
        var neck = Armor(BodySlot.Neck);
        Assert.Equal(66, EnchantRules.BaseChance(weapon, 3, rates));
        Assert.Equal(40, EnchantRules.BaseChance(armor, 3, rates));
        Assert.Equal(10, EnchantRules.BaseChance(neck, 3, rates));
        Assert.Equal(0, EnchantRules.BaseChance(weapon, 16, rates));

        // 100 * 0.66 == 66, and Mobius succeeds only when the sample is strictly less.
        Assert.False(EnchantRules.Roll(weapon, 3, NormalWeapon(CrystalGrade.D), rates, new SequenceEnchantRandom(0.66)).Success);
        Assert.True(EnchantRules.Roll(weapon, 3, NormalWeapon(CrystalGrade.D), rates, new SequenceEnchantRandom(0.659)).Success);
        Assert.False(EnchantRules.Roll(neck, 3, NormalArmor(CrystalGrade.D), rates, new SequenceEnchantRandom(0.10)).Success);
    }

    [Fact]
    public void Crystal_bonus_makes_the_default_scroll_succeed_without_passing_a_zero_base_chance()
    {
        var weapon = Weapon(CrystalGrade.D);
        var crystal = new EnchantScroll(true, CrystalGrade.D, EnchantFail.Keep, 65535, 100);
        var atThree = EnchantRules.Roll(weapon, 3, crystal, Rates, new SequenceEnchantRandom(0.99));
        Assert.True(atThree.Success);
        Assert.Equal(66, atThree.BaseChance);
        Assert.Equal(100, atThree.FinalChance);

        var atSixteen = EnchantRules.Roll(weapon, 16, crystal, Rates, new SequenceEnchantRandom(0.99));
        Assert.True(atSixteen.Success);
        Assert.Equal(0, atSixteen.BaseChance);

        var noBonus = new EnchantScroll(true, CrystalGrade.D, EnchantFail.Keep, 65535, 0);
        var failed = EnchantRules.Roll(weapon, 3, noBonus, Rates, new SequenceEnchantRandom(0.99));
        Assert.False(failed.Success);
        Assert.Equal(66, failed.FinalChance);
    }

    [Fact]
    public void Validation_checks_enchantable_slot_max_then_grade()
    {
        var weapon = Weapon(CrystalGrade.D);
        var armor = Armor(BodySlot.Chest, CrystalGrade.D);
        var neck = Armor(BodySlot.Neck, CrystalGrade.C);
        Assert.Null(EnchantRules.Reject(weapon, 0, NormalWeapon(CrystalGrade.D)));
        Assert.Equal(EnchantDeny.NotEnchantable, EnchantRules.Reject(Weapon(CrystalGrade.D, enchantable: false), 0, NormalWeapon(CrystalGrade.D)));
        Assert.Equal(EnchantDeny.NotEnchantable, EnchantRules.Reject(Weapon(CrystalGrade.D, id: 7816), 0, NormalWeapon(CrystalGrade.D)));
        Assert.Equal(EnchantDeny.SlotMismatch, EnchantRules.Reject(armor, 0, NormalWeapon(CrystalGrade.D)));
        Assert.Equal(EnchantDeny.SlotMismatch, EnchantRules.Reject(weapon, 0, NormalArmor(CrystalGrade.D)));
        Assert.Null(EnchantRules.Reject(neck, 0, NormalArmor(CrystalGrade.C)));
        Assert.Equal(EnchantDeny.MaxEnchant, EnchantRules.Reject(weapon, 16, NormalWeapon(CrystalGrade.D)));
        Assert.Equal(EnchantDeny.GradeMismatch, EnchantRules.Reject(weapon, 0, NormalWeapon(CrystalGrade.C)));
        Assert.Null(EnchantRules.Reject(weapon, 15, NormalWeapon(CrystalGrade.D)));
    }

    [Fact]
    public void Normal_failure_crystals_follow_the_mobius_count()
    {
        var saber = Weapon(CrystalGrade.D, crystalCount: 743);
        Assert.Equal(371, EnchantRules.CrystallizedCount(saber, 0));
        Assert.Equal(641, EnchantRules.CrystallizedCount(saber, 3));
        Assert.Equal(821, EnchantRules.CrystallizedCount(saber, 4));

        var plate = Armor(BodySlot.Chest, CrystalGrade.D, crystalCount: 332);
        Assert.Equal(166, EnchantRules.CrystallizedCount(plate, 0));
        Assert.Equal(177, EnchantRules.CrystallizedCount(plate, 1));
        Assert.Equal(232, EnchantRules.CrystallizedCount(plate, 4));

        var neck = Armor(BodySlot.Neck, CrystalGrade.D, crystalCount: 103);
        Assert.Equal(51, EnchantRules.CrystallizedCount(neck, 0));
        Assert.Equal(117, EnchantRules.CrystallizedCount(neck, 4));

        var tiny = Weapon(CrystalGrade.D, crystalCount: 1);
        Assert.Equal(1, EnchantRules.CrystallizedCount(tiny, 0));
        Assert.Equal(0, EnchantRules.CrystallizedCount(Weapon(CrystalGrade.None, crystalCount: 10), 0));
        Assert.Equal(0, EnchantRules.CrystallizedCount(Weapon(CrystalGrade.D, crystalCount: 0), 4));
        Assert.Equal(1458, EnchantRules.CrystalItemId(CrystalGrade.D));
        Assert.Equal(1462, EnchantRules.CrystalItemId(CrystalGrade.S));
        Assert.Equal(0, EnchantRules.CrystalItemId(CrystalGrade.None));
    }

    [Fact]
    public void The_same_seed_repeats_the_same_samples()
    {
        var left = new SeededEnchantRandom(42);
        var right = new SeededEnchantRandom(42);
        for (int i = 0; i < 5; i++)
            Assert.Equal(left.NextUnitInterval(), right.NextUnitInterval());

        var weapon = Weapon(CrystalGrade.D);
        var scroll = NormalWeapon(CrystalGrade.D);
        bool first = EnchantRules.Roll(weapon, 3, scroll, Rates, new SeededEnchantRandom(7)).Success;
        bool second = EnchantRules.Roll(weapon, 3, scroll, Rates, new SeededEnchantRandom(7)).Success;
        Assert.Equal(first, second);
    }

    static EnchantScroll NormalWeapon(CrystalGrade grade) =>
        new(true, grade, EnchantFail.Crystallize, 16, 0);

    static EnchantScroll NormalArmor(CrystalGrade grade) =>
        new(false, grade, EnchantFail.Crystallize, 16, 0);

    static ItemTemplate Weapon(CrystalGrade grade, int id = 123, int crystalCount = 743, bool enchantable = true) =>
        new(id, "Saber", ItemKind.Weapon, BodySlot.RightHand, "icon.test", WeaponType.Sword,
            Crystal: grade, CrystalCount: crystalCount, Enchantable: enchantable);

    static ItemTemplate Armor(BodySlot slot, CrystalGrade grade = CrystalGrade.D, int crystalCount = 332) =>
        new(58, "Plate", ItemKind.Armor, slot, "icon.test",
            Crystal: grade, CrystalCount: crystalCount, Enchantable: true);
}
