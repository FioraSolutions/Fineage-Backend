using Fineage.Game;
using Fineage.Zone.Augmentation;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class AugmentationTests
{
    [Fact]
    public void Life_stone_table_matches_mobius_8723()
    {
        Assert.True(AugmentationLifeStones.TryGet(8723, out var stone));
        Assert.Equal(AugmentationLifeStones.GradeNone, stone.Grade);
        Assert.Equal(46, stone.MinPlayerLevel);
        Assert.Equal(20, AugmentationLifeStones.GetGemStoneCount(CrystalGrade.C, stone.Grade));
        Assert.Equal(AugmentationLifeStones.GemstoneD, AugmentationLifeStones.GetGemStoneId(CrystalGrade.C));
    }

    [Fact]
    public void Split_variation_id_matches_combine()
    {
        var (a, b) = AugmentationRules.SplitVariationId(0x0001_0002);
        Assert.Equal(2, a);
        Assert.Equal(1, b);
        Assert.Equal(0x0001_0002, AugmentationRules.CombineVariationId(a, b));
    }

    [Fact]
    public void Retail_roller_is_deterministic_with_seeded_random()
    {
        var catalog = AugmentationCatalog.LoadDefault();
        if (!catalog.IsLoaded)
        {
            Assert.Fail("Augmentation data missing in data/augmentation");
            return;
        }

        var template = new ItemTemplate(
            1, "Saber", ItemKind.Weapon, BodySlot.RightHand, "icon", WeaponType.Sword, Crystal: CrystalGrade.C, Enchantable: true);
        var rng = new Random(42);
        var first = AugmentationRoller.RollRetail(catalog, rng, template, 8723, AugmentationLifeStones.GradeNone);
        rng = new Random(42);
        var second = AugmentationRoller.RollRetail(catalog, rng, template, 8723, AugmentationLifeStones.GradeNone);
        Assert.Equal(first, second);
        Assert.True(first.Option1 > 0);
    }

    [Fact]
    public void Validate_target_rejects_augmented_piece()
    {
        var game = GameData.LoadDefault();
        var inv = new PlayerInventory(game.Items, new ItemObjectIdAllocator(100));
        var saber = inv.Add(123, 1);
        inv.SetAugmentation(saber, 10, 0);
        Assert.False(AugmentationRules.ValidateTarget(saber));
    }
}