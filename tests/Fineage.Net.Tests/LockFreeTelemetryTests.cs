using System.Diagnostics.Metrics;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class LockFreeTelemetryTests
{
    [Fact]
    public void Collector_counts_every_measurement_from_many_threads_without_a_recording_lock()
    {
        using var meter = new Meter(FineageTelemetry.MeterName);
        Counter<long> overruns = meter.CreateCounter<long>("fineage.tick.overruns");
        using var collector = new MetricsCollector();
        long before = collector.Snapshot().TickOverruns;

        const int threads = 8;
        const int perThread = 50_000; // several times the per-thread ring, so the full-ring path runs too
        var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
        {
            for (int i = 0; i < perThread; i++)
                overruns.Add(1);
        })).ToArray();
        foreach (Thread worker in workers)
            worker.Start();
        foreach (Thread worker in workers)
            worker.Join();

        // Threads have exited: their rings must still be drained, not lost.
        Assert.Equal(before + threads * perThread, collector.Snapshot().TickOverruns);
    }

    [Fact]
    public void A_producer_that_outruns_the_drainer_keeps_every_measurement_and_shows_a_backlog()
    {
        using var meter = new Meter(FineageTelemetry.MeterName);
        Counter<long> overruns = meter.CreateCounter<long>("fineage.tick.overruns");
        using var collector = new MetricsCollector();
        long before = collector.Snapshot().TickOverruns;

        // A stopped drainer is a starved consumer: the producer now owns the whole backlog, which #972 promises to keep
        // lossless. Nothing may be dropped, however far the consumer lags.
        collector.PauseDrainerForTest();

        const int recorded = 20_000;
        for (int i = 0; i < recorded; i++)
            overruns.Add(1);

        // The backlog is observable while the consumer is stopped, which is how a soak proves the drain keeps up rather
        // than inferring it from a counter that would look the same either way.
        Assert.True(collector.TelemetryBacklogSlotsForTest() >= recorded,
            "sealed blocks must be visible as backlog while the consumer is behind");

        // Snapshot drains under the gate: every measurement survives, and the backlog returns to zero.
        Assert.Equal(before + recorded, collector.Snapshot().TickOverruns);
        Assert.Equal(0, collector.TelemetryBacklogSlotsForTest());
    }

    [Fact]
    public void Wakeup_never_loses_a_signal()
    {
        using var wakeup = new WorkerWakeup();
        var queue = new System.Collections.Concurrent.ConcurrentQueue<int>();
        const int items = 200_000;
        long consumed = 0;
        var consumer = new Thread(() =>
        {
            while (Interlocked.Read(ref consumed) < items)
            {
                if (queue.TryDequeue(out _))
                {
                    Interlocked.Increment(ref consumed);
                    continue;
                }

                wakeup.Park(() => !queue.IsEmpty);
            }
        });
        consumer.Start();
        for (int i = 0; i < items; i++)
        {
            queue.Enqueue(i);
            wakeup.Signal();
        }

        Assert.True(consumer.Join(TimeSpan.FromSeconds(30)), $"consumer stalled at {Interlocked.Read(ref consumed)}/{items}");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65")]
    [InlineData("four")]
    public void Invalid_stripe_count_env_throws(string value)
    {
        string? previous = Environment.GetEnvironmentVariable("FINEAGE_NET_FANOUT_STRIPES");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_NET_FANOUT_STRIPES", value);
            Assert.Throws<InvalidOperationException>(() => NetFanoutStripeSettings.StripeCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_NET_FANOUT_STRIPES", previous);
        }
    }

    [Fact]
    public void Invalid_explicit_stripe_config_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetFanoutStripes(stripeCount: 0, queueCapacity: 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetFanoutStripes(stripeCount: 65, queueCapacity: 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetFanoutStripes(stripeCount: 2, queueCapacity: 0));
    }

    [Fact]
    public void Backlog_and_slow_client_counters_reach_the_metrics_text()
    {
        using var metrics = new MetricsCollector();
        var before = metrics.Snapshot();
        FineageTelemetry.FanoutBacklogSoftExceeded.Add(2);
        FineageTelemetry.FanoutBacklogEmergency.Add(1);
        FineageTelemetry.OutboundSlowClientDisconnect.Add(1, new KeyValuePair<string, object?>("endpoint", "zone"));
        FineageTelemetry.OutboundShedBytes.Add(512, new KeyValuePair<string, object?>("lane", "visual"));
        var after = metrics.Snapshot();

        // The meter is process-wide: other tests may add too, so check at least this test's increments.
        Assert.True(after.FanoutBacklogSoftExceeded - before.FanoutBacklogSoftExceeded >= 2);
        Assert.True(after.FanoutBacklogEmergency - before.FanoutBacklogEmergency >= 1);
        Assert.True(after.OutboundSlowClientDisconnect - before.OutboundSlowClientDisconnect >= 1);
        Assert.True(after.OutboundShedBytes - before.OutboundShedBytes >= 512);
        string text = MetricsText.Prometheus(after, 0, 0);
        Assert.Contains("fineage_fanout_backlog_soft_exceeded_total ", text);
        Assert.Contains("fineage_fanout_backlog_emergency_total ", text);
        Assert.Contains("fineage_outbound_slow_client_disconnect_total ", text);
        Assert.Contains("fineage_outbound_shed_bytes_total ", text);
    }
}
