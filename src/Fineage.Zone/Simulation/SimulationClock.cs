using Fineage.Zone.Time;

namespace Fineage.Zone.Simulation;

/// <summary>
/// Fixed-step simulation time. Tick <c>t</c> is the <c>t</c>-th step since the Unix epoch and happens at
/// <c>t × StepMilliseconds</c>, so tick numbers mean the same instant on every host and never repeat across restarts.
/// Inside a step the time is pinned for the calling thread (<see cref="Pin"/>), so every rule the step reaches reads the
/// one tick time, and calendar rules the one wall sample of the step, instead of sampling the wall clock again.
/// Simulation time advances from the monotonic side of <see cref="IZoneTime"/>; a wall-clock jump only changes the
/// <see cref="WallUnixMs"/> sample the calendar actors receive.
/// </summary>
public sealed class SimulationClock
{
    readonly IZoneTime _time;
    readonly ThreadLocal<(long Now, long Wall)?> _pinned = new();
    long _anchorWallUnixMs;
    long _anchorTimestamp;

    public SimulationClock(int stepMilliseconds, IZoneTime time)
    {
        if (stepMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepMilliseconds), stepMilliseconds, "Simulation step must be positive.");
        StepMilliseconds = stepMilliseconds;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        Reanchor(time.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>Legacy test constructor: the wall clock is also the monotonic source.</summary>
    public SimulationClock(int stepMilliseconds, Func<long> wallUnixMs)
        : this(stepMilliseconds, new UnixMsWallClockZoneTime(wallUnixMs))
    {
    }

    public int StepMilliseconds { get; }

    /// <summary>The wall sample of the step pinned on this thread; outside a step the wall clock.</summary>
    public long WallUnixMs() => _pinned.Value?.Wall ?? _time.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>The tick time pinned on this thread inside a step; outside one (enter, leave, snapshots) the wall clock.</summary>
    public long NowUnixMs() => _pinned.Value?.Now ?? _time.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// The simulation instant the next step is derived from. Production advances from the monotonic timestamp; the
    /// wall-clock test adapter follows the supplied wall sample so existing hand-driven tests stay deterministic.
    /// </summary>
    public long SimulationNowUnixMs(long wallUnixMs) =>
        _time.SimulationFollowsWall ? wallUnixMs : _anchorWallUnixMs + ElapsedMilliseconds();

    /// <summary>
    /// The next tick: at least one step after <paramref name="previous"/>, otherwise the tick the simulation instant
    /// is in. A wall-clock jump forward therefore does not run the world ahead; a real scheduling stall still reports
    /// skipped ticks through the scheduler observer.
    /// </summary>
    public long NextTick(long previous, long wallUnixMs) =>
        Math.Max(previous + 1, TickAt(SimulationNowUnixMs(wallUnixMs)));

    /// <summary>Moves the simulation anchor to <paramref name="wallUnixMs"/> and the current monotonic sample.</summary>
    public void Reanchor(long wallUnixMs)
    {
        _anchorWallUnixMs = wallUnixMs;
        _anchorTimestamp = _time.Timestamp;
    }

    /// <summary>The tick whose step interval contains <paramref name="unixMs"/> (floor, also for times before 1970).</summary>
    public long TickAt(long unixMs) => Math.DivRem(unixMs, StepMilliseconds, out long rest) - (rest < 0 ? 1 : 0);

    public long TimeOf(long tick) => tick * StepMilliseconds;

    /// <summary>Pins <see cref="NowUnixMs"/> and <see cref="WallUnixMs"/> on this thread until the scope is disposed; scopes nest.</summary>
    public PinScope Pin(TickContext tick)
    {
        var scope = new PinScope(this, _pinned.Value);
        _pinned.Value = (tick.NowUnixMs, tick.WallUnixMs);
        return scope;
    }

    long ElapsedMilliseconds() =>
        (long)_time.Elapsed(_anchorTimestamp, _time.Timestamp).TotalMilliseconds;

    public readonly struct PinScope : IDisposable
    {
        readonly SimulationClock _clock;
        readonly (long Now, long Wall)? _previous;

        internal PinScope(SimulationClock clock, (long Now, long Wall)? previous)
        {
            _clock = clock;
            _previous = previous;
        }

        public void Dispose() => _clock._pinned.Value = _previous;
    }
}
