using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

internal enum EconomyFlushResult
{
    NoWork,
    Queued,
    QueueFull,
    ReservationConflict,
}

/// <summary>
/// Non-blocking economy durable commits. Simulation only <see cref="TryEnqueue"/>; completions are drained on the
/// simulation thread via <see cref="DrainCompletions"/>.
/// </summary>
internal sealed class EconomyPersistService
{
    internal const string EconomyCommitFailed = "economy_commit_failed: auditable economy commit failed";
    public const int DefaultQueueCapacity = 64;

    public const string PersistQueueFullReject = "persist_queue_full";

    readonly IAccountStore? _store;
    readonly ILedgerJournal _journal;
    readonly WorldAffinity _affinity;
    readonly EconomyReservation _reservations;
    readonly Channel<EconomyPersistWork> _queue;
    readonly ConcurrentQueue<EconomyPersistCompletion> _completions = new();
    readonly ConcurrentDictionary<ulong, PendingBatch> _pending = new();
    readonly Action<string> _warn;
    Thread? _thread;
    int _running;
    int _inFlight;
    int _queued;
    long _commitFailures;
    long _oldestEnqueueTimestamp;
    long _nextBatchId;

    public EconomyPersistService(IAccountStore? store, ILedgerJournal journal, WorldAffinity affinity)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(affinity);
        _store = store;
        _journal = journal;
        _affinity = affinity;
        _reservations = new EconomyReservation();
        _warn = ZoneLog.Warn;
        _queue = Channel.CreateBounded<EconomyPersistWork>(new BoundedChannelOptions(DefaultQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public EconomyReservation Reservations => _reservations;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public int PendingCount => _queued + _inFlight;

    public double OldestAgeMilliseconds
    {
        get
        {
            if (PendingCount == 0)
                return 0;
            long oldest = Volatile.Read(ref _oldestEnqueueTimestamp);
            return oldest == 0 ? 0 : Stopwatch.GetElapsedTime(oldest).TotalMilliseconds;
        }
    }

    public int LastWriterThreadId { get; private set; }

    public void Start()
    {
        FineageTelemetry.EconomyPersistPendingCount = () => PendingCount;
        FineageTelemetry.EconomyPersistOldestAgeMs = () => OldestAgeMilliseconds;
        if (_running == 1)
            return;
        _running = 1;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "fineage-economy-persist",
        };
        _thread.Start();
    }

    public void Stop()
    {
        DrainWorkerOnCallerThread();
        _running = 0;
        _queue.Writer.TryComplete();
        _thread?.Join(TimeSpan.FromSeconds(5));
        _thread = null;
    }

    /// <summary>
    /// Hand a dirty economy batch to the persist worker without blocking.
    /// This is the simulation thread's only economy-persist entry point, so it must stay
    /// non-blocking: no SQL, no wait, no <c>Drain</c>. The batch commits on the worker and
    /// reports back through the completion queue that <c>DrainCompletions</c> pumps.
    /// </summary>
    public EconomyFlushResult TryEnqueue(
        EconomyPersistBatch batch,
        int? discardWithoutPublish,
        IReadOnlyList<ZoneCommandIngress.Applied>? heldReplies,
        Action<EconomyPersistBatch> restore) =>
        TryEnqueue(batch, discardWithoutPublish, heldReplies, restore, out _);

    /// <summary>
    /// The same hand-off, also returning this batch's own outcome: it completes once <see cref="DrainCompletions"/> has
    /// finished the batch (published on success, restored on failure). Null unless the result is
    /// <see cref="EconomyFlushResult.Queued"/>.
    /// </summary>
    public EconomyFlushResult TryEnqueue(
        EconomyPersistBatch batch,
        int? discardWithoutPublish,
        IReadOnlyList<ZoneCommandIngress.Applied>? heldReplies,
        Action<EconomyPersistBatch> restore,
        out Task<bool>? finished)
    {
        ArgumentNullException.ThrowIfNull(batch);
        finished = null;

        // A world with no hosted simulation (tests, admin hooks, tools) has no worker thread. Enqueueing there would
        // leave the batch in the queue until some later drain the caller does not run, so the write is committed on
        // the caller instead — the same rule ZoneCharacterPersist uses for a world without its worker.
        if (!IsRunning)
        {
            ulong inlineId = (ulong)Interlocked.Increment(ref _nextBatchId);
            var inline = new EconomyPersistWork
            {
                BatchId = inlineId,
                Batch = batch,
                EnqueuedAt = Stopwatch.GetTimestamp(),
            };
            bool committed = Apply(inline);
            if (!committed)
                Interlocked.Increment(ref _commitFailures);
            var inlinePending = new PendingBatch(batch, discardWithoutPublish, heldReplies?.ToList(), []);
            _pending[inlineId] = inlinePending;
            _completions.Enqueue(new EconomyPersistCompletion(inlineId, committed));
            finished = inlinePending.Finished.Task;
            return EconomyFlushResult.Queued;
        }

        var charIds = CharacterIdsOf(batch);
        if (!_reservations.TryReserve(charIds))
        {
            restore(batch);
            return EconomyFlushResult.ReservationConflict;
        }

        ulong batchId = (ulong)Interlocked.Increment(ref _nextBatchId);
        var work = new EconomyPersistWork
        {
            BatchId = batchId,
            Batch = batch,
            EnqueuedAt = Stopwatch.GetTimestamp(),
        };

        // The pending entry and the queued count are published before the work is visible to the worker. The worker
        // commits the batch and enqueues its completion as soon as it reads it; a completion that arrives before its
        // entry exists is dropped by Finish, which strands the held command replies forever (the caller waits on a
        // reply no one will complete).
        var pendingBatch = new PendingBatch(batch, discardWithoutPublish, heldReplies?.ToList(), charIds);
        _pending[batchId] = pendingBatch;
        Interlocked.Increment(ref _queued);
        if (_oldestEnqueueTimestamp == 0)
            Volatile.Write(ref _oldestEnqueueTimestamp, work.EnqueuedAt);

        if (!_queue.Writer.TryWrite(work))
        {
            _pending.TryRemove(batchId, out _);
            Interlocked.Decrement(ref _queued);
            _reservations.Release(charIds);
            restore(batch);
            return EconomyFlushResult.QueueFull;
        }

        finished = pendingBatch.Finished.Task;
        return EconomyFlushResult.Queued;
    }

    public int DrainCompletions(ZoneWorld world)
    {
        int drained = 0;
        while (_completions.TryDequeue(out var completion))
        {
            drained++;
            Finish(world, completion);
        }

        return drained;
    }

    /// <summary>
    /// Logout / shutdown / off-step callers: wait for every queued batch and report whether all of them committed.
    /// The completions it pumps are world state, so the caller must already hold the world turn.
    /// </summary>
    public bool WaitForAllBatches(TimeSpan budget, ZoneWorld world)
    {
        EconomyPersistThreading.AssertNoBlockingWaitOnSimulationThread(world.InSimulationStep, "WaitForAllBatches");
        long failuresBefore = Volatile.Read(ref _commitFailures);
        long deadline = Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency);
        while (PendingCount > 0 || !_pending.IsEmpty)
        {
            DrainCompletions(world);
            if (Stopwatch.GetTimestamp() >= deadline)
                return false;
            Thread.Sleep(5);
        }

        return Volatile.Read(ref _commitFailures) == failuresBefore;
    }

    /// <summary>
    /// Off-step callers (logout, kick, an admin or request path): wait for one batch of their own, not for every batch
    /// in flight. The world turn is taken only around each <see cref="DrainCompletions"/> (completions publish world
    /// state) and never across the sleep, so the simulation step and other leavers keep running while this caller
    /// waits on Postgres. A caller that already holds the turn keeps it (the monitor is reentrant), as before.
    /// </summary>
    public bool WaitForBatch(Task<bool> finished, TimeSpan budget, ZoneWorld world)
    {
        ArgumentNullException.ThrowIfNull(finished);
        EconomyPersistThreading.AssertNoBlockingWaitOnSimulationThread(world.InSimulationStep, nameof(WaitForBatch));
        long deadline = Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency);
        while (!finished.IsCompleted)
        {
            if (!_completions.IsEmpty)
            {
                using (_affinity.Step())
                    DrainCompletions(world);
                continue;
            }

            if (Stopwatch.GetTimestamp() >= deadline)
                return false;
            finished.Wait(TimeSpan.FromMilliseconds(5));
        }

        return finished.Result;
    }

    /// <summary>
    /// Off-step: wait until no batch in flight holds a reservation on any of <paramref name="characterIds"/>, draining
    /// completions under the world turn only (same rule as <see cref="WaitForBatch"/>).
    /// </summary>
    public bool WaitForReservations(IReadOnlyCollection<int> characterIds, TimeSpan budget, ZoneWorld world)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        EconomyPersistThreading.AssertNoBlockingWaitOnSimulationThread(world.InSimulationStep, nameof(WaitForReservations));
        long deadline = Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            Task<bool>? blocking = null;
            foreach (var pending in _pending.Values)
            {
                if (pending.CharacterIds.Exists(characterIds.Contains))
                {
                    blocking = pending.Finished.Task;
                    break;
                }
            }

            if (blocking == null)
                return true;
            if (!_completions.IsEmpty)
            {
                using (_affinity.Step())
                    DrainCompletions(world);
                continue;
            }

            if (Stopwatch.GetTimestamp() >= deadline)
                return false;
            blocking.Wait(TimeSpan.FromMilliseconds(5));
        }
    }

    internal static List<int> CharacterIdsOfBatch(EconomyPersistBatch batch) => CharacterIdsOf(batch);

    void Finish(ZoneWorld world, EconomyPersistCompletion completion)
    {
        if (!_pending.TryRemove(completion.BatchId, out var pending))
            return;

        _reservations.Release(pending.CharacterIds);
        if (completion.Success)
        {
            world.PublishStolenEconomyInternal(pending.Batch, pending.DiscardWithoutPublish);
            if (pending.HeldReplies is { Count: > 0 })
                ZoneCommandIngress.Complete(pending.HeldReplies);
        }
        else
        {
            world.RestoreDirtyEconomyInternal(pending.Batch);
            // Only the characters whose economy write was in this batch hear the failure; a command from anyone else
            // that merely shared the step still answers with its own result.
            if (pending.HeldReplies is { Count: > 0 })
                ZoneCommandIngress.AnswerAfterFlush(
                    pending.HeldReplies, committed: false, pending.CharacterIds.ToHashSet(), EconomyCommitFailed);
        }

        pending.Finished.TrySetResult(completion.Success);
    }

    static List<int> CharacterIdsOf(EconomyPersistBatch batch)
    {
        var ids = new HashSet<int>();
        foreach (var (objectId, _, _) in batch.Inventories)
            ids.Add(objectId);
        foreach (var (objectId, _) in batch.Warehouses)
            ids.Add(objectId);
        foreach (var (objectId, _) in batch.Freight)
            ids.Add(objectId);
        foreach (var bag in batch.OfflineBags)
            ids.Add(bag.CharacterId);
        foreach (var bag in batch.Bags)
            ids.Add(bag.CharacterId);
        return ids.ToList();
    }

    void Loop()
    {
        try
        {
            while (_queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (_queue.Reader.TryRead(out var work))
                {
                    Interlocked.Decrement(ref _queued);
                    Interlocked.Increment(ref _inFlight);
                    if (_queued == 0)
                        Volatile.Write(ref _oldestEnqueueTimestamp, 0);

                    bool ok = Apply(work);
                    if (!ok)
                        Interlocked.Increment(ref _commitFailures);
                    _completions.Enqueue(new EconomyPersistCompletion(work.BatchId, ok));
                    Interlocked.Decrement(ref _inFlight);
                }
            }
        }
        catch (ChannelClosedException)
        {
        }
    }

    void DrainWorkerOnCallerThread()
    {
        // Stop runs on the shutdown thread, never inside a step body.
        while (_queue.Reader.TryRead(out var work))
        {
            // The worker loop counts its reads down; this drain has to as well, or PendingCount stays above zero
            // forever after a stop and a shutdown reconcile reads a quiescent economy as in flight.
            Interlocked.Decrement(ref _queued);
            bool ok = Apply(work);
            if (!ok)
                Interlocked.Increment(ref _commitFailures);
            _completions.Enqueue(new EconomyPersistCompletion(work.BatchId, ok));
        }
    }

    bool Apply(EconomyPersistWork work)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            LastWriterThreadId = Environment.CurrentManagedThreadId;
            if (_store == null)
                return true;
            _journal.CommitWith(entries => _store.CommitEconomy(entries, work.Batch.Bags));
            FineageTelemetry.EconomyPersistBatches.Add(1, new KeyValuePair<string, object?>("result", "written"));
            FineageTelemetry.PersistWriteMilliseconds.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("kind", "economy"));
            return true;
        }
        catch (Exception ex)
        {
            _warn(
                $"economy persist failed batch={work.BatchId} bags={work.Batch.Bags.Count}: {ex.GetType().Name}: {ex.Message}");
            FineageTelemetry.EconomyPersistFailures.Add(1, new KeyValuePair<string, object?>("reason", ex.GetType().Name));
            FineageTelemetry.LedgerFlushFailures.Add(1, new KeyValuePair<string, object?>("pending", 0));
            return false;
        }
    }

    sealed class EconomyPersistWork
    {
        public ulong BatchId { get; init; }
        public required EconomyPersistBatch Batch { get; init; }
        public long EnqueuedAt { get; init; }
    }

    readonly record struct EconomyPersistCompletion(ulong BatchId, bool Success);

    sealed class PendingBatch(
        EconomyPersistBatch batch,
        int? discardWithoutPublish,
        List<ZoneCommandIngress.Applied>? heldReplies,
        List<int> characterIds)
    {
        public EconomyPersistBatch Batch { get; } = batch;
        public int? DiscardWithoutPublish { get; } = discardWithoutPublish;
        public List<ZoneCommandIngress.Applied>? HeldReplies { get; } = heldReplies;
        public List<int> CharacterIds { get; } = characterIds;

        /// <summary>Completed by <see cref="Finish"/> after the batch is published or restored.</summary>
        public TaskCompletionSource<bool> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>Dirty economy containers stolen from <see cref="ZoneWorld"/> for one durable commit.</summary>
internal sealed class EconomyPersistBatch
{
    public List<(int ObjectId, ZoneSession Session, PlayerInventory Inventory)> Inventories { get; } = [];
    public List<(int ObjectId, ZoneEntity Player)> Warehouses { get; } = [];
    public List<(int ObjectId, ZoneEntity Player)> Freight { get; } = [];
    public List<EconomyBagWrite> OfflineBags { get; } = [];
    public List<EconomyBagWrite> Bags { get; set; } = [];
}