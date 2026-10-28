namespace Fineage.Zone.Olympiad;

/// <summary>
/// Competition window and the monthly / weekly boundaries.
/// Mobius <c>Olympiad.init</c>, <c>setNewOlympiadEnd</c>, and <c>OlympiadGameTask</c> countdown steps.
/// </summary>
public static class OlympiadSchedule
{
    public readonly record struct CompetitionWindow(long StartUnixMs, long EndUnixMs, bool Open);

    public readonly record struct Announcement(long AtUnixMs, int Seconds);

    public static CompetitionWindow At(long nowUnixMs, OlympiadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var zone = settings.TimeZone;
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs), zone);
        var today = new DateTime(local.Year, local.Month, local.Day, settings.StartHour, settings.StartMinute, 0, DateTimeKind.Unspecified);
        bool todayAllowed = IsCompetitionDay(today, settings);
        long start = ToUnix(today, zone);
        long end = start + settings.CompetitionMillis;
        if (todayAllowed && nowUnixMs >= start && nowUnixMs < end)
            return new CompetitionWindow(start, end, true);

        var next = nowUnixMs < start && todayAllowed ? today : today.AddDays(1);
        next = SkipToCompetitionDay(next, settings);
        long nextStart = ToUnix(next, zone);
        return new CompetitionWindow(nextStart, nextStart + settings.CompetitionMillis, false);
    }

    /// <summary>First day of next month at 12:00 in the server zone. Mobius sets <c>HOUR_OF_DAY</c> 12.</summary>
    public static long MonthEnd(long nowUnixMs, OlympiadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.UseCustomPeriod)
            return CustomEnd(nowUnixMs, settings);

        var zone = settings.TimeZone;
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs), zone);
        var wall = new DateTime(local.Year, local.Month, 1, 12, 0, 0, DateTimeKind.Unspecified).AddMonths(1);
        return ToUnix(wall, zone);
    }

    public static long WeeklyDelay(OlympiadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.UseCustomPeriod)
            return settings.WeeklyMillis;
        return settings.PeriodKind switch
        {
            "DAY" when settings.PeriodMultiplier >= 14 => settings.WeeklyMillis,
            "DAY" when settings.PeriodMultiplier >= 7 => settings.WeeklyMillis / 2,
            "WEEK" when settings.PeriodMultiplier > 1 => settings.WeeklyMillis,
            "WEEK" => settings.WeeklyMillis / 2,
            _ => settings.WeeklyMillis
        };
    }

    /// <summary>
    /// Announcements follow the Mobius countdown loop: the step changes when the announced
    /// second matches, and that new step is both the sleep and the next subtraction.
    /// </summary>
    public static List<Announcement> Countdown(long startUnixMs, int totalSeconds, int initialStep, IReadOnlyDictionary<int, int> stepAt)
    {
        var list = new List<Announcement>();
        int step = initialStep;
        long at = startUnixMs;
        for (int second = totalSeconds; second > 0; second -= step)
        {
            list.Add(new Announcement(at, second));
            if (stepAt.TryGetValue(second, out int next))
                step = next;
            at += step * 1000L;
        }

        return list;
    }

    public static List<Announcement> StadiumCountdown(long startUnixMs, int waitSeconds) =>
        Countdown(startUnixMs, waitSeconds, 60, TeleportSteps);

    public static List<Announcement> FightCountdown(long startUnixMs) =>
        Countdown(startUnixMs, 60, 10, BattleSteps);

    public static List<Announcement> ReturnCountdown(long startUnixMs) =>
        Countdown(startUnixMs, 40, 10, BattleSteps);

    static readonly Dictionary<int, int> TeleportSteps = new()
    {
        [60] = 30,
        [30] = 15,
        [15] = 5,
        [5] = 1
    };

    static readonly Dictionary<int, int> BattleSteps = new()
    {
        [10] = 5,
        [5] = 1
    };

    static long CustomEnd(long nowUnixMs, OlympiadSettings settings)
    {
        var zone = settings.TimeZone;
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs), zone);
        var wall = new DateTime(local.Year, local.Month, local.Day, 12, 0, 0, DateTimeKind.Unspecified);
        wall = settings.PeriodKind switch
        {
            "DAY" => wall.AddDays(settings.PeriodMultiplier).AddDays(-1),
            "WEEK" => wall.AddDays(7 * settings.PeriodMultiplier).AddDays(-1),
            _ => wall.AddMonths(settings.PeriodMultiplier).AddDays(-1)
        };
        return ToUnix(wall, zone);
    }

    static DateTime SkipToCompetitionDay(DateTime wall, OlympiadSettings settings)
    {
        if (!settings.UseCustomPeriod)
            return wall;
        for (int i = 0; i < 14; i++)
        {
            if (IsCompetitionDay(wall, settings))
                return wall;
            wall = wall.AddDays(1);
        }

        return wall;
    }

    static bool IsCompetitionDay(DateTime wall, OlympiadSettings settings)
    {
        if (!settings.UseCustomPeriod)
            return true;
        int javaDay = (int)wall.DayOfWeek + 1;
        return settings.CompetitionDays.Contains(javaDay);
    }

    static long ToUnix(DateTime unspecified, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(unspecified))
            unspecified = unspecified.AddHours(1);
        var offset = zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUnixTimeMilliseconds();
    }
}
