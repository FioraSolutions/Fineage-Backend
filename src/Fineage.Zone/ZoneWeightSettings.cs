using Fineage.Game;

namespace Fineage.Zone;

/// <summary>
/// Mobius <c>Character.ini AltWeightLimit</c> (<c>Config.java:159</c>,
/// <c>Config.java:1654</c>: <c>characterConfig.getDouble("AltWeightLimit", 1)</c>). Multiplies the CON-derived weight
/// limit before any <c>weightLimit</c> skill: <c>floor(CON bonus * 69000 * AltWeightLimit)</c>
/// (<c>Creature.java:6284</c>).
/// <para>Env <c>FINEAGE_ALT_WEIGHT_LIMIT</c> overrides it; unset or unparsable keeps the shipped default of 1.</para>
/// </summary>
public static class ZoneWeightSettings
{
    public const string AltWeightLimitVariable = "FINEAGE_ALT_WEIGHT_LIMIT";

    public static double AltWeightLimit =>
        ParseAltWeightLimit(Environment.GetEnvironmentVariable(AltWeightLimitVariable));

    /// <summary>
    /// Mobius reads a plain double with default 1; a non-positive limit would disable every load check.
    /// Parsed with the invariant culture like Java's <c>Double.parseDouble</c>: the ini always uses a dot, so a ru-RU
    /// host must not read <c>2.5</c> as a thousands separator.
    /// </summary>
    public static double ParseAltWeightLimit(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
            return WeightPenalty.DefaultAltWeightLimit;
        return value > 0 ? value : WeightPenalty.DefaultAltWeightLimit;
    }
}
