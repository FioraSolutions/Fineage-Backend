using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class FanoutDispatcherBackpressureTests
{
    [Fact]
    public void Enqueue_never_blocks_or_delivers_inline_past_the_hard_backlog_and_keeps_order()
    {
        var holdWorker = new ManualResetEventSlim(false);
        using var dispatcher = new FanoutDispatcher(softBacklogBatches: 2, hardBacklogBatches: 4)
        {
            HoldWorkerBeforeReadForTest = holdWorker,
        };
        var delivered = new ConcurrentQueue<ulong>();
        int callerThread = Environment.CurrentManagedThreadId;
        int inlineDeliveries = 0;

        var enqueue = Task.Run(() =>
        {
            int producer = Environment.CurrentManagedThreadId;
            for (ulong n = 1; n <= 10; n++)
            {
                ulong id = n;
                dispatcher.Enqueue(Batch(id, () =>
                {
                    if (Environment.CurrentManagedThreadId == producer)
                        Interlocked.Increment(ref inlineDeliveries);
                    delivered.Enqueue(id);
                }));
            }
        });
        Assert.True(enqueue.Wait(TimeSpan.FromSeconds(2)), "enqueue must return while the worker is held");
        Assert.Empty(delivered);
        Assert.Equal(10, dispatcher.Backlog);

        holdWorker.Set();
        dispatcher.WaitUntilIdle();

        Assert.Equal(0, inlineDeliveries);
        Assert.Equal(Enumerable.Range(1, 10).Select(n => (ulong)n).ToArray(), delivered.ToArray());
        // Instance counters, not the process-wide meter: other tests publish to the same instruments in parallel.
        Assert.Equal(6, dispatcher.EmergencyEnqueues);
        Assert.Equal(2, dispatcher.SoftExceededEnqueues);
        Assert.NotEqual(0, callerThread);
    }

    [Fact]
    public void Invalid_backlog_limits_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FanoutDispatcher(softBacklogBatches: 0, hardBacklogBatches: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FanoutDispatcher(softBacklogBatches: 8, hardBacklogBatches: 4));
    }

    [Fact]
    public void Enqueue_after_close_logs_sequence_and_throws_without_delivering_the_batch()
    {
        using var dispatcher = new FanoutDispatcher();
        var delivered = new ConcurrentQueue<ulong>();

        dispatcher.Enqueue(Batch(1, () => delivered.Enqueue(1)));
        dispatcher.Dispose();

        var rejected = Batch(99, () => delivered.Enqueue(99));
        var ex = Assert.Throws<InvalidOperationException>(() => dispatcher.Enqueue(rejected));
        Assert.Contains("tick=42", ex.Message);
        Assert.Contains("phase=3", ex.Message);
        Assert.Contains("seq=99", ex.Message);
        Assert.DoesNotContain(delivered, n => n == 99);
    }

    [Fact]
    public void Queue_age_is_the_batch_still_waiting_not_the_first_batch_ever_enqueued()
    {
        var ages = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName &&
                instrument.Name == "fineage.fanout.queue.oldest_age.ms")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            if (instrument.Name == "fineage.fanout.queue.oldest_age.ms")
                ages.Add(value);
        });
        listener.Start();

        using var dispatcher = new FanoutDispatcher();
        dispatcher.Enqueue(Batch(1, () => { }));
        dispatcher.WaitUntilIdle();
        Thread.Sleep(300);
        int beforeSecond = ages.Count;
        dispatcher.Enqueue(Batch(2, () => { }));
        dispatcher.WaitUntilIdle();

        Assert.True(ages.Count > beforeSecond);
        Assert.All(ages.Skip(beforeSecond), age => Assert.True(age < 250, $"age {age} ms still includes a drained batch"));
    }

    static SealedWorldFanoutBatch Batch(ulong sequence, Action send)
    {
        var batch = new WorldFanoutBatch();
        batch.AddPrepared(send);
        return new SealedWorldFanoutBatch(batch, new WorldEventSequence(42, 3, sequence), 0);
    }
}