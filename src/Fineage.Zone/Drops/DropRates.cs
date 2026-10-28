namespace Fineage.Zone.Drops;

/// <summary>Mobius-named rate multipliers; retail defaults are 1.</summary>
public sealed class DropRates
{
    public static DropRates Retail { get; } = new();

    public double DeathDropChanceMultiplier { get; init; } = 1;
    public double DeathDropAmountMultiplier { get; init; } = 1;
    public double CorpseDropChanceMultiplier { get; init; } = 1;
    public double CorpseDropAmountMultiplier { get; init; } = 1;
}
