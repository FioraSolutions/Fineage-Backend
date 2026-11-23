using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Delivers sealed world fan-out batches off the simulation thread, in seal order, on one worker.
/// <para>
/// The simulation thread never waits here and never delivers a batch itself. The hand-off is an unbounded
/// single-reader channel: <see cref="Enqueue"/> is a short producer-only lock plus <c>TryWrite</c>, which cannot
/// fail for capacity. Backpressure for slow clients lives where the bytes are, in each connection's byte-bounded
/// <c>OutboundMailbox</c> (shed low-priority frames, then disconnect that client). Everything the worker calls
/// downstream (stripe <c>TryWrite</c>, mailbox admission) is non-blocking, so the worker can only fall behind on CPU,
/// never wait on a client.
/// </para>
/// <para>
/// A backlog above the soft limit is counted (<c>fineage_fanout_backlog_soft_exceeded_total</c>) and logged once a
/// second. Above the hard limit every enqueue is counted on <c>fineage_fanout_backlog_emergency_total</c> and logged
/// as an error that names the batch the worker is stuck on. That counter must stay 0. The batch is still queued:
/// delivering it inline would reorder it ahead of older queued batches and move the worker's cost into the tick,
/// and dropping it would lose reliable gameplay frames.
/// </para>
/// </summary>
internal sealed class FanoutDispatcher : IDisposable
{
    public const int DefaultSoftBacklogBatches = 2048;
    public const int DefaultHardBacklogBatches = 8192;
    const string SoftBacklogEnv = "FINEAGE_ZONE_FANOUT_BACKLOG_SOFT";
    const string HardBacklogEnv = "FINEAGE_ZONE_FANOUT_BACKLOG_HARD";

    readonly Channel<SealedWorldFanoutBatch> _queue;
    readonly Task _worker;
    readonly CancellationTokenSource _cts = new();
    readonly object _enqueueOrder = new();
    readonly ConcurrentQueue<long> _enqueuedAt = new();
    readonly int _softBacklog;
    readonly int _hardBacklog;
    int _closed;
    int _inFlight;
    int _pending;
    long _inFlightSinceTimestamp;
    long _inFlightTick;
    long _lastSoftLogMs;
    long _lastHardLogMs;
    Exception? _propagated;

    /// <summary>Tests: rethrow the first fan-out send failure from <see cref="WaitUntilIdle"/>.</summary>
    internal bool FailFast;

    /// <summary>Tests with <see cref="DispatchProxy"/> Net bridges: deliver batches on the simulation thread.</summary>
    internal bool DeliverSynchronously;

    /// <summary>Tests: worker blocks before dequeuing batches (backlog grows).</summary>
    internal ManualResetEventSlim? HoldWorkerBeforeReadForTest;

    public FanoutDispatcher(int? softBacklogBatches = null, int? hardBacklogBatches = null)
    {
        _softBacklog = softBacklogBatches ?? ReadLimit(SoftBacklogEnv, DefaultSoftBacklogBatches);
        _hardBacklog = hardBacklogBatches ?? ReadLimit(HardBacklogEnv, DefaultHardBacklogBatches);
        if (_softBacklog < 1)
            throw new ArgumentOutOfRangeException(nameof(softBacklogBatches), _softBacklog, "fan-out soft backlog must be at least 1 batch");
        if (_hardBacklog < _softBacklog)
            throw new ArgumentOutOfRangeException(nameof(hardBacklogBatches), _hardBacklog,
                $"fan-out hard backlog ({_hardBacklog}) must be at least the soft backlog ({_softBacklog})");

        _queue = Channel.CreateUnbounded<SealedWorldFanoutBatch>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        _worker = Task.Run(ProcessAsync);
    }

    static int ReadLimit(string name, int fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        if (!int.TryParse(text, out int value) || value < 1)
            throw new InvalidOperationException($"{name}='{text}' is not a positive batch count");
        return value;
    }

    internal int SoftBacklog => _softBacklog;
    internal int HardBacklog => _hardBacklog;

    /// <summary>Batches handed over and not yet finished by the worker.</summary>
    internal int Backlog => Volatile.Read(ref _pending);

    /// <summary>
    /// Hands a sealed batch to the worker. Never blocks and never runs the batch on the caller (except the test-only
    /// <see cref="DeliverSynchronously"/> mode). Throws only when the dispatcher is closed, naming the batch.
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

        long started = Stopwatch.GetTimestamp();
        int backlog;
        // Producer-only lock: keeps the age side queue in channel order when several threads seal.
        // The worker never takes it, so it cannot make the simulation thread wait on delivery.
        lock (_enqueueOrder)
        {
            if (!_queue.Writer.TryWrite(batch))
            {
                RejectUnrecoverable(batch, "channel completed");
                throw RejectException(batch, "channel completed");
            }

            NoteEnqueued();
            backlog = Interlocked.Increment(ref _pending);
        }

        double handoffMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        FineageTelemetry.FanoutDispatcherEnqueueWaitMilliseconds.Record(handoffMs);
        if (backlog > _softBacklog)
            NoteBacklog(batch, backlog);
        PublishQueue();
    }

    void NoteBacklog(SealedWorldFanoutBatch batch, int backlog)
    {
        long nowMs = Environment.TickCount64;
        if (backlog > _hardBacklog)
        {
            FineageTelemetry.FanoutBacklogEmergency.Add(1);
            if (TryRateLimit(ref _lastHardLogMs, nowMs))
                ZoneLog.Error(
                    $"fan-out dispatcher backlog {backlog} batches is above the hard limit {_hardBacklog} ({HardBacklogEnv}); " +
                    $"queued tick={batch.Sequence.Tick} seq={batch.Sequence.Sequence}; {DescribeWorker()}. " +
                    "Batches stay queued in order; the worker is not keeping up with the world.");
            return;
        }

        FineageTelemetry.FanoutBacklogSoftExceeded.Add(1);
        if (TryRateLimit(ref _lastSoftLogMs, nowMs))
            ZoneLog.Warn(
                $"fan-out dispatcher backlog {backlog} batches is above the soft limit {_softBacklog} ({SoftBacklogEnv}); {DescribeWorker()}");
    }

    static bool TryRateLimit(ref long last, long nowMs)
    {
        long previous = Volatile.Read(ref last);
        return nowMs - previous >= 1000 && Interlocked.CompareExchange(ref last, nowMs, previous) == previous;
    }

    string DescribeWorker()
    {
        long since = Volatile.Read(ref _inFlightSinceTimestamp);
        if (Volatile.Read(ref _inFlight) == 0 || since == 0)
            return "worker idle between batches";
        double ms = (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;
        return $"worker has been on batch tick={Volatile.Read(ref _inFlightTick)} for {ms:0.0} ms";
    }

    void NoteEnqueued() => _enqueuedAt.Enqueue(Stopwatch.GetTimestamp());

    void NoteDequeued() => _enqueuedAt.TryDequeue(out _);

    void PublishQueue()
    {
        // Batches handed over and not yet picked up by the worker (unbounded channels do not count).
        int depth = Math.Max(0, Volatile.Read(ref _pending) - Volatile.Read(ref _inFlight));
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

    internal string DescribeQueue() =>
        $"fanout backlog pending={Volatile.Read(ref _pending)} inFlight={Volatile.Read(ref _inFlight)} soft={_softBacklog} hard={_hardBacklog}; {DescribeWorker()}";

    /// <summary>Test and shutdown: wait until the worker queue and in-flight batch are empty.</summary>
    public void WaitUntilIdle(TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(60);
        var sw = Stopwatch.StartNew();
        var spin = new SpinWait();
        while (sw.Elapsed < limit)
        {
            if (Volatile.Read(ref _inFlight) == 0 && Volatile.Read(ref _pending) == 0)
            {
                if (_propagated != null)
                    throw new InvalidOperationException("fan-out delivery failed", _propagated);
                return;
            }
            spin.SpinOnce();
        }

        throw new TimeoutException(
            $"fan-out dispatcher did not drain within {limit.TotalSeconds:0}s (pending={Volatile.Read(ref _pending)}, inFlight={Volatile.Read(ref _inFlight)}; {DescribeWorker()})");
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
                    Volatile.Write(ref _inFlightTick, batch.Sequence.Tick);
                    Volatile.Write(ref _inFlightSinceTimestamp, Stopwatch.GetTimestamp());
                    Interlocked.Increment(ref _inFlight);
                    try
                    {
                        ExecuteBatch(batch);
                    }
                    finally
                    {
                        Volatile.Write(ref _inFlightSinceTimestamp, 0);
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
                var cursor = batch.OpenCursor();
                while (cursor.MoveNext())
                {
                    try
                    {
                        cursor.Send();
                        events++;
                        FineageTelemetry.FanoutRecipientsPerEvent.Record(CommandFanoutTimingContext.TakeRecipients());
                    }
                    catch (Exception ex)
                    {
                        CommandFanoutTimingContext.TakeRecipients();
                        FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", "fanout"));
                        ZoneLog.Warn($"world fan-out {cursor.Kind} failed: {ex}");
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
                    RecordSubmitToMailbox(batch.CommandSubmits);
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

    static void RecordSubmitToMailbox(CommandSubmitMark[]? submits)
    {
        if (submits == null || submits.Length == 0)
            return;
        long now = Stopwatch.GetTimestamp();
        bool trace = FineageTelemetry.Activity.HasListeners();
        foreach (var mark in submits)
        {
            double ms = Math.Max(0, (now - mark.SubmittedAtTicks) * 1000.0 / Stopwatch.Frequency);
            FineageTelemetry.CommandTimelineMilliseconds.Record(ms,
                new KeyValuePair<string, object?>("phase", "submit_to_mailbox"));
            if (!trace)
                continue;
            using var span = FineageTelemetry.Activity.StartActivity("zone.command.submit_to_mailbox");
            span?.SetTag("fineage.correlation_id", mark.CorrelationId);
        }
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
