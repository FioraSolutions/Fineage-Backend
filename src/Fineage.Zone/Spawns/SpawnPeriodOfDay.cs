namespace Fineage.Zone.Spawns;

/// <summary>Mobius <c>periodOfDay</c> on spawn rows (day-only, night-only, or always).</summary>
public enum SpawnPeriodOfDay
{
    Any,
    Day,
    Night
}

public static class SpawnPeriodOfDayParse
{
    public static SpawnPeriodOfDay Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SpawnPeriodOfDay.Any;
        return value.Trim().ToLowerInvariant() switch
        {
            "day" => SpawnPeriodOfDay.Day,
            "night" => SpawnPeriodOfDay.Night,
            _ => SpawnPeriodOfDay.Any
        };
    }

    public static bool IsActive(SpawnPeriodOfDay period, bool isNight) =>
        period switch
        {
            SpawnPeriodOfDay.Any => true,
            SpawnPeriodOfDay.Day => !isNight,
            SpawnPeriodOfDay.Night => isNight,
            _ => true
        };
}