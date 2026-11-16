using Fineage.Protocol;

namespace Fineage.Zone.Manor;

/// <summary>Manor switches (Mobius <c>General.ini</c> AllowManor, <c>Rates.ini</c> RateDropManor) and the clock offset.</summary>
public sealed class ManorSettings
{
    public bool Enabled { get; init; } = true;
    /// <summary>Mobius <c>RateDropManor</c>: scales harvest counts and the seed / crop period limits.</summary>
    public int Rate { get; init; } = 1;
    /// <summary>Manor clock offset from UTC in minutes (Mobius runs on the server's local time; we run on UTC).</summary>
    public int UtcOffsetMinutes { get; init; }

    public static ManorSettings Mobius { get; } = new();

    public static ManorSettings FromEnvironment() => new()
    {
        Enabled = !string.Equals(Environment.GetEnvironmentVariable("FINEAGE_MANOR_ENABLED"), "0", StringComparison.Ordinal),
        Rate = int.TryParse(Environment.GetEnvironmentVariable("FINEAGE_MANOR_RATE"), out int rate) && rate > 0 ? rate : 1,
        UtcOffsetMinutes = int.TryParse(Environment.GetEnvironmentVariable("FINEAGE_MANOR_UTC_OFFSET_MINUTES"), out int off)
            ? Math.Clamp(off, -14 * 60, 14 * 60) : 0
    };
}

/// <summary>Pure manor formulas after the Mobius Sow / Harvesting effects and the manor day cycle.</summary>
public static class ManorRules
{
    /// <summary>
    /// Mobius <c>Sow.calcSuccess</c>: 90% (alternative seeds 20%), −5% per level the monster is outside seed level ± 5,
    /// −5% per level beyond 5 between the sower and the monster. Compared with a 0..98 roll.
    /// </summary>
    public static int SowChance(bool alternative, int seedLevel, int playerLevel, int targetLevel)
    {
        int chance = alternative ? 20 : 90;
        int min = seedLevel - 5, max = seedLevel + 5;
        if (targetLevel < min)
            chance -= 5 * (min - targetLevel);
        if (targetLevel > max)
            chance -= 5 * (targetLevel - max);
        int diff = Math.Abs(playerLevel - targetLevel);
        if (diff > 5)
            chance -= 5 * (diff - 5);
        return chance; // Mobius discards its Math.max(…, 1): a very bad match never sows
    }

    /// <summary>Mobius <c>Harvesting.calcSuccess</c>: 100%, −5% per level beyond 5 between harvester and corpse, at least 1%.</summary>
    public static int HarvestChance(int playerLevel, int targetLevel)
    {
        int diff = Math.Abs(playerLevel - targetLevel);
        int chance = 100;
        if (diff > 5)
            chance -= (diff - 5) * 5;
        return Math.Max(1, chance);
    }

    /// <summary>Mobius <c>Attackable.setSeeded(Player)</c>: 1 crop, +1 per monster level above seed level + 5, × manor rate.</summary>
    public static int HarvestCount(int monsterLevel, int seedLevel, int rate)
    {
        int count = 1;
        int diff = monsterLevel - seedLevel - 5;
        if (diff > 0)
            count += diff;
        return count * Math.Max(1, rate);
    }

    /// <summary>Mobius <c>Rnd.get(99) &lt; chance</c>.</summary>
    public static bool Roll(Random random, int chance) => random.Next(99) < chance;

    static int MinuteOfDay(long unixMs, int offsetMinutes)
    {
        var t = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToOffset(TimeSpan.FromMinutes(offsetMinutes));
        return t.Hour * 60 + t.Minute;
    }

    const int Refresh = ManorProtocol.RefreshHour * 60 + ManorProtocol.RefreshMinute;
    const int MaintenanceEnd = Refresh + ManorProtocol.MaintenanceMinutes;
    const int Approve = ManorProtocol.ApproveHour * 60 + ManorProtocol.ApproveMinute;

    /// <summary>
    /// The manor mode at boot (Mobius <c>CastleManorManager</c> constructor): 20:00–20:06 maintenance, 20:06–04:30 the
    /// next period may be edited, 04:30–20:00 the settings are approved and running.
    /// </summary>
    public static int ModeAt(long unixMs, int offsetMinutes)
    {
        int m = MinuteOfDay(unixMs, offsetMinutes);
        if (m >= Refresh && m < MaintenanceEnd)
            return ManorProtocol.ModeMaintenance;
        if (m >= MaintenanceEnd || m < Approve)
            return ManorProtocol.ModeModifiable;
        return ManorProtocol.ModeApproved;
    }

    /// <summary>Mobius <c>scheduleModeChange</c>: the next wall-clock time the mode advances, never sooner than 1 s.</summary>
    public static long NextChange(int mode, long nowMs, int offsetMinutes)
    {
        int target = mode switch
        {
            ManorProtocol.ModeModifiable => Approve,
            ManorProtocol.ModeMaintenance => MaintenanceEnd,
            ManorProtocol.ModeApproved => Refresh,
            _ => -1
        };
        if (target < 0)
            return 0;
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var local = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToOffset(offset);
        var at = new DateTimeOffset(local.Year, local.Month, local.Day, target / 60, target % 60, 0, offset);
        if (at.ToUnixTimeMilliseconds() <= nowMs)
            at = at.AddDays(1);
        return Math.Max(nowMs + 1000, at.ToUnixTimeMilliseconds());
    }
}
