namespace Fineage.Zone.Combat;

/// <summary>
/// Mobius <c>GameTimeTaskManager</c>: 6 in-game days per real day (IG day = 4 real hours),
/// night when the IG hour is &lt; 6. Used for HitConditionBonus dark −10.
/// </summary>
public sealed class ZoneGameTime
{
    public const int IgDaysPerRealDay = 6;
    public const long MillisPerIgDay = 3_600_000L * 24 / IgDaysPerRealDay; // 4 h
    public const int NightUntilHour = 6;

    readonly long _referenceMidnightUnixMs;

    public ZoneGameTime(long referenceMidnightUnixMs) => _referenceMidnightUnixMs = referenceMidnightUnixMs;

    /// <summary>Mobius starts the clock at local calendar midnight of the process start day.</summary>
    public static ZoneGameTime FromLocalMidnight(DateTimeOffset? now = null)
    {
        var anchor = now ?? DateTimeOffset.Now;
        var local = anchor.ToOffset(anchor.Offset);
        var midnight = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
        return new ZoneGameTime(midnight.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Mobius <c>ClientSetTime</c> field 1: IG minutes into the current IG day (0..1439), one IG minute every
    /// 10 s real time. The client wraps nothing, so a counter that keeps growing since the reference midnight
    /// (3073+ after a few real hours) is out of range for its clock.
    /// </summary>
    public int ClientGameMinutes(long unixMsNow) => GameMinutes(unixMsNow);

    /// <summary>Mobius <c>ClientSetTime</c> field 2 — IG days per real day.</summary>
    public const int ClientClockSpeed = IgDaysPerRealDay;

    /// <summary>IG minutes into the current IG day (0..1439), matching Mobius <c>_gameTime</c>.</summary>
    public int GameMinutes(long unixMsNow)
    {
        long elapsed = unixMsNow - _referenceMidnightUnixMs;
        long msIntoDay = ((elapsed % MillisPerIgDay) + MillisPerIgDay) % MillisPerIgDay;
        return (int)(msIntoDay * 24 * 60 / MillisPerIgDay);
    }

    public int GameHour(long unixMsNow) => GameMinutes(unixMsNow) / 60;

    public bool IsNight(long unixMsNow) => GameHour(unixMsNow) < NightUntilHour;
}
