namespace Fineage.Zone;

/// <summary>
/// Mobius <c>Rates.ini MonsterExpMaxLevelDifference</c> (<c>Config.java:2266</c>:
/// <c>ratesConfig.getInt("MonsterExpMaxLevelDifference", 11)</c>). Outside <c>±limit</c> around the monster
/// level a kill pays nothing (<c>Attackable.calculateExpAndSp</c>, <c>Attackable.java:1347</c>).
/// <para>
/// The shipped CT0 Interlude ini sets <c>40</c>, so that is the default here; the code default of <c>11</c> only
/// applies when the key is missing from an ini. Env <c>FINEAGE_MONSTER_EXP_MAX_LEVEL_DIFFERENCE</c> overrides it.
/// </para>
/// </summary>
public static class ZoneRewardSettings
{
    public const string MonsterExpMaxLevelDifferenceVariable = "FINEAGE_MONSTER_EXP_MAX_LEVEL_DIFFERENCE";

    /// <summary>Mobius <c>Rates.ini MonsterExpMaxLevelDifference = 40</c> (shipped CT0 Interlude file).</summary>
    public const int DefaultMonsterExpMaxLevelDifference = 40;

    public static int MonsterExpMaxLevelDifference =>
        ParseMonsterExpMaxLevelDifference(Environment.GetEnvironmentVariable(MonsterExpMaxLevelDifferenceVariable));

    /// <summary>
    /// Mobius reads a plain int; a non-positive limit would zero every reward, so fall back to the shipped value.
    /// </summary>
    public static int ParseMonsterExpMaxLevelDifference(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int value))
            return DefaultMonsterExpMaxLevelDifference;
        return value > 0 ? value : DefaultMonsterExpMaxLevelDifference;
    }
}
