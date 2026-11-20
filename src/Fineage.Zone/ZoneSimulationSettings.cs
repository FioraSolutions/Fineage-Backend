using System.Globalization;
using System.Security.Cryptography;
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

    /// <summary>
    /// Seed of the simulation's random streams: <c>FINEAGE_ZONE_SEED</c> (an unsigned 64-bit integer) to reproduce a
    /// recorded run, otherwise a fresh cryptographic seed per world so live rolls stay unpredictable.
    /// </summary>
    public static ulong WorldSeed() => ParseSeed(Environment.GetEnvironmentVariable("FINEAGE_ZONE_SEED"));

    internal static ulong ParseSeed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        if (!ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
            throw new InvalidOperationException($"FINEAGE_ZONE_SEED='{text}' must be an unsigned 64-bit decimal integer.");
        return seed;
    }

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }
}
