using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Reporting;
using Xunit;

namespace Fineage.LoadTest.Tests;

public sealed class SimulationHangGateTests
{
    static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Advancing_ticks_do_not_fail_and_the_tail_is_the_measured_gap()
    {
        var samples = new List<TickObservation>();
        for (int i = 0; i < 12; i++)
            samples.Add(new TickObservation(T0.AddSeconds(i * 5), 100 + i));

        var verdict = SimulationHangGate.Evaluate(samples, T0, T0.AddSeconds(60), TimeSpan.FromSeconds(30));

        Assert.False(verdict.Hung);
        Assert.Equal(0, verdict.StallCount);
        Assert.InRange(verdict.StallSeconds, 4.9, 5.1);
    }

    [Fact]
    public void Freeze_through_the_end_of_the_window_fails_with_the_true_duration()
    {
        var samples = new[]
        {
            new TickObservation(T0, 10),
            new TickObservation(T0.AddSeconds(5), 20),
            new TickObservation(T0.AddSeconds(10), 20)
        };

        var verdict = SimulationHangGate.Evaluate(samples, T0, T0.AddSeconds(46), TimeSpan.FromSeconds(30));

        Assert.True(verdict.Hung);
        Assert.Equal(1, verdict.StallCount);
        Assert.InRange(verdict.StallSeconds, 40.9, 41.1);
        Assert.Contains("41.0s", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Freeze_under_the_limit_does_not_fail()
    {
        var samples = new[]
        {
            new TickObservation(T0, 10),
            new TickObservation(T0.AddSeconds(10), 10)
        };

        var verdict = SimulationHangGate.Evaluate(samples, T0, T0.AddSeconds(29), TimeSpan.FromSeconds(30));

        Assert.False(verdict.Hung);
        Assert.InRange(verdict.StallSeconds, 28.9, 29.1);
    }

    [Fact]
    public void Empty_observation_longer_than_the_limit_fails()
    {
        var verdict = SimulationHangGate.Evaluate([], T0, T0.AddSeconds(31), TimeSpan.FromSeconds(30));

        Assert.True(verdict.Hung);
        Assert.InRange(verdict.StallSeconds, 30.9, 31.1);
    }

    [Fact]
    public void Watchdog_line_fails_with_the_logged_duration_not_the_threshold()
    {
        const string log =
            "fail: simulation step tick=12 has run 30.0s without finishing: no region bodies in flight\n" +
            "fail: simulation step tick=12 has run 47.3s without finishing: no region bodies in flight: a world stage, a collect step or the stage barrier is stuck\n";

        var verdict = SimulationHangGate.FromLog(log);

        Assert.True(verdict.Hung);
        Assert.Equal(2, verdict.StallCount);
        Assert.Equal(47.3, verdict.StallSeconds, 1);
        Assert.Contains("47.3s", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("stage barrier is stuck", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Combine_prints_the_longer_of_the_tick_freeze_and_the_watchdog()
    {
        var ticks = SimulationHangGate.Evaluate(
            [new TickObservation(T0, 1), new TickObservation(T0.AddSeconds(5), 1)],
            T0,
            T0.AddSeconds(40),
            TimeSpan.FromSeconds(30));
        var watchdog = SimulationHangGate.FromLog(
            "simulation step tick=4 has run 47.3s without finishing: region bodies still running: r1");

        var verdict = SimulationHangGate.Combine(ticks, watchdog);

        Assert.True(verdict.Hung);
        Assert.Equal(47.3, verdict.StallSeconds, 1);
        Assert.Contains("true stall duration 47.3s", verdict.Reason, StringComparison.Ordinal);
        Assert.True(verdict.StallCount >= 1);
    }

    [Fact]
    public void Tick_max_gauge_is_the_stall_when_it_exceeds_the_counter_gap_and_the_limit()
    {
        var samples = new List<TickObservation>();
        for (int i = 0; i < 12; i++)
            samples.Add(new TickObservation(T0.AddSeconds(i * 5), 100 + i, 45000));

        var counter = SimulationHangGate.Evaluate(samples, T0, T0.AddSeconds(60), TimeSpan.FromSeconds(30));
        Assert.False(counter.Hung);

        var gauge = SimulationHangGate.FromTickMax(samples, TimeSpan.FromSeconds(30));
        Assert.True(gauge.Hung);
        Assert.InRange(gauge.StallSeconds, 44.9, 45.1);

        var combined = SimulationHangGate.Combine(counter, SimulationHangVerdict.Clear, gauge);
        Assert.True(combined.Hung);
        Assert.Contains("45.0s", combined.Reason, StringComparison.Ordinal);
        Assert.Contains("fineage_tick_max_ms", combined.Reason, StringComparison.Ordinal);
        Assert.Contains("45000.0ms", combined.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Tick_max_under_the_limit_raises_the_printed_duration_without_failing()
    {
        var samples = new List<TickObservation>();
        for (int i = 0; i < 12; i++)
            samples.Add(new TickObservation(T0.AddSeconds(i * 5), 100 + i, 12000));

        var combined = SimulationHangGate.Combine(
            SimulationHangGate.Evaluate(samples, T0, T0.AddSeconds(60), TimeSpan.FromSeconds(30)),
            SimulationHangVerdict.Clear,
            SimulationHangGate.FromTickMax(samples, TimeSpan.FromSeconds(30)));

        Assert.False(combined.Hung);
        Assert.Equal(0, combined.StallCount);
        Assert.InRange(combined.StallSeconds, 11.9, 12.1);
    }

    [Fact]
    public void Tick_max_gauge_is_read_from_the_raw_scrape()
    {
        const string raw = "fineage_tick_ms_count 42\nfineage_tick_max_ms 4123.5\nfineage_tick_stage_ms_count{stage=\"commands\"} 9\n";
        var series = SimulationHangGate.FromScrapes([new PrometheusSample { ScrapedAt = T0 }], [raw]);

        Assert.Equal(4123.5, Assert.Single(series).TickMaxMs);
        Assert.Equal(4123.5, SimulationHangGate.ParseTickMaxMs(raw));
    }

    [Fact]
    public void Reported_tick_max_is_the_gauge_not_the_histogram_bucket()
    {
        var aggregate = PrometheusAggregate.FromSamples(
        [
            new PrometheusSample { ScrapedAt = T0, TickMax = 1500, TickTrueMaxMs = 1234.5 },
            new PrometheusSample { ScrapedAt = T0.AddSeconds(5), TickMax = 1000, TickTrueMaxMs = 800 }
        ]);

        Assert.Equal(1234.5, aggregate.TickMaxMs);
        Assert.Equal(1500, aggregate.TickHistogramCeilingMs);
    }

    [Fact]
    public void Tick_count_is_read_from_the_raw_scrape()
    {
        var raw = "fineage_tick_stage_ms_count{stage=\"commands\"} 9\nfineage_tick_ms_count 42\n";
        var samples = new List<PrometheusSample> { new() { ScrapedAt = T0 } };

        var series = SimulationHangGate.FromScrapes(samples, [raw]);

        Assert.Equal(42, Assert.Single(series).TickCount);
        Assert.Equal(42, SimulationHangGate.ParseTickCount(raw));
    }

    [Fact]
    public void Missing_report_is_a_failed_exit()
    {
        Assert.Equal(2, SimulationHangGate.ExitCode(reportWritten: false, passed: true));
        Assert.Equal(2, SimulationHangGate.ExitCode(reportWritten: true, passed: false));
        Assert.Equal(0, SimulationHangGate.ExitCode(reportWritten: true, passed: true));
    }

    [Fact]
    public async Task Watch_aborts_when_the_counter_stays_frozen_past_the_limit()
    {
        long count = 7;
        using var abort = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();
        var hung = new TaskCompletionSource<SimulationHangVerdict>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = SimulationHangGate.WatchTicksAsync(
            abort,
            DateTimeOffset.UtcNow,
            stop.Token,
            hung,
            poll: TimeSpan.FromMilliseconds(15),
            stallLimit: TimeSpan.FromMilliseconds(80),
            readTickCount: _ => Task.FromResult<long?>(count));

        var signal = await hung.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stop.Cancel();
        var final = await watch.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(abort.IsCancellationRequested);
        Assert.True(signal.Hung);
        Assert.True(final.Hung);
        Assert.True(final.StallSeconds >= signal.StallSeconds);
        Assert.True(final.StallSeconds > 0.08);
    }
}
