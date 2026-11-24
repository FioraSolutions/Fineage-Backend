using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Reporting;
using Xunit;

namespace Fineage.LoadTest.Tests;

public class SteadyTickWindowTests
{
    static string Scrape(int online, long fast, long slow, double max) => $"""
        fineage_zone_online {online}
        fineage_tick_max_ms {max}
        fineage_tick_ms_bucket{"{"}le="50"{"}"} {fast}
        fineage_tick_ms_bucket{"{"}le="500"{"}"} {fast + slow}
        fineage_tick_ms_bucket{"{"}le="+Inf"{"}"} {fast + slow}
        fineage_tick_ms_sum {fast * 20 + slow * 400}
        fineage_tick_ms_count {fast + slow}

        """;

    [Fact]
    public void Steady_window_excludes_the_ramp_burst_and_starts_after_all_bots_are_in_world_plus_settle()
    {
        var t0 = DateTimeOffset.UnixEpoch.AddDays(20000);
        // Ramp: slow ticks while bots join; then all 10 in world at t0+10; steady opens at t0+40.
        var raw = new[]
        {
            Scrape(2, 10, 5, 450),
            Scrape(10, 20, 30, 450),
            Scrape(10, 120, 30, 450),
            Scrape(10, 220, 30, 450),
            Scrape(10, 1020, 30, 450),
        };
        int[] seconds = [0, 10, 30, 40, 100];
        var samples = raw.Select((text, i) => PrometheusParser.Parse(text, t0.AddSeconds(seconds[i]))).ToList();

        var summary = HonestLoadSummaryBuilder.Build(
            samples,
            raw,
            t0,
            t0.AddSeconds(10),
            Array.Empty<BotObservationLog.EntityTracker.LatencySample>(),
            Array.Empty<BotObservationLog.EntityTracker.LatencySample>(),
            Array.Empty<BotObservationLog.EntityTracker.LatencySample>());

        Assert.NotNull(summary.TickOverall);
        Assert.NotNull(summary.TickSteady);
        Assert.Equal(1050, summary.TickOverall!.Ticks);
        Assert.Equal(800, summary.TickSteady!.Ticks);
        Assert.Equal(t0.AddSeconds(40), summary.TickSteady.From);
        Assert.True(summary.TickSteady.P99Ms <= 50, $"steady p99 {summary.TickSteady.P99Ms}");
        Assert.True(summary.TickOverall.P99Ms > 50, $"overall p99 {summary.TickOverall.P99Ms}");
        Assert.False(summary.TickSteady.MaxExact);
        Assert.Equal(50, summary.TickSteady.MaxMs);
        Assert.Equal(450, summary.TickOverall.MaxMs);
    }
}
