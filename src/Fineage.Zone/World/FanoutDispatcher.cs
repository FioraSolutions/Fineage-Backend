using System.Diagnostics;
using System.Threading.Channels;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Delivers sealed world fan-out batches off the simulation thread. PR2 adds connection stripes; PR1 uses one
/// bounded queue and worker with observable depth/age.
/// </summary>
internal sealed class FanoutDispatcher : IDisposable
{
    const int BatchQueueCapacity = 8192;

    readonly Channel<SealedWorldFanoutBatch> _queue = Channel.CreateBounded<SealedWorldFanoutBatch>(
        new BoundedChannelOptions(BatchQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    readonly Task _worker;
    readonly CancellationTokenSource _cts = new();
    int _closed;
    int _inFlight;
    int _pending;
    long _oldestEnqueueTicks;
    Exception? _propagated;

    /// <summary>Tests: rethrow the first fan-out send failure from <see cref="WaitUntilIdle"/>.</summary>
    internal bool FailFast;

    public FanoutDispatcher()
    {
        _worker = Task.Run(ProcessAsync);
    }

    /// <summary>Non-blocking enqueue. Returns false when the queue is full or the dispatcher is closed.</summary>
    public bool TryEnqueue(SealedWorldFanoutBatch batch)
    {
        if (Volatile.Read(ref _closed) != 0)
            return false;

        long now = Stopwatch.GetTimestamp();
        if (batch.EnqueuedAtTimestamp > 0)
            FanoutTelemetry.RecordEnqueue(now);

        if (!_queue.Writer.TryWrite(batch))
        {
            FineageTelemetry.FanoutQueueOverflow.Add(1);
            FanoutTelemetry.RecordQueueDepth(_queue.Reader.CanCount ? _queue.Reader.Count : BatchQueueCapacity);
            return false;
        }

        Interlocked.Increment(ref _pending);
        FanoutTelemetry.RecordQueueDepth(_queue.Reader.Count);
        if (batch.EnqueuedAtTimestamp > 0)
        {
            long prevOldest = Volatile.Read(ref _oldestEnqueueTicks);
            if (prevOldest == 0 || batch.EnqueuedAtTimestamp < prevOldest)
                Interlocked.Exchange(ref _oldestEnqueueTicks, batch.EnqueuedAtTimestamp);
        }

        return true;
    }

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
            await foreach (var batch in _queue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                FanoutTelemetry.RecordQueueDepth(_queue.Reader.Count);
                Volatile.Write(ref _oldestEnqueueTicks, 0);
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
        catch (OperationCanceledException)
        {
        }
    }

    void ExecuteBatch(SealedWorldFanoutBatch batch)
    {
        long started = Stopwatch.GetTimestamp();
        double queueAgeMs = batch.EnqueuedAtTimestamp > 0 ? ElapsedMs(batch.EnqueuedAtTimestamp, started) : 0;
        if (queueAgeMs > 0)
            FineageTelemetry.FanoutQueueAgeMilliseconds.Record(queueAgeMs);

        bool commandBatch = batch.EnqueuedAtTimestamp > 0;
        if (commandBatch)
            CommandFanoutTimingContext.BeginCommandBatch(batch.EnqueuedAtTimestamp);
        try
        {
            int events = 0;
            foreach (var (name, send) in batch.Sends())
            {
                try
                {
                    send();
                    events++;
                }
                catch (Exception ex)
                {
                    FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", "fanout"));
                    ZoneLog.Warn($"world fan-out {name} failed: {ex}");
                    if (FailFast)
                        _propagated ??= ex;
                }
            }

            FineageTelemetry.FanoutEventsPerBatch.Record(events);
        }
        finally
        {
            if (commandBatch)
                CommandFanoutTimingContext.EndCommandBatch();
        }

        double processMs = ElapsedMs(started);
        FineageTelemetry.FanoutBatchProcessMilliseconds.Record(processMs);
        FineageTelemetry.TickStepFanoutDrainMilliseconds.Record(processMs);
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

/// <summary>Records fan-out queue depth and oldest-batch age for Prometheus export.</summary>
internal static class FanoutTelemetry
{
    static readonly object Gate = new();
    static int _depth;
    static long _oldestEnqueueTicks;

    public static void RecordEnqueue(long enqueueTicks)
    {
        lock (Gate)
        {
            if (_oldestEnqueueTicks == 0 || enqueueTicks < _oldestEnqueueTicks)
                _oldestEnqueueTicks = enqueueTicks;
        }
    }

    public static void RecordQueueDepth(int depth)
    {
        int clamped = Math.Max(0, depth);
        Interlocked.Exchange(ref _depth, clamped);
        FineageTelemetry.FanoutQueueDepth.Record(clamped);

        long oldest;
        lock (Gate)
            oldest = _oldestEnqueueTicks;
        if (oldest > 0)
        {
            double ageMs = (Stopwatch.GetTimestamp() - oldest) * 1000.0 / Stopwatch.Frequency;
            FineageTelemetry.FanoutQueueAgeMilliseconds.Record(Math.Max(0, ageMs));
        }
    }
}