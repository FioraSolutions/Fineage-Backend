using System.Globalization;
using System.Security.Cryptography;
using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

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

    /// <summary>
    /// A step that runs longer than this is reported by the simulation loop watchdog with the region bodies still
    /// in flight (<c>FINEAGE_ZONE_STEP_WATCHDOG_SECONDS</c>, default 30; the report repeats while the step lasts).
    /// A finished step, however late, is already covered by the overrun warning; the watchdog is for steps that
    /// never return at all.
    /// </summary>
    public static int StepWatchdogSeconds => ReadInt("FINEAGE_ZONE_STEP_WATCHDOG_SECONDS", 30, min: 1, max: 3600);

    public const int MaxRegionWorkers = 256;

    /// <summary>
    /// Worker threads of the stages that allow parallel regions: <c>FINEAGE_ZONE_REGION_WORKERS</c>. 0, the default,
    /// runs every region on the scheduler thread one after another; the result is the same either way.
    /// </summary>
    public static int RegionWorkers => ParseRegionWorkers(Environment.GetEnvironmentVariable("FINEAGE_ZONE_REGION_WORKERS"));

    internal static int ParseRegionWorkers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int workers) || workers > MaxRegionWorkers)
            throw new InvalidOperationException(
                $"FINEAGE_ZONE_REGION_WORKERS='{text}' must be an integer from 0 (sequential) to {MaxRegionWorkers}.");
        return workers;
    }

    public static IRegionExecutor RegionExecutor(int workers) =>
        workers == 0 ? SequentialRegionExecutor.Instance : new ParallelRegionExecutor(workers);

    /// <summary>The region executor as one banner token: <c>sequential</c> or <c>parallel:N</c>.</summary>
    public static string DescribeRegionExecutor(int workers) => workers == 0 ? "sequential" : $"parallel:{workers}";

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }
}
