namespace Fineage.Zone;

/// <summary>
/// Mobius <c>NPC.ini</c> leash / drift settings (<c>Config.java:2200</c>–<c>:2206</c>):
/// <list type="bullet">
/// <item><c>MaxDriftRange = 300</c> — how far a monster may wander from its spawn.</item>
/// <item><c>AggroDistanceCheckEnabled = False</c> — when a mob is pulled past <c>AggroDistanceCheckRange</c>
/// (<c>2000</c>) from its spawn it drops aggro and walks home (<c>AttackableAI.thinkActive</c>,
/// <c>AttackableAI.java:740</c>). The shipped CT0 Interlude file has it <b>off</b>.</item>
/// <item><c>AggroDistanceCheckRestoreLife = True</c> — that reset also refills HP/MP to the maxima
/// (<c>AttackableAI.java:749</c>). This is the <i>only</i> place Mobius full-heals a monster: plain
/// <c>Attackable.returnHome()</c> (<c>Attackable.java:1685</c>) just clears aggro and walks back.</item>
/// </list>
/// <para>Env overrides: <c>FINEAGE_AGGRO_DISTANCE_CHECK_ENABLED</c>, <c>FINEAGE_AGGRO_DISTANCE_CHECK_RANGE</c>,
/// <c>FINEAGE_AGGRO_DISTANCE_CHECK_RAIDS</c>, <c>FINEAGE_AGGRO_DISTANCE_CHECK_RAID_RANGE</c>,
/// <c>FINEAGE_AGGRO_DISTANCE_CHECK_RESTORE_LIFE</c>, <c>FINEAGE_MAX_DRIFT_RANGE</c>.</para>
/// </summary>
public static class ZoneNpcSettings
{
    public const string AggroDistanceCheckEnabledVariable = "FINEAGE_AGGRO_DISTANCE_CHECK_ENABLED";
    public const string AggroDistanceCheckRangeVariable = "FINEAGE_AGGRO_DISTANCE_CHECK_RANGE";
    public const string AggroDistanceCheckRestoreLifeVariable = "FINEAGE_AGGRO_DISTANCE_CHECK_RESTORE_LIFE";
    public const string AggroDistanceCheckRaidsVariable = "FINEAGE_AGGRO_DISTANCE_CHECK_RAIDS";
    public const string AggroDistanceCheckRaidRangeVariable = "FINEAGE_AGGRO_DISTANCE_CHECK_RAID_RANGE";
    public const string MaxDriftRangeVariable = "FINEAGE_MAX_DRIFT_RANGE";

    /// <summary>Mobius <c>NPC.ini AggroDistanceCheckEnabled = False</c>.</summary>
    public const bool DefaultAggroDistanceCheckEnabled = false;
    /// <summary>Mobius <c>NPC.ini AggroDistanceCheckRange = 2000</c>.</summary>
    public const int DefaultAggroDistanceCheckRange = 2000;
    /// <summary>Mobius <c>NPC.ini AggroDistanceCheckRaids = False</c>.</summary>
    public const bool DefaultAggroDistanceCheckRaids = false;
    /// <summary>Mobius <c>NPC.ini AggroDistanceCheckRaidRange = 4000</c>.</summary>
    public const int DefaultAggroDistanceCheckRaidRange = 4000;
    /// <summary>Mobius <c>NPC.ini AggroDistanceCheckRestoreLife = True</c>.</summary>
    public const bool DefaultAggroDistanceCheckRestoreLife = true;
    /// <summary>Mobius <c>NPC.ini MaxDriftRange = 300</c>.</summary>
    public const int DefaultMaxDriftRange = 300;

    public static bool AggroDistanceCheckEnabled =>
        ParseBool(Environment.GetEnvironmentVariable(AggroDistanceCheckEnabledVariable), DefaultAggroDistanceCheckEnabled);

    public static int AggroDistanceCheckRange =>
        ParsePositiveInt(Environment.GetEnvironmentVariable(AggroDistanceCheckRangeVariable), DefaultAggroDistanceCheckRange);

    public static bool AggroDistanceCheckRaids =>
        ParseBool(Environment.GetEnvironmentVariable(AggroDistanceCheckRaidsVariable), DefaultAggroDistanceCheckRaids);

    public static int AggroDistanceCheckRaidRange =>
        ParsePositiveInt(Environment.GetEnvironmentVariable(AggroDistanceCheckRaidRangeVariable), DefaultAggroDistanceCheckRaidRange);

    public static bool AggroDistanceCheckRestoreLife =>
        ParseBool(Environment.GetEnvironmentVariable(AggroDistanceCheckRestoreLifeVariable), DefaultAggroDistanceCheckRestoreLife);

    public static int MaxDriftRange =>
        ParsePositiveInt(Environment.GetEnvironmentVariable(MaxDriftRangeVariable), DefaultMaxDriftRange);

    /// <summary>
    /// Mobius starts HP/MP regeneration for a monster as soon as it is wounded (<c>CreatureStatus.setCurrentHp</c>)
    /// and keeps it running until it is full — combat does not stop it. Kept switchable so scripted acceptance runs,
    /// which assert the exact mob HP after every single swing, can opt out.
    /// </summary>
    public const string NpcRegenVariable = "FINEAGE_NPC_REGEN_ENABLED";
    public const bool DefaultNpcRegenEnabled = true;

    public static bool NpcRegenEnabled =>
        ParseBool(Environment.GetEnvironmentVariable(NpcRegenVariable), DefaultNpcRegenEnabled);
    /// <summary>Mobius reads ini booleans as <c>true</c>/<c>false</c>; a bad value keeps the shipped default.</summary>
    public static bool ParseBool(string? text, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        string value = text.Trim();
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1")
            return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0")
            return false;
        return fallback;
    }

    /// <summary>A non-positive range would reset every mob instantly, so it falls back to the ini value.</summary>
    public static int ParsePositiveInt(string? text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int value))
            return fallback;
        return value > 0 ? value : fallback;
    }
}
