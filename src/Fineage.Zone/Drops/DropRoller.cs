namespace Fineage.Zone.Drops;

/// <summary>
/// Mobius <c>NpcTemplate.calculateDrops</c> at retail rates (x1): one item per group via the cumulative
/// chance, a level-gap roll, and at most <see cref="DropMaxOccurrencesNormal"/> non-guaranteed stacks
/// (<see cref="DropMaxOccurrencesRaid"/> on a raid). Spoil is the ungrouped list and never becomes a ground drop.
/// </summary>
public static class DropRoller
{
    public const int AdenaItemId = 57;
    public const int DropMaxOccurrencesNormal = 2;
    public const int DropMaxOccurrencesRaid = 7;
    public const int DropAdenaMinLevelDifference = 8;
    public const int DropAdenaMaxLevelDifference = 15;
    public const double DropAdenaMinLevelGapChance = 10;
    public const int DropItemMinLevelDifference = 5;
    public const int DropItemMaxLevelDifference = 10;
    public const double DropItemMinLevelGapChance = 10;

    public static IReadOnlyList<DropStack> RollDeath(
        DropNpcDefinition definition,
        Random random,
        DropRates? rates = null,
        int victimLevel = 0,
        int killerLevel = 0,
        bool raid = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);
        rates ??= DropRates.Retail;
        return RollGroups(definition.Death.Groups, random, rates, rates.DeathDropChanceMultiplier, rates.DeathDropAmountMultiplier,
            victimLevel, killerLevel, raid);
    }

    public static IReadOnlyList<DropStack> RollCorpse(
        DropNpcDefinition definition,
        Random random,
        DropRates? rates = null,
        int victimLevel = 0,
        int killerLevel = 0,
        bool raid = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);
        if (definition.Corpse == null)
            return [];

        rates ??= DropRates.Retail;
        // Spoil rows are Mobius ungrouped drops: each line rolls its own chance, not a group weight.
        var groups = new List<DropGroupDefinition>(definition.Corpse.Items.Count);
        foreach (var item in definition.Corpse.Items)
        {
            groups.Add(new DropGroupDefinition
            {
                Chance = item.Chance,
                Items =
                [
                    new DropItemDefinition { ItemId = item.ItemId, Min = item.Min, Max = item.Max, Chance = 100 }
                ]
            });
        }
        return RollGroups(groups, random, rates, rates.CorpseDropChanceMultiplier, rates.CorpseDropAmountMultiplier,
            victimLevel, killerLevel, raid);
    }

    /// <summary>
    /// Mobius <c>Util.map</c> of <c>victimLevel - killerLevel</c> onto the gap window.
    /// A killer at or below the mob rolls 100. Past the max gap the chance stays at the configured floor.
    /// </summary>
    public static double LevelGapChance(int victimLevel, int killerLevel, bool adena)
    {
        int diff = victimLevel - killerLevel;
        int minDiff = adena ? DropAdenaMinLevelDifference : DropItemMinLevelDifference;
        int maxDiff = adena ? DropAdenaMaxLevelDifference : DropItemMaxLevelDifference;
        double minChance = adena ? DropAdenaMinLevelGapChance : DropItemMinLevelGapChance;
        double inputMin = -maxDiff;
        double inputMax = -minDiff;
        double clamped = Math.Clamp(diff, inputMin, inputMax);
        return ((clamped - inputMin) * (100d - minChance) / (inputMax - inputMin)) + minChance;
    }

    static List<DropStack> RollGroups(
        IReadOnlyList<DropGroupDefinition> groups,
        Random random,
        DropRates rates,
        double chanceMultiplier,
        double amountMultiplier,
        int victimLevel,
        int killerLevel,
        bool raid)
    {
        var calculated = new List<Rolled>();
        var randomDrops = new List<Rolled>();
        Rolled? cached = null;
        int occurrences = raid ? DropMaxOccurrencesRaid : DropMaxOccurrencesNormal;
        bool retailRate = chanceMultiplier == 1;

        foreach (var group in groups)
        {
            double totalChance = 0;
            foreach (var item in group.Items)
            {
                if (retailRate)
                    totalChance += Math.Max(0, item.Chance);
                else
                    totalChance = Math.Max(0, item.Chance);

                double groupItemChance = totalChance * (group.Chance / 100d) * chanceMultiplier;

                if (occurrences == 0 && groupItemChance < 100 && randomDrops.Count > 0 && calculated.Count > 0)
                {
                    if (retailRate)
                    {
                        cached = randomDrops[0];
                        randomDrops.RemoveAt(0);
                        calculated.Remove(cached);
                    }
                    occurrences = 1;
                }

                bool adena = item.ItemId == AdenaItemId;
                double gap = LevelGapChance(victimLevel, killerLevel, adena);
                if (random.NextDouble() * 100 > gap)
                    continue;

                if (random.NextDouble() * 100 >= groupItemChance)
                    continue;

                int count = RollCount(item.Min, item.Max, AmountMultiplier(item.ItemId, amountMultiplier, rates), random);
                if (count <= 0)
                {
                    if (retailRate)
                        break;
                    continue;
                }

                var stack = new Rolled(item.ItemId, count);
                if (groupItemChance < 100)
                {
                    occurrences--;
                    if (retailRate)
                        randomDrops.Add(stack);
                }
                calculated.Add(stack);
                if (retailRate)
                    break;
            }
        }

        if (occurrences > 0 && cached != null)
            calculated.Add(cached);
        return calculated.Select(s => new DropStack(s.ItemId, s.Count)).ToList();
    }

    sealed class Rolled(int itemId, int count)
    {
        public int ItemId { get; } = itemId;
        public int Count { get; } = count;
    }

    static double AmountMultiplier(int itemId, double baseAmount, DropRates rates)
    {
        double mul = baseAmount;
        if (itemId == AdenaItemId && rates.AdenaAmountMultiplier > 0)
            mul *= rates.AdenaAmountMultiplier;
        return mul;
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
