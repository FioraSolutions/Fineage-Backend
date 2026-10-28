namespace Fineage.Zone.Siege;

/// <summary>
/// Next siege instant. Mobius <c>Siege.setNextSiegeDate</c>: if the stored date is already
/// past, start from now; pin that week to the schedule day and hour; if that instant is
/// still before now, add <c>SiegeCycle</c> weeks once.
/// </summary>
public static class SiegeSchedule
{
    public static long NextFrom(long siegeUnixMs, long nowUnixMs, DayOfWeek day, int hour, int cycleWeeks)
    {
        long baseMs = siegeUnixMs < nowUnixMs ? nowUnixMs : siegeUnixMs;
        var baseTime = DateTimeOffset.FromUnixTimeMilliseconds(baseMs).UtcDateTime;
        int delta = day - baseTime.DayOfWeek;
        var slot = new DateTime(baseTime.Year, baseTime.Month, baseTime.Day, hour, 0, 0, DateTimeKind.Utc)
            .AddDays(delta);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs).UtcDateTime;
        if (slot < now)
            slot = slot.AddDays(7 * cycleWeeks);
        return new DateTimeOffset(slot, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }
}
