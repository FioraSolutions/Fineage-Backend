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

    /// <summary>
    /// Fraction of the simulation step the command stage may spend before it borrows banked time
    /// (<c>FINEAGE_ZONE_COMMAND_APPLY_BUDGET_FRACTION</c>, default 0.35). The stage may spend unused credit from
    /// earlier steps when a per-player backlog remains, and it always stops at the tick deadline.
    /// </summary>
    public static double CommandApplyBudgetFraction =>
        ReadDouble("FINEAGE_ZONE_COMMAND_APPLY_BUDGET_FRACTION", 0.35, min: 0.05, max: 1);

    /// <summary>Nominal command-stage wall budget in milliseconds: the fraction times the step.</summary>
    public static double CommandApplyNominalMilliseconds =>
        StepMilliseconds * CommandApplyBudgetFraction;

    /// <summary>
    /// Optional extra cost-unit cap per step (<c>FINEAGE_ZONE_COMMAND_APPLY_BUDGET</c>).
    /// Unset, or <c>0</c>: no cost cap; the wall-clock timebox is the budget.
    /// A positive integer: also stop after that many command cost units in the step.
    /// Text that is not an integer throws; it is not ignored and it does not fall back to the old default of 48.
    /// </summary>
    public static int? CommandApplyCostCapOverride => ReadOptionalInt("FINEAGE_ZONE_COMMAND_APPLY_BUDGET", min: 0, max: 1_000_000);

    /// <summary>Deficit round-robin quantum in command cost units (<c>FINEAGE_ZONE_COMMAND_DRR_QUANTUM</c>, default 8).</summary>
    public static int CommandDrrQuantum => ReadIntStrict("FINEAGE_ZONE_COMMAND_DRR_QUANTUM", 8, min: 1, max: 10_000);

    /// <summary>Per-player ingress queue depth (<c>FINEAGE_ZONE_COMMAND_QUEUE_DEPTH</c>, default 64).</summary>
    public static int CommandQueueDepthLimit => ReadIntStrict("FINEAGE_ZONE_COMMAND_QUEUE_DEPTH", 64, min: 1, max: 100_000);

    /// <summary>
    /// Reject a non-coalesced command when the player's oldest pending command is older than this
    /// (<c>FINEAGE_ZONE_COMMAND_QUEUE_MAX_AGE_MS</c>, default 2000).
    /// </summary>
    public static int CommandQueueMaxAgeMs => ReadIntStrict("FINEAGE_ZONE_COMMAND_QUEUE_MAX_AGE_MS", 2000, min: 1, max: 120_000);

    /// <summary>
    /// How many character loads may hit the store at once (<c>FINEAGE_ZONE_ENTER_LOAD_CONCURRENCY</c>, default 8).
    /// The simulation thread does not load; it only inserts a player that was prepared off-tick.
    /// </summary>
    public static int EnterLoadConcurrency => ReadIntStrict("FINEAGE_ZONE_ENTER_LOAD_CONCURRENCY", 8, min: 1, max: 256);

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

    /// <summary>Unset uses <paramref name="fallback"/>. Set but not an integer, or outside the range, throws naming the variable.</summary>
    static int ReadIntStrict(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
            throw new InvalidOperationException(
                $"{name}='{text}' must be an integer from {min} to {max}.");
        return value;
    }

    static int? ReadOptionalInt(string name, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
            throw new InvalidOperationException(
                $"{name}='{text}' must be an integer from {min} to {max} (0 = no cost cap; the wall-clock timebox still applies).");
        return value;
    }

    static double ReadDouble(string name, double fallback, double min, double max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < min || value > max)
            throw new InvalidOperationException(
                $"{name}='{text}' must be a number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}.");
        return value;
    }
}
