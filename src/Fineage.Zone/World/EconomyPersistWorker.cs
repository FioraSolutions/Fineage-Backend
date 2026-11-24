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

    public bool HasAwaitingHeldReplies => !_pending.IsEmpty;

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
        Action<EconomyPersistBatch> restore)
    {
        ArgumentNullException.ThrowIfNull(batch);

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

        if (!_queue.Writer.TryWrite(work))
        {
            _reservations.Release(charIds);
            restore(batch);
            return EconomyFlushResult.QueueFull;
        }

        Interlocked.Increment(ref _queued);
        if (_oldestEnqueueTimestamp == 0)
            Volatile.Write(ref _oldestEnqueueTimestamp, work.EnqueuedAt);

        _pending[batchId] = new PendingBatch(batch, discardWithoutPublish, heldReplies?.ToList(), charIds);
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

    /// <summary>Logout / shutdown: wait for batches on a non-simulation caller.</summary>
    public bool WaitForAllBatches(TimeSpan budget, ZoneWorld world)
    {
        EconomyPersistThreading.AssertNoBlockingWaitOnSimulationThread(world.InSimulationStep, "WaitForAllBatches");
        long deadline = Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency);
        while (PendingCount > 0 || !_pending.IsEmpty)
        {
            DrainCompletions(world);
            if (Stopwatch.GetTimestamp() >= deadline)
                return false;
            Thread.Sleep(5);
        }

        return true;
    }

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
            if (pending.HeldReplies is { Count: > 0 })
            {
                var uncommitted = UncommittedOf(pending.HeldReplies);
                world.CommandIngress.FailUncommitted(uncommitted, EconomyCommitFailed);
            }
        }
    }

    static HashSet<int> UncommittedOf(List<ZoneCommandIngress.Applied> held)
    {
        var ids = new HashSet<int>();
        foreach (var applied in held)
            ids.Add(applied.Submission.Session.ObjectId);
        return ids;
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
        foreach (var bag in batch.OfflineFreight)
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
            bool ok = Apply(work);
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
            FineageTelemetry.StageMilliseconds.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("stage", "persist.economy"));
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
    }
}

/// <summary>Dirty economy containers stolen from <see cref="ZoneWorld"/> for one durable commit.</summary>
internal sealed class EconomyPersistBatch
{
    public List<(int ObjectId, ZoneSession Session, PlayerInventory Inventory)> Inventories { get; } = [];
    public List<(int ObjectId, ZoneEntity Player)> Warehouses { get; } = [];
    public List<(int ObjectId, ZoneEntity Player)> Freight { get; } = [];
    public List<EconomyBagWrite> OfflineFreight { get; } = [];
    public List<EconomyBagWrite> Bags { get; set; } = [];
}