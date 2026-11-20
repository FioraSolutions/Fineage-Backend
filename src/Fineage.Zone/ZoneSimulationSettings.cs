using Fineage.Zone.Combat;

namespace Fineage.Zone;

public static class ZoneSimulationSettings
{
    /// <summary>Fixed simulation step: the combat cadence (the former 200 ms combat loop).</summary>
    public static int StepMilliseconds => ZoneCombat.AutoAttackTickMs;

    /// <summary>
    /// Interval of the slow systems (regen, abnormals, spawns, global schedules); <c>FINEAGE_ZONE_TICK_MS</c> keeps
    /// its meaning from the former 3 s loop. Must be a multiple of <see cref="StepMilliseconds"/>.
    /// </summary>
    public static int SlowIntervalMilliseconds => ReadInt("FINEAGE_ZONE_TICK_MS", ZoneRegen.DefaultTickMs, min: 20, max: 60_000);

    /// <summary>Steps per slow interval (15 by default).</summary>
    public static int SlowPeriodTicks => SlowPeriodFor(SlowIntervalMilliseconds, StepMilliseconds);

    internal static int SlowPeriodFor(int intervalMs, int stepMs)
    {
        if (intervalMs < stepMs || intervalMs % stepMs != 0)
            throw new InvalidOperationException(
                $"FINEAGE_ZONE_TICK_MS={intervalMs} must be a positive multiple of the {stepMs} ms simulation step.");
        return intervalMs / stepMs;
    }

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }
}
