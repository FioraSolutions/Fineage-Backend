using System.Diagnostics;
using System.Threading.Channels;
using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Background writer for durable economy commits (ledger journal + bag projections). The simulation thread steals
/// dirty state under the economy lock and enqueues an immutable batch; this worker is the only thread that runs
/// <see cref="ILedgerJournal.CommitWith"/> with <see cref="IAccountStore.CommitEconomy"/> for those batches.
/// </summary>
/// <remarks>
/// Character snapshots use <see cref="CharacterPersistWorker"/> on a separate thread with the same durability policy.
/// There is a single persistence path for economy rows: this worker, not <c>Task.Run</c> on the pool.
/// </remarks>
sealed class EconomyPersistWorker : IDisposable
{
    public const int DefaultQueueCapacity = 64;
    public const int DefaultWaitMs = 30_000;

    public static bool AsyncEnabled =>
        !string.Equals(Environment.GetEnvironmentVariable("FINEAGE_ECONOMY_ASYNC_PERSIST"), "0", StringComparison.Ordinal);

    readonly IAccountStore? _store;
    readonly ILedgerJournal _journal;
    readonly Channel<EconomyPersistWork> _queue;
    readonly Action<string> _warn;
    readonly Action<string> _error;
    readonly object _drainGate = new();
    Thread? _thread;
    int _running;
    int _inFlight;
    int _queued;
    long _oldestEnqueueTimestamp;
    long _nextBatchId;

    public EconomyPersistWorker(IAccountStore? store, ILedgerJournal journal, int capacity = DefaultQueueCapacity)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _store = store;
        _journal = journal;
        _warn = ZoneLog.Warn;
        _error = ZoneLog.Error;
        _queue = Channel.CreateBounded<EconomyPersistWork>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public int PendingCount
    {
        get
        {
            lock (_drainGate)
                return _queued + _inFlight;
        }
    }

    public double OldestAgeMilliseconds
    {
        get
        {
            lock (_drainGate)
            {
                if (PendingCount == 0)
                    return 0;
                long oldest = _oldestEnqueueTimestamp;
                return oldest == 0 ? 0 : Stopwatch.GetElapsedTime(oldest).TotalMilliseconds;
            }
        }
    }

    public int LastWriterThreadId { get; private set; }

    public void Start()
    {
        FineageTelemetry.EconomyPersistPendingCount = () => PendingCount;
        FineageTelemetry.EconomyPersistOldestAgeMs = () => OldestAgeMilliseconds;
        if (_store == null || !AsyncEnabled)
            return;
        lock (_drainGate)
        {
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
    }

    public void Stop()
    {
        FlushSync();
        lock (_drainGate)
        {
            _running = 0;
            _queue.Writer.TryComplete();
        }

        _thread?.Join(TimeSpan.FromSeconds(5));
        _thread = null;
    }

    /// <summary>Steals no state; drains every queued commit on the caller (logout / shutdown).</summary>
    public void FlushSync()
    {
        if (_store == null)
            return;
        while (TryTakeHead(out var work))
        {
            bool ok = Apply(work);
            work.Completion.TrySetResult(ok);
        }
    }

    /// <summary>
    /// Enqueues one stolen batch and blocks the caller until the worker finishes or <paramref name="wait"/> expires.
    /// SQL runs on the worker thread; the caller must not hold the economy lock while waiting.
    /// </summary>
    public bool CommitAndWait(EconomyPersistBatch batch, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_store == null)
            return true;
        if (!AsyncEnabled || Volatile.Read(ref _running) == 0)
            return ApplyDirect(batch);

        var work = new EconomyPersistWork
        {
            BatchId = (ulong)Interlocked.Increment(ref _nextBatchId),
            Batch = batch,
            EnqueuedAt = Stopwatch.GetTimestamp(),
        };

        lock (_drainGate)
        {
            _queued++;
            if (_oldestEnqueueTimestamp == 0)
                _oldestEnqueueTimestamp = work.EnqueuedAt;
        }

        try
        {
            _queue.Writer.WriteAsync(work).AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException)
        {
            lock (_drainGate)
            {
                _queued = Math.Max(0, _queued - 1);
                NoteQueueDrainedLocked();
            }

            return ApplyDirect(batch);
        }

        if (!work.Completion.Task.Wait(wait))
        {
            _error($"economy persist wait timed out batch={work.BatchId} bags={batch.Bags.Count}");
            FineageTelemetry.EconomyPersistFailures.Add(1, new KeyValuePair<string, object?>("reason", "wait_timeout"));
            return false;
        }

        return work.Completion.Task.Result;
    }

    bool ApplyDirect(EconomyPersistBatch batch)
    {
        var work = new EconomyPersistWork { Batch = batch, BatchId = 0, EnqueuedAt = Stopwatch.GetTimestamp() };
        return Apply(work);
    }

    void Loop()
    {
        try
        {
            while (_queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (_queue.Reader.TryRead(out var work))
                {
                    lock (_drainGate)
                    {
                        _queued = Math.Max(0, _queued - 1);
                        _inFlight++;
                        if (_queued == 0)
                            NoteQueueDrainedLocked();
                    }

                    bool ok = false;
                    try
                    {
                        ok = Apply(work);
                    }
                    finally
                    {
                        lock (_drainGate)
                            _inFlight--;
                        work.Completion.TrySetResult(ok);
                    }
                }
            }
        }
        catch (ChannelClosedException)
        {
        }
    }

    bool TryTakeHead(out EconomyPersistWork work) => _queue.Reader.TryRead(out work!);

    bool Apply(EconomyPersistWork work)
    {
        if (_store == null)
            return true;
        long started = Stopwatch.GetTimestamp();
        try
        {
            LastWriterThreadId = Environment.CurrentManagedThreadId;
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
            return false;
        }
    }

    void NoteQueueDrainedLocked()
    {
        if (_queued == 0 && _inFlight == 0)
            _oldestEnqueueTimestamp = 0;
    }

    public void Dispose() => Stop();

    sealed class EconomyPersistWork
    {
        public ulong BatchId { get; init; }
        public required EconomyPersistBatch Batch { get; init; }
        public long EnqueuedAt { get; init; }
        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
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