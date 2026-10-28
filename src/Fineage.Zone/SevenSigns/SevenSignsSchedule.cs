namespace Fineage.Zone.SevenSigns;

/// <summary>
/// Weekly boundary at the configured weekday and time in the server time zone.
/// Minor periods (recruitment and results) last <see cref="SevenSignsOptions.MinorLength"/>.
/// Competition and seal validation run until the next weekly boundary.
/// </summary>
public static class SevenSignsSchedule
{
    public static DateTimeOffset Next(SevenSignsPeriod period, DateTimeOffset utcNow, SevenSignsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MinorLength <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Minor period length must be positive.");

        return period is SevenSignsPeriod.Recruiting or SevenSignsPeriod.Results
            ? utcNow + options.MinorLength
            : NextWeekly(utcNow, options);
    }

    public static DateTimeOffset NextWeekly(DateTimeOffset utcNow, SevenSignsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var local = TimeZoneInfo.ConvertTime(utcNow, options.TimeZone);
        var localWall = DateTime.SpecifyKind(local.DateTime, DateTimeKind.Unspecified);
        int days = ((int)options.PeriodStartDay - (int)localWall.DayOfWeek + 7) % 7;
        var wall = new DateTime(
            localWall.Year,
            localWall.Month,
            localWall.Day,
            options.PeriodStartHour,
            options.PeriodStartMinute,
            0,
            DateTimeKind.Unspecified).AddDays(days);
        if (wall <= localWall)
            wall = wall.AddDays(7);
        return ToUtc(wall, options.TimeZone);
    }

    static DateTimeOffset ToUtc(DateTime unspecified, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(unspecified))
            unspecified = unspecified.AddHours(1);
        var offset = zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }
}
