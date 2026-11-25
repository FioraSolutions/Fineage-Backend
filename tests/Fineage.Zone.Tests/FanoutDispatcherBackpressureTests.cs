using System.Collections.Concurrent;
using System.Diagnostics;
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

    [Fact]
    public void Backlog_above_the_hard_send_work_limit_counts_an_emergency_even_below_the_batch_limit()
    {
        var holdWorker = new ManualResetEventSlim(false);
        using var dispatcher = new FanoutDispatcher(
            softBacklogBatches: 1000,
            hardBacklogBatches: 2000,
            softBacklogWork: 10,
            hardBacklogWork: 20)
        {
            HoldWorkerBeforeReadForTest = holdWorker,
        };

        dispatcher.Enqueue(WorkBatch(1, work: 12));
        dispatcher.Enqueue(WorkBatch(2, work: 12));

        // Two batches is far below the batch limits; the send work is what crossed them. The counters are exclusive:
        // the first enqueue crossed only the soft limit, the second crossed the hard one and counted as an emergency.
        Assert.Equal(2, dispatcher.Backlog);
        Assert.Equal(24, dispatcher.BacklogWork);
        Assert.Equal(1, dispatcher.SoftExceededEnqueues);
        Assert.Equal(1, dispatcher.EmergencyEnqueues);

        holdWorker.Set();
        dispatcher.WaitUntilIdle();
        Assert.Equal(0, dispatcher.BacklogWork);
    }

    [Fact]
    public void Invalid_send_work_limits_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FanoutDispatcher(softBacklogWork: 0, hardBacklogWork: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FanoutDispatcher(softBacklogWork: 20, hardBacklogWork: 10));
    }

    [Fact]
    public void Batch_sheds_its_oldest_visual_writes_until_it_fits_its_send_work_budget()
    {
        var batch = new WorldFanoutBatch(softSendWork: 30);
        try
        {
            for (int i = 0; i < 5; i++)
            {
                var audience = AudienceSnapshots.Capture([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], sort: false);
                batch.AddOrdered(FanoutKind.SocialAction, audience, new FanoutArgs(A: i));
            }

            // Each visual costs its 10 watchers: five of them are 50 work against a budget of 30, so the two oldest
            // go and the batch settles at 30 with the three newest visuals kept.
            Assert.Equal(2, batch.TakeDroppedVisuals());
            Assert.Equal(3, batch.Count);
            Assert.Equal(30, batch.SendWork);
        }
        finally
        {
            batch.ReleaseAudiences();
        }
    }

    [Fact]
    public void Batch_never_sheds_a_gameplay_write_to_fit_its_send_work_budget()
    {
        var batch = new WorldFanoutBatch(softSendWork: 10);
        try
        {
            var deathAudience = AudienceSnapshots.Capture([1, 2, 3, 4, 5], sort: false);
            var firstVisualAudience = AudienceSnapshots.Capture([1, 2, 3, 4, 5], sort: false);
            var secondVisualAudience = AudienceSnapshots.Capture([1, 2, 3, 4, 5], sort: false);
            batch.AddOrdered(FanoutKind.Die, deathAudience, new FanoutArgs(A: 1));
            batch.AddOrdered(FanoutKind.SocialAction, firstVisualAudience, new FanoutArgs(A: 2));
            batch.AddOrdered(FanoutKind.SocialAction, secondVisualAudience, new FanoutArgs(A: 3));

            // Three writes of 5 watchers are 15 work against a budget of 10. The oldest visual is the only shed
            // candidate: it goes, the death and the newest visual stay. The batch then sits exactly at its budget.
            Assert.Equal(1, batch.TakeDroppedVisuals());
            Assert.Equal(2, batch.Count);
            Assert.Equal(10, batch.SendWork);
        }
        finally
        {
            batch.ReleaseAudiences();
        }
    }

    [Fact]
    public void Overflow_drops_a_visual_only_batch_instead_of_growing_the_backlog()
    {
        var holdWorker = new ManualResetEventSlim(false);
        using var dispatcher = new FanoutDispatcher(
            softBacklogBatches: 1000,
            hardBacklogBatches: 2000,
            softBacklogWork: 10,
            hardBacklogWork: 20)
        {
            HoldWorkerBeforeReadForTest = holdWorker,
        };

        // Fills the backlog to its hard limit with a reliable write, which is never dropped.
        dispatcher.Enqueue(WorkBatch(1, work: 20));
        Assert.Equal(20, dispatcher.BacklogWork);

        // A batch of nothing but animations would cross the limit, so the hand-off drops it whole.
        dispatcher.Enqueue(VisualBatch(2, work: 10));

        Assert.Equal(1, dispatcher.Backlog);
        Assert.Equal(20, dispatcher.BacklogWork);
        Assert.Equal(0, dispatcher.EmergencyEnqueues);

        holdWorker.Set();
        dispatcher.WaitUntilIdle();
        Assert.Equal(0, dispatcher.BacklogWork);
    }

    [Fact]
    public void Overflow_sheds_visuals_from_a_mixed_batch_and_keeps_its_gameplay_writes()
    {
        var holdWorker = new ManualResetEventSlim(false);
        using var dispatcher = new FanoutDispatcher(
            softBacklogBatches: 1000,
            hardBacklogBatches: 2000,
            softBacklogWork: 10,
            hardBacklogWork: 20)
        {
            HoldWorkerBeforeReadForTest = holdWorker,
        };

        dispatcher.Enqueue(WorkBatch(1, work: 10));
        Assert.Equal(10, dispatcher.BacklogWork);

        // 1 reliable write plus 3 visuals of 10 watchers each is 40 work. The hand-off has 10 units of room, so the
        // visuals go and the reliable write stays queued behind the batch already in flight.
        dispatcher.Enqueue(MixedBatch(2, reliableWork: 10, visualWork: 10, visuals: 3));

        Assert.Equal(2, dispatcher.Backlog);
        Assert.Equal(20, dispatcher.BacklogWork);

        holdWorker.Set();
        dispatcher.WaitUntilIdle();
        Assert.Equal(0, dispatcher.BacklogWork);
    }

    [Fact]
    public void Sustained_lag_keeps_the_backlog_bounded_and_the_enqueue_never_blocks()
    {
        using var dispatcher = new FanoutDispatcher(
            softBacklogBatches: 16,
            hardBacklogBatches: 32,
            softBacklogWork: 200,
            hardBacklogWork: 400)
        {
            // 20 ms per batch: the worker is far slower than the producer below, so the hand-off must fall behind.
            BatchDelayForTest = TimeSpan.FromMilliseconds(20),
        };

        const int batches = 400;
        long worstEnqueueTicks = 0;
        long backlogHigh = 0;
        long workHigh = 0;
        for (int i = 0; i < batches; i++)
        {
            long started = Stopwatch.GetTimestamp();
            dispatcher.Enqueue(VisualBatch((ulong)i, work: 20));
            worstEnqueueTicks = Math.Max(worstEnqueueTicks, Stopwatch.GetTimestamp() - started);
            backlogHigh = Math.Max(backlogHigh, dispatcher.Backlog);
            workHigh = Math.Max(workHigh, dispatcher.BacklogWork);
        }

        double worstEnqueueMs = worstEnqueueTicks * 1000.0 / Stopwatch.Frequency;
        // The producer is never blocked and the hand-off stays bounded by the hard limits, so memory cannot grow
        // without limit while the worker lags. Visual-only batches are dropped at the limit rather than queued.
        Assert.True(worstEnqueueMs < 50, $"enqueue blocked for {worstEnqueueMs:0.0} ms");
        Assert.True(backlogHigh <= dispatcher.HardBacklog, $"backlog reached {backlogHigh} batches");
        Assert.True(workHigh <= dispatcher.HardBacklogWork, $"backlog work reached {workHigh}");
        Assert.True(dispatcher.OverflowDrops > 0, "the lagging worker should have forced the overflow policy to act");

        dispatcher.WaitUntilIdle(TimeSpan.FromSeconds(60));
        Assert.Equal(0, dispatcher.Backlog);
        Assert.Equal(0, dispatcher.BacklogWork);
    }

    static SealedWorldFanoutBatch WorkBatch(ulong sequence, int work)
    {
        var batch = new WorldFanoutBatch(softSendWork: work + 1);
        var audience = AudienceSnapshots.Capture(Enumerable.Range(1, work).ToArray(), sort: false);
        batch.AddOrdered(FanoutKind.Die, audience, new FanoutArgs(A: 1));
        return new SealedWorldFanoutBatch(batch, new WorldEventSequence(42, 3, sequence), 0);
    }

    static SealedWorldFanoutBatch VisualBatch(ulong sequence, int work)
    {
        var batch = new WorldFanoutBatch(softSendWork: work + 1);
        var audience = AudienceSnapshots.Capture(Enumerable.Range(1, work).ToArray(), sort: false);
        batch.AddOrdered(FanoutKind.SocialAction, audience, new FanoutArgs(A: 1));
        return new SealedWorldFanoutBatch(batch, new WorldEventSequence(42, 3, sequence), 0);
    }

    static SealedWorldFanoutBatch MixedBatch(ulong sequence, int reliableWork, int visualWork, int visuals)
    {
        var batch = new WorldFanoutBatch(softSendWork: reliableWork + visualWork * visuals + 1);
        batch.AddOrdered(FanoutKind.Die,
            AudienceSnapshots.Capture(Enumerable.Range(1, reliableWork).ToArray(), sort: false),
            new FanoutArgs(A: 0));
        for (int i = 0; i < visuals; i++)
        {
            batch.AddOrdered(FanoutKind.SocialAction,
                AudienceSnapshots.Capture(Enumerable.Range(1, visualWork).ToArray(), sort: false),
                new FanoutArgs(A: i + 1));
        }

        return new SealedWorldFanoutBatch(batch, new WorldEventSequence(42, 3, sequence), 0);
    }
}