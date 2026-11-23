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
}
