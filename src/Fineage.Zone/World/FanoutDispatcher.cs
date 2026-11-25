using System.Collections.Concurrent;
using System.Diagnostics;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Delivers sealed world fan-out batches off the simulation thread, in seal order, on one dedicated worker thread.
/// <para>
/// The simulation thread never waits here and never delivers a batch itself. The hand-off is an unbounded
/// concurrent queue: <see cref="Enqueue"/> is a short producer-only lock plus an enqueue, which cannot fail for
/// capacity, and a <see cref="WorkerWakeup"/> signal. The worker is its own thread, not a thread-pool continuation:
/// an async wait put its wake-up behind socket pumps on the pool, which was most of the fan-out queue age under load.
/// The build stage stays one thread because the world-event publishers keep per-observer state (knownlists,
/// appearance) that must see batches in seal order; per-connection delivery is parallel on <c>NetFanoutStripes</c>. Backpressure for slow clients lives where the bytes are, in each connection's byte-bounded
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
    public const int DefaultSoftBacklogWork = 20_000_000;
    public const int DefaultHardBacklogWork = 80_000_000;
    const string SoftBacklogEnv = "FINEAGE_ZONE_FANOUT_BACKLOG_SOFT";
    const string HardBacklogEnv = "FINEAGE_ZONE_FANOUT_BACKLOG_HARD";
    const string SoftWorkEnv = "FINEAGE_ZONE_FANOUT_BACKLOG_SOFT_WORK";
    const string HardWorkEnv = "FINEAGE_ZONE_FANOUT_BACKLOG_HARD_WORK";

    readonly ConcurrentQueue<(SealedWorldFanoutBatch Batch, long EnqueuedAt)> _queue = new();
    readonly WorkerWakeup _wakeup = new();
    readonly Thread _worker;
    readonly CancellationTokenSource _cts = new();
    readonly object _enqueueOrder = new();
    readonly int _softBacklog;
    readonly int _hardBacklog;
    readonly int _softWork;
    readonly int _hardWork;
    int _closed;
    int _inFlight;
    int _pending;
    long _pendingWork;
    int _inFlightWork;
    long _inFlightSinceTimestamp;
    long _inFlightTick;
    long _lastSoftLogMs;
    long _lastHardLogMs;
    long _lastOverflowShedLogMs;
    Exception? _propagated;

    /// <summary>Tests: rethrow the first fan-out send failure from <see cref="WaitUntilIdle"/>.</summary>
    internal bool FailFast;

    /// <summary>Tests with <see cref="DispatchProxy"/> Net bridges: deliver batches on the simulation thread.</summary>
    internal bool DeliverSynchronously;

    /// <summary>Tests: worker blocks before dequeuing batches (backlog grows).</summary>
    internal ManualResetEventSlim? HoldWorkerBeforeReadForTest;

    /// <summary>Tests: worker spends this long on every batch, so the hand-off stays behind for a while.</summary>
    internal TimeSpan BatchDelayForTest;

    public FanoutDispatcher(
        int? softBacklogBatches = null,
        int? hardBacklogBatches = null,
        int? softBacklogWork = null,
        int? hardBacklogWork = null)
    {
        _softBacklog = softBacklogBatches ?? ReadLimit(SoftBacklogEnv, DefaultSoftBacklogBatches, "batch");
        _hardBacklog = hardBacklogBatches ?? ReadLimit(HardBacklogEnv, DefaultHardBacklogBatches, "batch");
        if (_softBacklog < 1)
            throw new ArgumentOutOfRangeException(nameof(softBacklogBatches), _softBacklog, "fan-out soft backlog must be at least 1 batch");
        if (_hardBacklog < _softBacklog)
            throw new ArgumentOutOfRangeException(nameof(hardBacklogBatches), _hardBacklog,
                $"fan-out hard backlog ({_hardBacklog}) must be at least the soft backlog ({_softBacklog})");
        _softWork = softBacklogWork ?? ReadLimit(SoftWorkEnv, DefaultSoftBacklogWork, "send work");
        _hardWork = hardBacklogWork ?? ReadLimit(HardWorkEnv, DefaultHardBacklogWork, "send work");
        if (_softWork < 1)
            throw new ArgumentOutOfRangeException(nameof(softBacklogWork), _softWork, "fan-out soft backlog work must be at least 1");
        if (_hardWork < _softWork)
            throw new ArgumentOutOfRangeException(nameof(hardBacklogWork), _hardWork,
                $"fan-out hard backlog work ({_hardWork}) must be at least the soft backlog work ({_softWork})");

        _worker = new Thread(Process) { IsBackground = true, Name = "fanout-dispatcher" };
        _worker.Start();
    }

    static int ReadLimit(string name, int fallback, string what)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        if (!int.TryParse(text, out int value) || value < 1)
            throw new InvalidOperationException($"{name}='{text}' is not a positive {what} count");
        return value;
    }

    internal int SoftBacklog => _softBacklog;
    internal int HardBacklog => _hardBacklog;
    internal int SoftBacklogWork => _softWork;
    internal int HardBacklogWork => _hardWork;

    /// <summary>Batches handed over and not yet finished by the worker.</summary>
    internal int Backlog => Volatile.Read(ref _pending);

    /// <summary>Recipient work handed over and not yet finished by the worker (queued plus the one in flight).</summary>
    internal long BacklogWork => Volatile.Read(ref _pendingWork);

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
        long work = batch.SendWork;
        // Overflow policy: an enqueue that would cross the hard backlog sheds the incoming batch's visual writes down
        // to its own budget first. Reliable gameplay writes are never dropped, so the hand-off stays bounded by the
        // number of batches and their send work instead of growing without limit while the worker lags.
        if (WouldCrossHardLimit(work))
        {
            if (TryDropVisualOnlyBatch(batch))
                return;
            work = ShedVisualsForOverflow(batch, work);
        }

        int backlog;
        long backlogWork;
        // Producer-only lock: keeps enqueue order equal to seal order when several threads seal.
        // The worker never takes it, so it cannot make the simulation thread wait on delivery.
        lock (_enqueueOrder)
        {
            backlog = Interlocked.Increment(ref _pending);
            backlogWork = Interlocked.Add(ref _pendingWork, work);
            _queue.Enqueue((batch, started));
        }

        _wakeup.Signal();

        double handoffMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        FineageTelemetry.FanoutDispatcherEnqueueWaitMilliseconds.Record(handoffMs);
        NoteDroppedVisuals(batch);
        if (backlog > _softBacklog || backlogWork > _softWork)
            NoteBacklog(batch, backlog, backlogWork);
        PublishQueue();
    }

    /// <summary>
    /// True when handing <paramref name="work"/> over would push the backlog past the hard limit. The check is a
    /// read of the producer counters without the enqueue lock: a concurrent enqueue may cross the limit first, and
    /// that enqueue sheds its own batch. Shedding is therefore never missed, only sometimes done twice.
    /// </summary>
    bool WouldCrossHardLimit(long work) =>
        Volatile.Read(ref _pending) + 1 > _hardBacklog ||
        Volatile.Read(ref _pendingWork) + work > _hardWork;

    /// <summary>
    /// Drops a batch that carries nothing but visual writes when the hand-off is already at its hard limit. The whole
    /// hand-off goes away instead of being queued behind a worker that is not keeping up: the client loses only
    /// animations and markers, which the outbound mailbox sheds for a slow client anyway. Returns true when dropped.
    /// </summary>
    bool TryDropVisualOnlyBatch(SealedWorldFanoutBatch batch)
    {
        if (!batch.HasOnlyVisualWrites())
            return false;
        int dropped = batch.TakeDroppedVisuals();
        Interlocked.Increment(ref _overflowDrops);
        FineageTelemetry.FanoutOverflowShed.Add(1);
        FineageTelemetry.FanoutQueueOverflow.Add(1);
        if (TryRateLimit(ref _lastOverflowShedLogMs, Environment.TickCount64))
            ZoneLog.Warn(
                $"fan-out hand-off is above its hard limit ({_hardBacklog} batches / {_hardWork} work); " +
                $"dropped visual-only batch tick={batch.Sequence.Tick} seq={batch.Sequence.Sequence} " +
                $"({dropped} writes already shed, {batch.Count} left). Reliable writes are never dropped here. {DescribeWorker()}");
        batch.ReleaseScratchResources();
        return true;
    }

    /// <summary>
    /// Sheds the incoming batch's visual writes so the hand-off stays inside the hard backlog. Returns the work the
    /// batch now carries. Reliable gameplay writes are never dropped, so a batch with only reliable writes goes over
    /// the limit and the emergency counter reports it — losing a gameplay frame is worse than a long backlog.
    /// </summary>
    long ShedVisualsForOverflow(SealedWorldFanoutBatch batch, long work)
    {
        // Aim the incoming batch at the room left under the hard limit: the hand-off then lands inside it.
        long room = _hardWork - Volatile.Read(ref _pendingWork);
        batch.ShedVisualsToBudget(Math.Max(1, room));
        // The batch reports its shed count once, so the later NoteDroppedVisuals sees nothing left to count.
        int shed = batch.TakeDroppedVisuals();
        if (shed == 0)
            return work;
        Interlocked.Increment(ref _overflowDrops);
        FineageTelemetry.FanoutShedVisual.Add(shed);
        FineageTelemetry.FanoutOverflowShed.Add(1);
        long remaining = batch.SendWork;
        if (TryRateLimit(ref _lastOverflowShedLogMs, Environment.TickCount64))
            ZoneLog.Warn(
                $"fan-out hand-off is above its hard limit ({_hardBacklog} batches / {_hardWork} work); " +
                $"shed {shed} visual writes from batch tick={batch.Sequence.Tick} seq={batch.Sequence.Sequence}, " +
                $"{work} -> {remaining} send work; reliable writes stay queued. {DescribeWorker()}");
        return remaining;
    }

    static void NoteDroppedVisuals(SealedWorldFanoutBatch batch)
    {
        int dropped = batch.TakeDroppedVisuals();
        if (dropped > 0)
            FineageTelemetry.FanoutShedVisual.Add(dropped);
    }

    long _emergencyEnqueues;
    long _softExceededEnqueues;
    long _overflowDrops;

    /// <summary>Hand-offs this dispatcher dropped or shed at the hard limit (visual-only drops and visual sheds).</summary>
    public long OverflowDrops => Interlocked.Read(ref _overflowDrops);

    /// <summary>Enqueues this dispatcher saw above the hard backlog limit (the emergency counter; 0 in normal runs).</summary>
    public long EmergencyEnqueues => Interlocked.Read(ref _emergencyEnqueues);

    /// <summary>Enqueues this dispatcher saw above the soft backlog limit and at or below the hard one.</summary>
    public long SoftExceededEnqueues => Interlocked.Read(ref _softExceededEnqueues);

    void NoteBacklog(SealedWorldFanoutBatch batch, int backlog, long backlogWork)
    {
        long nowMs = Environment.TickCount64;
        if (backlog > _hardBacklog || backlogWork > _hardWork)
        {
            Interlocked.Increment(ref _emergencyEnqueues);
            FineageTelemetry.FanoutBacklogEmergency.Add(1);
            if (TryRateLimit(ref _lastHardLogMs, nowMs))
                ZoneLog.Error(
                    $"fan-out dispatcher backlog {backlog} batches / {backlogWork} send work is above the hard limit " +
                    $"({_hardBacklog} batches / {_hardWork} work, {HardBacklogEnv} / {HardWorkEnv}); " +
                    $"queued tick={batch.Sequence.Tick} seq={batch.Sequence.Sequence}; {DescribeWorker()}. " +
                    "Batches stay queued in order; the worker is not keeping up with the world.");
            return;
        }

        Interlocked.Increment(ref _softExceededEnqueues);
        FineageTelemetry.FanoutBacklogSoftExceeded.Add(1);
        if (TryRateLimit(ref _lastSoftLogMs, nowMs))
            ZoneLog.Warn(
                $"fan-out dispatcher backlog {backlog} batches / {backlogWork} send work is above the soft limit " +
                $"({_softBacklog} batches / {_softWork} work, {SoftBacklogEnv} / {SoftWorkEnv}); {DescribeWorker()}");
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

    void PublishQueue()
    {
        // Batches handed over and not yet picked up by the worker.
        int depth = Math.Max(0, Volatile.Read(ref _pending) - Volatile.Read(ref _inFlight));
        long work = Math.Max(0, Volatile.Read(ref _pendingWork) - Volatile.Read(ref _inFlightWork));
        double ageMs = 0;
        if (depth > 0 && _queue.TryPeek(out var oldest))
            ageMs = Math.Max(0, (Stopwatch.GetTimestamp() - oldest.EnqueuedAt) * 1000.0 / Stopwatch.Frequency);
        FineageTelemetry.FanoutQueueDepth.Record(depth);
        FineageTelemetry.FanoutQueueWork.Record((int)Math.Min(int.MaxValue, work));
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

    void Process()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                HoldWorkerBeforeReadForTest?.Wait(_cts.Token);
                if (!_queue.TryDequeue(out var item))
                {
                    if (Volatile.Read(ref _closed) != 0)
                        break;
                    _wakeup.Park(() => !_queue.IsEmpty);
                    continue;
                }

                if (BatchDelayForTest > TimeSpan.Zero)
                    Thread.Sleep(BatchDelayForTest);

                FineageTelemetry.FanoutDispatchWaitMilliseconds.Record(ElapsedMs(item.EnqueuedAt));
                long work = item.Batch.SendWork;
                Volatile.Write(ref _inFlightTick, item.Batch.Sequence.Tick);
                Volatile.Write(ref _inFlightSinceTimestamp, Stopwatch.GetTimestamp());
                Volatile.Write(ref _inFlightWork, (int)Math.Min(int.MaxValue, work));
                Interlocked.Increment(ref _inFlight);
                PublishQueue();
                try
                {
                    ExecuteBatch(item.Batch, item.EnqueuedAt);
                }
                catch (Exception ex)
                {
                    // ExecuteBatch isolates each event; reaching here is a scope/flush fault. Name the batch, keep going.
                    ZoneLog.Error($"fan-out dispatcher failed batch tick={item.Batch.Sequence.Tick} seq={item.Batch.Sequence.Sequence}: {ex}");
                    if (FailFast)
                        _propagated ??= ex;
                }
                finally
                {
                    Volatile.Write(ref _inFlightSinceTimestamp, 0);
                    Volatile.Write(ref _inFlightWork, 0);
                    Interlocked.Decrement(ref _inFlight);
                    Interlocked.Decrement(ref _pending);
                    Interlocked.Add(ref _pendingWork, -work);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void ExecuteBatch(SealedWorldFanoutBatch batch, long enqueuedAt = 0)
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
            using (FanoutDeliveryScope scope = FanoutDeliveryScope.Begin(enqueuedAt))
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
        try
        {
            WaitUntilIdle(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            ZoneLog.Warn("fan-out dispatcher did not drain completely during shutdown");
        }

        _cts.Cancel();
        _wakeup.Stop();
        if (!_worker.Join(TimeSpan.FromSeconds(5)))
            ZoneLog.Error($"fan-out dispatcher worker did not stop within 5s; {DescribeQueue()}");
        _wakeup.Dispose();
        _cts.Dispose();
    }
}
