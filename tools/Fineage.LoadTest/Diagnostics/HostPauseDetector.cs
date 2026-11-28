using System.Diagnostics;

namespace Fineage.LoadTest.Diagnostics;

/// <summary>
/// Notices when the load-test process itself was not running. A dedicated thread wakes every
/// <see cref="Interval"/>. A wake that comes far later than asked means every thread of this process was
/// descheduled: the box was paused, frozen, or starved. The monotonic clock keeps counting through that, so on resume
/// every handshake budget that was running expires at once. #1114: 14 first-login "handshake exceeded 30s" failures
/// came from one 100 s box-wide freeze during the ramp. The Zone log, the load-test runner, and an independent tick
/// sampler all went silent together, so the cause was not a slow segment of the login chain.
/// </summary>
public sealed class HostPauseDetector : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    /// <summary>A late wake beyond this counts as a pause. It is well above GC pauses and scheduler jitter.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromSeconds(2);

    static HostPauseDetector? _current;

    /// <summary>The detector of the run in progress. Bots read it to explain their own timeouts.</summary>
    public static HostPauseDetector? Current => Volatile.Read(ref _current);

    readonly object _gate = new();
    readonly List<HostPause> _pauses = [];
    readonly Thread _thread;
    volatile bool _stop;

    HostPauseDetector()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "loadtest-host-pause" };
    }

    public static HostPauseDetector Start()
    {
        var detector = new HostPauseDetector();
        Volatile.Write(ref _current, detector);
        detector._thread.Start();
        return detector;
    }

    public IReadOnlyList<HostPause> Pauses
    {
        get
        {
            lock (_gate)
                return _pauses.ToArray();
        }
    }

    /// <summary>Seconds of pause that overlap the window starting at the <see cref="Stopwatch"/> timestamp <paramref name="sinceTimestamp"/>.</summary>
    public double PausedSecondsSince(long sinceTimestamp)
    {
        double total = 0;
        lock (_gate)
        {
            foreach (var pause in _pauses)
            {
                if (pause.EndTimestamp <= sinceTimestamp)
                    continue;
                long from = Math.Max(pause.StartTimestamp, sinceTimestamp);
                total += Stopwatch.GetElapsedTime(from, pause.EndTimestamp).TotalSeconds;
            }
        }

        return total;
    }

    /// <summary>Records one late wake. It is internal so tests can feed it without sleeping.</summary>
    internal void Observe(long previousTimestamp, long nowTimestamp, DateTimeOffset nowWall)
    {
        var late = Stopwatch.GetElapsedTime(previousTimestamp, nowTimestamp) - Interval;
        if (late < Threshold)
            return;
        lock (_gate)
            _pauses.Add(new HostPause(nowWall - late, late.TotalSeconds, previousTimestamp, nowTimestamp));
    }

    void Run()
    {
        long previous = Stopwatch.GetTimestamp();
        while (!_stop)
        {
            Thread.Sleep(Interval);
            long now = Stopwatch.GetTimestamp();
            Observe(previous, now, DateTimeOffset.UtcNow);
            previous = now;
        }
    }

    internal static HostPauseDetector ForTests() => new();

    public void Dispose()
    {
        _stop = true;
        Interlocked.CompareExchange(ref _current, null, this);
    }
}

/// <summary>A span in which the load-test process did not run. <see cref="StartedAt"/> is UTC.</summary>
public sealed record HostPause(
    DateTimeOffset StartedAt,
    double Seconds,
    [property: System.Text.Json.Serialization.JsonIgnore] long StartTimestamp,
    [property: System.Text.Json.Serialization.JsonIgnore] long EndTimestamp);
