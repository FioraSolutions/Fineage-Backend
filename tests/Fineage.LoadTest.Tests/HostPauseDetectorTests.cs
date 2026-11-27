using System.Diagnostics;
using Fineage.LoadTest.Diagnostics;
using Xunit;

namespace Fineage.LoadTest.Tests;

public sealed class HostPauseDetectorTests
{
    [Fact]
    public void A_wake_far_past_its_interval_is_recorded_as_a_pause()
    {
        var detector = HostPauseDetector.ForTests();
        long t0 = Stopwatch.GetTimestamp();
        long t1 = t0 + (long)(100.1 * Stopwatch.Frequency);
        var wall = DateTimeOffset.Parse("2026-11-27T19:59:49Z");
        detector.Observe(t0, t1, wall);

        var pause = Assert.Single(detector.Pauses);
        Assert.InRange(pause.Seconds, 99.9, 100.1);
        Assert.Equal(wall.AddSeconds(-pause.Seconds), pause.StartedAt);
        // A handshake that started 1 s before the freeze lost the whole freeze, not its own 30 s.
        Assert.InRange(detector.PausedSecondsSince(t0 - Stopwatch.Frequency), 99.9, 100.1);
        Assert.Equal(0, detector.PausedSecondsSince(t1 + 1));
    }

    [Fact]
    public void Ordinary_scheduler_jitter_is_not_a_pause()
    {
        var detector = HostPauseDetector.ForTests();
        long t0 = Stopwatch.GetTimestamp();
        detector.Observe(t0, t0 + (long)(1.5 * Stopwatch.Frequency), DateTimeOffset.UtcNow);
        Assert.Empty(detector.Pauses);
    }
}
