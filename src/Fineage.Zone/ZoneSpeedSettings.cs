namespace Fineage.Zone;

/// <summary>
/// Mobius <c>Character.ini RunSpeedBoost</c> (<c>Config.RUN_SPD_BOOST = characterConfig.getInt("RunSpeedBoost", 0)</c>,
/// <c>Config.java:1655</c>): a flat <c>+N</c> added to the folded run and walk speed of players, before the
/// <c>MaxRunSpeed</c> cap (<c>PlayerStat.java:514,543</c>). Default 0, same as the shipped ini.
/// <para>Env <c>FINEAGE_RUN_SPEED_BOOST</c> overrides it; unset or unparsable keeps the ini default.</para>
/// </summary>
public static class ZoneSpeedSettings
{
    public const string RunSpeedBoostVariable = "FINEAGE_RUN_SPEED_BOOST";

    public static int RunSpeedBoost => ParseRunSpeedBoost(Environment.GetEnvironmentVariable(RunSpeedBoostVariable));

    /// <summary>Mobius reads a plain int with default 0; the bonus is flat, so values above the cap are pointless.</summary>
    public static int ParseRunSpeedBoost(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text.Trim(), out int value))
            return 0;
        return Math.Clamp(value, 0, (int)Combat.ZoneCombat.MaxRunSpeed);
    }
}
