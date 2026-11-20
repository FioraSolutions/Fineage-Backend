namespace Fineage.Zone.Simulation;

/// <summary>
/// Fixed-step simulation time. Tick <c>t</c> is the <c>t</c>-th step since the Unix epoch and happens at
/// <c>t × StepMilliseconds</c>, so tick numbers mean the same instant on every host and never repeat across restarts.
/// Inside a step or a command the time is pinned for the calling thread (<see cref="Pin"/>), so every rule the step
/// reaches reads the one tick time instead of sampling the wall clock again.
/// </summary>
public sealed class SimulationClock
{
    readonly Func<long> _wallUnixMs;
    readonly ThreadLocal<long?> _pinned = new();

    public SimulationClock(int stepMilliseconds, Func<long> wallUnixMs)
    {
        if (stepMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepMilliseconds), stepMilliseconds, "Simulation step must be positive.");
        StepMilliseconds = stepMilliseconds;
        _wallUnixMs = wallUnixMs ?? throw new ArgumentNullException(nameof(wallUnixMs));
    }

    public int StepMilliseconds { get; }

    public long WallUnixMs() => _wallUnixMs();

    /// <summary>
    /// The pinned time on this thread inside a step or command; outside them (handlers not yet routed through commands)
    /// the wall clock.
    /// </summary>
    public long NowUnixMs() => _pinned.Value ?? _wallUnixMs();

    /// <summary>The tick whose step interval contains <paramref name="unixMs"/> (floor, also for times before 1970).</summary>
    public long TickAt(long unixMs) => Math.DivRem(unixMs, StepMilliseconds, out long rest) - (rest < 0 ? 1 : 0);

    public long TimeOf(long tick) => tick * StepMilliseconds;

    /// <summary>Pins <see cref="NowUnixMs"/> on this thread until the scope is disposed; scopes nest.</summary>
    public PinScope Pin(long unixMs)
    {
        var scope = new PinScope(this, _pinned.Value);
        _pinned.Value = unixMs;
        return scope;
    }

    public readonly struct PinScope : IDisposable
    {
        readonly SimulationClock _clock;
        readonly long? _previous;

        internal PinScope(SimulationClock clock, long? previous)
        {
            _clock = clock;
            _previous = previous;
        }

        public void Dispose() => _clock._pinned.Value = _previous;
    }
}
