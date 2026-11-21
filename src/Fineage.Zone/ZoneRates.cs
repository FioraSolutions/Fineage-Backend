using System.Globalization;
using Fineage.Zone.Drops;

namespace Fineage.Zone;

/// <summary>
/// Single control point for XP / SP / Adena / death-drop / spoil rates (Mobius <c>Rates.ini</c> subset).
/// Defaults are retail 1×. Override via env:
/// <c>FINEAGE_RATE_XP</c>, <c>FINEAGE_RATE_SP</c>, <c>FINEAGE_RATE_ADENA</c>,
/// <c>FINEAGE_RATE_DROP_CHANCE</c>, <c>FINEAGE_RATE_DROP_AMOUNT</c>,
/// <c>FINEAGE_RATE_SPOIL_CHANCE</c>, <c>FINEAGE_RATE_SPOIL_AMOUNT</c>,
/// <c>FINEAGE_AUTO_LOOT</c>, <c>FINEAGE_AUTO_LOOT_RAIDS</c>, <c>FINEAGE_AUTO_LOOT_HERBS</c>.
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
    public const string AutoLootVariable = "FINEAGE_AUTO_LOOT";
    public const string AutoLootRaidsVariable = "FINEAGE_AUTO_LOOT_RAIDS";
    public const string AutoLootHerbsVariable = "FINEAGE_AUTO_LOOT_HERBS";

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
        AutoLoot = ReadBool(AutoLootVariable, false),
        AutoLootRaids = ReadBool(AutoLootRaidsVariable, false),
        AutoLootHerbs = ReadBool(AutoLootHerbsVariable, false),
    };

    public double RateXp { get; init; } = 1;
    public double RateSp { get; init; } = 1;
    /// <summary>Amount multiplier for adena item id 57 on death / spoil rolls.</summary>
    public double RateAdena { get; init; } = 1;
    public double DeathDropChanceMultiplier { get; init; } = 1;
    public double DeathDropAmountMultiplier { get; init; } = 1;
    public double SpoilDropChanceMultiplier { get; init; } = 1;
    public double SpoilDropAmountMultiplier { get; init; } = 1;
    /// <summary>Mobius <c>AUTO_LOOT</c>: death drops to killer inventory instead of ground (non-herb unless <see cref="AutoLootHerbs"/>).</summary>
    public bool AutoLoot { get; init; }
    /// <summary>Mobius <c>AUTO_LOOT_RAIDS</c>.</summary>
    public bool AutoLootRaids { get; init; }
    /// <summary>Mobius <c>AUTO_LOOT_HERBS</c>.</summary>
    public bool AutoLootHerbs { get; init; }

    public bool ShouldAutoLootItem(int itemId, bool raid, bool isHerb) =>
        (!raid || AutoLootRaids) && (isHerb ? AutoLootHerbs : AutoLoot);

    public DropRates AsDropRates() => new()
    {
        DeathDropChanceMultiplier = DeathDropChanceMultiplier,
        DeathDropAmountMultiplier = DeathDropAmountMultiplier,
        CorpseDropChanceMultiplier = SpoilDropChanceMultiplier,
        CorpseDropAmountMultiplier = SpoilDropAmountMultiplier,
        AdenaAmountMultiplier = RateAdena,
    };

    static bool ReadBool(string name, bool fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        return text.Trim() switch
        {
            "1" or "true" or "TRUE" or "yes" or "YES" or "on" or "ON" => true,
            "0" or "false" or "FALSE" or "no" or "NO" or "off" or "OFF" => false,
            _ => fallback,
        };
    }

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
