using System.Globalization;
using Fineage.Zone.Drops;

namespace Fineage.Zone;

/// <summary>
/// Single control point for XP / SP / Adena / death-drop / spoil rates (Mobius <c>Rates.ini</c> subset).
/// Defaults are retail 1×. Override via env:
/// <c>FINEAGE_RATE_XP</c>, <c>FINEAGE_RATE_SP</c>, <c>FINEAGE_RATE_ADENA</c>,
/// <c>FINEAGE_RATE_DROP_CHANCE</c>, <c>FINEAGE_RATE_DROP_AMOUNT</c>,
/// <c>FINEAGE_RATE_SPOIL_CHANCE</c>, <c>FINEAGE_RATE_SPOIL_AMOUNT</c>.
/// </summary>
public sealed class ZoneRates
{
    public const int AdenaItemId = 57;

    public const string RateXpVariable = "FINEAGE_RATE_XP";
    public const string RateSpVariable = "FINEAGE_RATE_SP";
    public const string RateAdenaVariable = "FINEAGE_RATE_ADENA";
    public const string RateDropChanceVariable = "FINEAGE_RATE_DROP_CHANCE";
    public const string RateDropAmountVariable = "FINEAGE_RATE_DROP_AMOUNT";
    public const string RateSpoilChanceVariable = "FINEAGE_RATE_SPOIL_CHANCE";
    public const string RateSpoilAmountVariable = "FINEAGE_RATE_SPOIL_AMOUNT";

    public static ZoneRates Retail { get; } = new();

    public static ZoneRates FromEnvironment() => new()
    {
        RateXp = ReadPositive(RateXpVariable, 1),
        RateSp = ReadPositive(RateSpVariable, 1),
        RateAdena = ReadPositive(RateAdenaVariable, 1),
        DeathDropChanceMultiplier = ReadPositive(RateDropChanceVariable, 1),
        DeathDropAmountMultiplier = ReadPositive(RateDropAmountVariable, 1),
        SpoilDropChanceMultiplier = ReadPositive(RateSpoilChanceVariable, 1),
        SpoilDropAmountMultiplier = ReadPositive(RateSpoilAmountVariable, 1),
    };

    public double RateXp { get; init; } = 1;
    public double RateSp { get; init; } = 1;
    /// <summary>Amount multiplier for adena item id 57 on death / spoil rolls.</summary>
    public double RateAdena { get; init; } = 1;
    public double DeathDropChanceMultiplier { get; init; } = 1;
    public double DeathDropAmountMultiplier { get; init; } = 1;
    public double SpoilDropChanceMultiplier { get; init; } = 1;
    public double SpoilDropAmountMultiplier { get; init; } = 1;

    public DropRates AsDropRates() => new()
    {
        DeathDropChanceMultiplier = DeathDropChanceMultiplier,
        DeathDropAmountMultiplier = DeathDropAmountMultiplier,
        CorpseDropChanceMultiplier = SpoilDropChanceMultiplier,
        CorpseDropAmountMultiplier = SpoilDropAmountMultiplier,
        AdenaAmountMultiplier = RateAdena,
    };

    static double ReadPositive(string name, double fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text)
            || !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || value < 0)
            return fallback;
        return value;
    }
}
