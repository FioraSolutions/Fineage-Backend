using Fineage.Game;

namespace Fineage.Zone.Augmentation;

/// <summary>Mobius <c>AugmentationData</c> retail-like roll (variation 1 + category-weighted variation 2).</summary>
public static class AugmentationRoller
{
    public static readonly int[] DefaultCategoryWeights = [55, 35, 7, 3];
    static readonly int[] CategoryCodes = [55, 35, 7, 3];

    public static (int Option1, int Option2) RollRetail(
        AugmentationCatalog catalog,
        Random random,
        ItemTemplate target,
        int lifeStoneItemId,
        int lifeStoneGrade,
        int[]? categoryWeights = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(target);

        string weaponType = target.MagicWeapon ? "mage" : "warrior";
        int option1 = PickOptionId(catalog.Chances(weaponType, lifeStoneItemId, 1), random);
        int category = PickCategory(random, categoryWeights ?? DefaultCategoryWeights);
        var pool = catalog.Chances(weaponType, lifeStoneItemId, 2).Where(c => c.CategoryChance == category).ToList();
        int option2 = PickOptionId(pool, random);
        return (option1, option2);
    }

    static int PickCategory(Random random, int[] weights)
    {
        int roll = random.Next(100);
        int acc = 0;
        for (int i = 0; i < weights.Length && i < CategoryCodes.Length; i++)
        {
            acc += weights[i];
            if (roll < acc)
                return CategoryCodes[i];
        }

        return CategoryCodes[^1];
    }

    /// <summary>Mobius retail: <c>Rnd.get(10000)</c> and subtract <c>chance * 100</c>.</summary>
    static int PickOptionId(IReadOnlyList<AugmentChanceRow> chances, Random random)
    {
        if (chances.Count == 0)
        {
            ZoneLog.Warn("Augmentation roll missing chance rows; returning option id 0.");
            return 0;
        }

        int r = random.Next(10_000);
        float s = 10_000f;
        int picked = chances[0].AugmentId;
        foreach (var row in chances)
        {
            if (s > r)
            {
                s -= (float)(row.Chance * 100);
                picked = row.AugmentId;
            }
        }

        return picked;
    }
}