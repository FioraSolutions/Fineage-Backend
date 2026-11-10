namespace Fineage.Zone.Drops;

/// <summary>Mobius-named rate multipliers; retail defaults are 1. Prefer <see cref="ZoneRates"/> as the control point.</summary>
public sealed class DropRates
{
    public static DropRates Retail { get; } = new();

    public double DeathDropChanceMultiplier { get; init; } = 1;
    public double DeathDropAmountMultiplier { get; init; } = 1;
    public double CorpseDropChanceMultiplier { get; init; } = 1;
    public double CorpseDropAmountMultiplier { get; init; } = 1;
    /// <summary>Extra amount multiplier for adena (item 57), stacked on death/corpse amount rates.</summary>
    public double AdenaAmountMultiplier { get; init; } = 1;
}
