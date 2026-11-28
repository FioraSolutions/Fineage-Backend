namespace Fineage.Zone.Time;

/// <summary>
/// The shared time port of one Zone process. It exposes wall UTC and a monotonic timestamp; the simulation derives
/// its fixed-step time from the monotonic timestamp so a wall-clock change cannot move a gameplay cooldown. Calendar
/// schedules read <see cref="UtcNow"/>. See <c>docs/architecture/time-model.md</c>.
/// </summary>
public interface IZoneTime
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Monotonic timestamp (provider ticks). Only differences between two samples are meaningful.</summary>
    long Timestamp { get; }

    TimeSpan Elapsed(long startingTimestamp, long endingTimestamp);

    /// <summary>
    /// True for a test adapter whose injected wall clock is the only clock: advancing it advances simulation time too.
    /// The production adapter is false, so wall jumps never alter simulation time.
    /// </summary>
    bool SimulationFollowsWall { get; }
}

/// <summary>The production clock: <see cref="TimeProvider.System"/> for both wall UTC and the monotonic timestamp.</summary>
public sealed class SystemZoneTime : IZoneTime
{
    public static readonly SystemZoneTime Instance = new();

    readonly TimeProvider _provider;

    public SystemZoneTime(TimeProvider? provider = null) =>
        _provider = provider ?? TimeProvider.System;

    public DateTimeOffset UtcNow => _provider.GetUtcNow();

    public long Timestamp => _provider.GetTimestamp();

    public TimeSpan Elapsed(long startingTimestamp, long endingTimestamp) =>
        _provider.GetElapsedTime(startingTimestamp, endingTimestamp);

    public bool SimulationFollowsWall => false;

    public long UtcNowUnixMs() => UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// Test adapter for the legacy <c>Func&lt;DateTimeOffset&gt;</c> injection: the injected wall clock is also the
/// monotonic source, so advancing it deterministically advances simulation time (the behaviour every existing
/// zone test expects). Production never constructs this adapter.
/// </summary>
public sealed class WallClockZoneTime : IZoneTime
{
    readonly Func<DateTimeOffset> _utcNow;

    public WallClockZoneTime(Func<DateTimeOffset> utcNow) =>
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));

    public DateTimeOffset UtcNow => _utcNow();

    public long Timestamp => _utcNow().ToUnixTimeMilliseconds();

    public TimeSpan Elapsed(long startingTimestamp, long endingTimestamp) =>
        TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);

    public bool SimulationFollowsWall => true;
}

/// <summary>Test adapter for the legacy unix-millisecond wall injection used by <c>SimulationClock</c> tests.</summary>
public sealed class UnixMsWallClockZoneTime : IZoneTime
{
    readonly Func<long> _wallUnixMs;

    public UnixMsWallClockZoneTime(Func<long> wallUnixMs) =>
        _wallUnixMs = wallUnixMs ?? throw new ArgumentNullException(nameof(wallUnixMs));

    public DateTimeOffset UtcNow => DateTimeOffset.FromUnixTimeMilliseconds(_wallUnixMs());

    public long Timestamp => _wallUnixMs();

    public TimeSpan Elapsed(long startingTimestamp, long endingTimestamp) =>
        TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);

    public bool SimulationFollowsWall => true;
}
