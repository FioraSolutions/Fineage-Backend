using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Delivers sealed world fan-out batches off the simulation thread. PR2 adds connection stripes; PR1 uses one
/// bounded queue and worker with observable depth/age.
/// </summary>
internal sealed class FanoutDispatcher : IDisposable
{
    const int DefaultBatchQueueCapacity = 8192;

    readonly Channel<SealedWorldFanoutBatch> _queue;
    readonly Task _worker;
    readonly CancellationTokenSource _cts = new();
    readonly object _enqueueOrder = new();
    readonly ConcurrentQueue<long> _enqueuedAt = new();
    int _closed;
    int _inFlight;
    int _pending;
    Exception? _propagated;

    /// <summary>Tests: rethrow the first fan-out send failure from <see cref="WaitUntilIdle"/>.</summary>
    internal bool FailFast;

    /// <summary>Tests with <see cref="DispatchProxy"/> Net bridges: deliver batches on the simulation thread.</summary>
    internal bool DeliverSynchronously;

    /// <summary>Tests: worker blocks before dequeuing batches (channel stays full).</summary>
    internal ManualResetEventSlim? HoldWorkerBeforeReadForTest;

    public FanoutDispatcher(int batchQueueCapacity = DefaultBatchQueueCapacity)
    {
        if (batchQueueCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(batchQueueCapacity));

        _queue = Channel.CreateBounded<SealedWorldFanoutBatch>(
            new BoundedChannelOptions(batchQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _worker = Task.Run(ProcessAsync);
    }

    /// <summary>
    /// Enqueues a sealed batch. Blocks the caller when the queue is full (backpressure). Throws when the
    /// dispatcher is closed so the batch is never silently dropped.
    /// </summary>
    public void Enqueue(SealedWorldFanoutBatch batch)
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            RejectUnrecoverable(batch, "dispatcher is closed");
            throw RejectException(batch, "dispatcher is closed");
        }

        if (DeliverSynchronously)
        {
            ExecuteBatch(batch);
            return;
        }

        long waitStarted = Stopwatch.GetTimestamp();
        // Push the enqueue timestamp before the channel write, under one lock, so the side queue
        // stays in the same order as the channel when several threads seal batches.
        lock (_enqueueOrder)
        {
            NoteEnqueued();
            try
            {
                _queue.Writer.WriteAsync(batch, _cts.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (ChannelClosedException ex)
            {
                NoteDequeued();
                RejectUnrecoverable(batch, "channel closed");
                throw RejectException(batch, "channel closed", ex);
            }
            catch (OperationCanceledException ex) when (_cts.IsCancellationRequested)
            {
                NoteDequeued();
                RejectUnrecoverable(batch, "dispatcher shut down");
                throw RejectException(batch, "dispatcher shut down", ex);
            }
        }

        double enqueueWaitMs = (Stopwatch.GetTimestamp() - waitStarted) * 1000.0 / Stopwatch.Frequency;
        FineageTelemetry.FanoutDispatcherEnqueueWaitMilliseconds.Record(enqueueWaitMs);
        Interlocked.Increment(ref _pending);
        PublishQueue();
    }

    void NoteEnqueued() => _enqueuedAt.Enqueue(Stopwatch.GetTimestamp());

    void NoteDequeued() => _enqueuedAt.TryDequeue(out _);

    void PublishQueue()
    {
        int depth = Math.Max(0, _queue.Reader.Count);
        double ageMs = 0;
        if (depth > 0 && _enqueuedAt.TryPeek(out long oldest))
            ageMs = Math.Max(0, (Stopwatch.GetTimestamp() - oldest) * 1000.0 / Stopwatch.Frequency);
        FineageTelemetry.FanoutQueueDepth.Record(depth);
        FineageTelemetry.FanoutQueueAgeMilliseconds.Record(ageMs);
    }

    static void RejectUnrecoverable(SealedWorldFanoutBatch batch, string reason)
    {
        FineageTelemetry.FanoutQueueOverflow.Add(1);
        ZoneLog.Error(
            $"fan-out dispatcher rejected batch tick={batch.Sequence.Tick} phase={batch.Sequence.Phase} seq={batch.Sequence.Sequence}: {reason}");
    }

    static InvalidOperationException RejectException(SealedWorldFanoutBatch batch, string reason, Exception? inner = null) =>
        new(
            $"fan-out dispatcher rejected batch tick={batch.Sequence.Tick} phase={batch.Sequence.Phase} seq={batch.Sequence.Sequence}: {reason}",
            inner);

    /// <summary>Test and shutdown: wait until the worker queue and in-flight batch are empty.</summary>
    public void WaitUntilIdle(TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(60);
        var sw = Stopwatch.StartNew();
        var spin = new SpinWait();
        while (sw.Elapsed < limit)
        {
            int depth = _queue.Reader.CanCount ? _queue.Reader.Count : 0;
            if (depth == 0 && Volatile.Read(ref _inFlight) == 0 && Volatile.Read(ref _pending) == 0)
            {
                if (_propagated != null)
                    throw new InvalidOperationException("fan-out delivery failed", _propagated);
                return;
            }
            spin.SpinOnce();
        }

        throw new TimeoutException(
            $"fan-out dispatcher did not drain within {limit.TotalSeconds:0}s (depth={_queue.Reader.Count}, inFlight={Volatile.Read(ref _inFlight)})");
    }

    async Task ProcessAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                HoldWorkerBeforeReadForTest?.Wait(_cts.Token);
                if (!await _queue.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                    break;
                while (_queue.Reader.TryRead(out var batch))
                {
                    NoteDequeued();
                    PublishQueue();
                    Interlocked.Increment(ref _inFlight);
                    try
                    {
                        ExecuteBatch(batch);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _inFlight);
                        Interlocked.Decrement(ref _pending);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void ExecuteBatch(SealedWorldFanoutBatch batch)
    {
        long started = Stopwatch.GetTimestamp();
        bool commandBatch = batch.EnqueuedAtTimestamp > 0;
        CommandFanoutTimingContext.BeginDelivery();
        if (commandBatch)
            CommandFanoutTimingContext.BeginCommandBatch(batch.EnqueuedAtTimestamp);
        using var batchActivity = FineageTelemetry.Activity.StartActivity("zone.fanout.batch");
        try
        {
            int events = 0;
            // One stripe enqueue per connection for the whole sealed batch, instead of one channel item per frame.
            using (FanoutDeliveryScope scope = FanoutDeliveryScope.Begin())
            {
                foreach (var (name, send) in batch.Sends())
                {
                    try
                    {
                        send();
                        events++;
                        FineageTelemetry.FanoutRecipientsPerEvent.Record(CommandFanoutTimingContext.TakeRecipients());
                    }
                    catch (Exception ex)
                    {
                        CommandFanoutTimingContext.TakeRecipients();
                        FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", "fanout"));
                        ZoneLog.Warn($"world fan-out {name} failed: {ex}");
                        if (FailFast)
                            _propagated ??= ex;
                    }
                }

                int connections = scope.Flush();
                if (connections > 0 && CommandFanoutTimingContext.TryRecordMailbox(out double commitToMailboxMs))
                {
                    FineageTelemetry.CommandToMailboxesMilliseconds.Record(commitToMailboxMs);
                    FineageTelemetry.CommandTimelineMilliseconds.Record(commitToMailboxMs,
                        new KeyValuePair<string, object?>("phase", "fanout_enqueue_to_mailbox"));
                }
            }

            FineageTelemetry.FanoutEventsPerBatch.Record(events);
            FineageTelemetry.FanoutQueueBytes.Record(CommandFanoutTimingContext.TakeBytes());
            if (batch.Coalesced > 0)
                FineageTelemetry.FanoutCoalesced.Add(batch.Coalesced);
        }
        finally
        {
            if (commandBatch)
                CommandFanoutTimingContext.EndCommandBatch();
            CommandFanoutTimingContext.EndDelivery();
        }

        double processMs = ElapsedMs(started);
        FineageTelemetry.FanoutBatchProcessMilliseconds.Record(processMs);
        FineageTelemetry.TickStepFanoutDrainMilliseconds.Record(processMs);
        batch.ReleaseScratchResources();
    }

    static double ElapsedMs(long fromTimestamp, long? toTimestamp = null)
    {
        long to = toTimestamp ?? Stopwatch.GetTimestamp();
        return (to - fromTimestamp) * 1000.0 / Stopwatch.Frequency;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        _queue.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            WaitUntilIdle(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            ZoneLog.Warn("fan-out dispatcher did not drain completely during shutdown");
        }

        try { _worker.Wait(TimeSpan.FromSeconds(5)); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
