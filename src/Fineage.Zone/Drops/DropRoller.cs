namespace Fineage.Zone.Drops;

public static class DropRoller
{
    public static IReadOnlyList<DropStack> RollDeath(DropNpcDefinition definition, Random random, DropRates? rates = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);
        rates ??= DropRates.Retail;

        var stacks = new List<DropStack>();
        foreach (var group in definition.Death.Groups)
        {
            if (!PassPercent(group.Chance * rates.DeathDropChanceMultiplier, random))
                continue;

            var item = PickWeighted(group.Items, random);
            if (item == null)
                continue;

            int count = RollCount(item.Min, item.Max, rates.DeathDropAmountMultiplier, random);
            if (count > 0)
                stacks.Add(new DropStack(item.ItemId, count));
        }

        return stacks;
    }

    public static IReadOnlyList<DropStack> RollCorpse(DropNpcDefinition definition, Random random, DropRates? rates = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);
        if (definition.Corpse == null)
            return [];

        rates ??= DropRates.Retail;
        var stacks = new List<DropStack>();
        foreach (var item in definition.Corpse.Items)
        {
            if (!PassPercent(item.Chance * rates.CorpseDropChanceMultiplier, random))
                continue;

            int count = RollCount(item.Min, item.Max, rates.CorpseDropAmountMultiplier, random);
            if (count > 0)
                stacks.Add(new DropStack(item.ItemId, count));
        }

        return stacks;
    }

    static DropItemDefinition? PickWeighted(IReadOnlyList<DropItemDefinition> items, Random random)
    {
        if (items.Count == 0)
            return null;
        if (items.Count == 1)
            return items[0];

        double total = 0;
        foreach (var item in items)
            total += Math.Max(0, item.Chance);
        if (total <= 0)
            return items[0];

        double roll = random.NextDouble() * total;
        foreach (var item in items)
        {
            roll -= Math.Max(0, item.Chance);
            if (roll <= 0)
                return item;
        }

        return items[^1];
    }

    static bool PassPercent(double chance, Random random)
    {
        if (chance <= 0)
            return false;
        if (chance >= 100)
            return true;
        return random.NextDouble() * 100 < chance;
    }

    static int RollCount(int min, int max, double amountMultiplier, Random random)
    {
        if (max < min)
            (min, max) = (max, min);
        int baseCount = min == max ? min : random.Next(min, max + 1);
        if (amountMultiplier <= 0)
            return 0;
        int scaled = (int)Math.Floor(baseCount * amountMultiplier);
        return Math.Max(scaled, baseCount > 0 ? 1 : 0);
    }
}
