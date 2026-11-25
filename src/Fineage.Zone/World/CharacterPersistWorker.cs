using System.Diagnostics;
using Fineage.Accounts;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Background writer for non-economic character snapshots. The simulation thread enqueues an immutable
/// closure; this thread is the only one that calls <see cref="IAccountStore"/> for those snapshots.
/// </summary>
/// <remarks>
/// Policy, applied uniformly (not per call site):
/// <list type="bullet">
/// <item>One queue per (domain, owner id). Different kinds stay in enqueue order. A newer snapshot of the
/// same kind replaces an older one that has not started writing (coalesce). In-flight writes are not replaced.
/// Memory stays bounded: at most one pending snapshot per owner and kind, plus the one write in flight.</item>
/// <item>A failed write (thrown or <c>false</c>) stays pending. The latest snapshot of that kind is retried with
/// capped exponential backoff. Nothing is dropped. Every failure is logged with the owner id, kind, attempt and
/// exception.</item>
/// <item>Logout and shutdown steal the pending snapshots and write them on the caller, skipping the backoff.
/// A failure there is logged with the id and the snapshot stays pending while the process is still up.</item>
/// <item>Currency, items, warehouse, freight and the economy ledger do not use this worker.</item>
/// </list>
/// </remarks>
sealed class CharacterPersistWorker
{
    public const int RetryBaseMs = 20;
    public const int RetryCapMs = 5_000;
    internal const string CharacterDomain = "character";
    internal const string PetDomain = "pet";

    readonly object _gate = new();
    readonly Dictionary<(string Domain, int OwnerId), List<PersistWork>> _pending = new();
    readonly Dictionary<(string Domain, int OwnerId), long> _ownerEpoch = new();
    readonly Dictionary<string, long> _domainEpoch = new();
    readonly IAccountStore? _store;
    readonly Action<string> _warn;
    readonly Action<string> _error;
    int _running;
    int _paused;
    int _inFlight;
    int _pendingCount;
    long _nextVersion = 1;
    Thread? _thread;
    PersistWork? _inFlightWork;

    public CharacterPersistWorker(IAccountStore? store, Action<string>? warn = null, Action<string>? error = null)
    {
        _store = store;
        _warn = warn ?? ZoneLog.Warn;
        _error = error ?? ZoneLog.Error;
    }

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public int PendingCount
    {
        get
        {
            lock (_gate)
                return _pendingCount + _inFlight;
        }
    }

    /// <summary>Age of the oldest snapshot that has not landed, or 0 when nothing is pending.</summary>
    public double OldestAgeMilliseconds
    {
        get
        {
            lock (_gate)
            {
                long oldest = long.MaxValue;
                if (_inFlightWork != null)
                    oldest = _inFlightWork.EnqueuedAt;
                foreach (var queue in _pending.Values)
                {
                    foreach (var work in queue)
                    {
                        if (work.EnqueuedAt < oldest)
                            oldest = work.EnqueuedAt;
                    }
                }

                return oldest == long.MaxValue ? 0 : Stopwatch.GetElapsedTime(oldest).TotalMilliseconds;
            }
        }
    }

    public int LastWriterThreadId { get; private set; }

    public void Start()
    {
        FineageTelemetry.PersistPendingCount = () => PendingCount;
        FineageTelemetry.PersistOldestAgeMs = () => OldestAgeMilliseconds;
        if (_store == null)
            return;
        lock (_gate)
        {
            if (_running == 1)
                return;
            _running = 1;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "fineage-character-persist"
            };
            _thread.Start();
        }
    }

    /// <summary>
    /// Writes every pending snapshot on this thread, skipping backoff, then stops the worker.
    /// Anything still pending failed that write and is logged with its id.
    /// </summary>
    public void Stop()
    {
        FlushSync(domain: null, ownerId: null);
        lock (_gate)
        {
            _running = 0;
            Monitor.PulseAll(_gate);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        lock (_gate)
            LogLeftoversLocked("persist shutdown left snapshot");
    }

    /// <summary>
    /// Writes the matching snapshots on the caller, including ones still inside their backoff. A failure is logged
    /// and the latest snapshot stays pending. <paramref name="domain"/> null matches every domain;
    /// <paramref name="ownerId"/> null matches every owner in that domain.
    /// </summary>
    public bool FlushSync(string? domain, int? ownerId)
    {
        List<PersistWork> batch;
        lock (_gate)
        {
            while (InFlightMatches(domain, ownerId))
                Monitor.Wait(_gate, 50);
            batch = StealMatchingLocked(domain, ownerId);
        }

        bool ok = true;
        foreach (var work in batch)
        {
            if (ApplyOnce(work))
            {
                lock (_gate)
                    NoteEpochLocked(work);
                continue;
            }

            ok = false;
            if (HasNewerSameKind(work))
            {
                _warn(
                    $"persist snapshot coalesced after failure domain={work.Domain} character={work.OwnerId} kind={work.Kind} attempt={work.Attempts}");
                FineageTelemetry.PersistSnapshots.Add(1, Result("coalesced"));
                continue;
            }

            RequeueFront(work);
        }

        return ok;
    }

    public void Enqueue(string domain, int ownerId, string kind, string account, Func<IAccountStore, bool> apply)
    {
        ArgumentException.ThrowIfNullOrEmpty(domain);
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(apply);
        var work = new PersistWork(domain, ownerId, kind, account ?? "", apply);
        lock (_gate)
        {
            work.Version = _nextVersion++;
            work.EnqueuedAt = Stopwatch.GetTimestamp();
            var key = (domain, ownerId);
            if (!_pending.TryGetValue(key, out var queue))
            {
                queue = [];
                _pending.Add(key, queue);
            }

            int removed = 0;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i].Kind == kind)
                {
                    queue.RemoveAt(i);
                    removed++;
                    _pendingCount--;
                }
            }

            queue.Add(work);
            _pendingCount++;
            if (removed > 0)
                FineageTelemetry.PersistSnapshots.Add(removed, Result("coalesced"));
            Monitor.PulseAll(_gate);
        }
    }

    public void PauseForTests() => Interlocked.Exchange(ref _paused, 1);

    public void ResumeForTests()
    {
        Interlocked.Exchange(ref _paused, 0);
        lock (_gate)
            Monitor.PulseAll(_gate);
    }

    public bool Drain(TimeSpan budget)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency);
        lock (_gate)
        {
            while (_pendingCount > 0 || _inFlight > 0)
            {
                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    LogLeftoversLocked("persist drain timed out");
                    return false;
                }

                Monitor.Wait(_gate, 50);
            }
        }

        return true;
    }

    /// <summary>Blocks until every queued snapshot in <paramref name="domain"/> has been written.</summary>
    public bool DrainDomain(string domain, TimeSpan budget)
    {
        ArgumentException.ThrowIfNullOrEmpty(domain);
        long deadline = Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency);
        lock (_gate)
        {
            while (DomainBusyLocked(domain))
            {
                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    _warn($"persist drain timed out domain={domain} pending={_pendingCount} inflight={_inFlight}");
                    return false;
                }

                Monitor.Wait(_gate, 50);
            }
        }

        return true;
    }

    /// <summary>How many of this owner's snapshots have been written.</summary>
    public long OwnerEpoch(string domain, int ownerId)
    {
        lock (_gate)
            return _ownerEpoch.GetValueOrDefault((domain, ownerId));
    }

    /// <summary>How many snapshots in <paramref name="domain"/> have finished.</summary>
    public long DomainEpoch(string domain)
    {
        lock (_gate)
            return _domainEpoch.GetValueOrDefault(domain);
    }

    public bool HasPending(string domain, int ownerId)
    {
        lock (_gate)
            return OwnerPendingLocked((domain, ownerId)) || InFlightIs((domain, ownerId));
    }

    public bool HasPendingDomain(string domain)
    {
        lock (_gate)
            return DomainBusyLocked(domain);
    }

    /// <summary>Blocks until this owner's queued snapshots have been written. Used before a reload of that character.</summary>
    public bool DrainOwner(string domain, int ownerId, TimeSpan budget)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency);
        var key = (domain, ownerId);
        lock (_gate)
        {
            while (OwnerPendingLocked(key) || InFlightIs(key))
            {
                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    _warn(
                        $"persist drain timed out domain={domain} owner={ownerId} pending={_pendingCount} inflight={_inFlight}");
                    return false;
                }

                Monitor.Wait(_gate, 50);
            }
        }

        return true;
    }

    void Loop()
    {
        while (true)
        {
            PersistWork? work = null;
            lock (_gate)
            {
                while (Volatile.Read(ref _paused) == 1 && _running == 1)
                    Monitor.Wait(_gate, 50);
                if (_running == 0)
                    break;

                work = TakeReadyLocked(out int waitMs);
                if (work == null)
                {
                    Monitor.Wait(_gate, waitMs);
                    continue;
                }

                _inFlight++;
                _inFlightWork = work;
            }

            bool landed = false;
            try
            {
                landed = ApplyOnce(work);
                if (landed)
                {
                    lock (_gate)
                        NoteEpochLocked(work);
                }
                else
                    RequeueFront(work);
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight--;
                    _inFlightWork = null;
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }

    PersistWork? TakeReadyLocked(out int waitMs)
    {
        long now = Stopwatch.GetTimestamp();
        (string Domain, int OwnerId) bestKey = default;
        PersistWork? best = null;
        long soonest = long.MaxValue;
        foreach (var (key, queue) in _pending)
        {
            if (queue.Count == 0)
                continue;
            var head = queue[0];
            if (head.NotBefore > now)
            {
                if (head.NotBefore < soonest)
                    soonest = head.NotBefore;
                continue;
            }

            if (best == null || head.Version < best.Version)
            {
                best = head;
                bestKey = key;
            }
        }

        if (best == null)
        {
            waitMs = soonest == long.MaxValue ? 250 : WaitMilliseconds(soonest);
            return null;
        }

        waitMs = 0;
        var queueOf = _pending[bestKey];
        queueOf.RemoveAt(0);
        _pendingCount--;
        if (queueOf.Count == 0)
            _pending.Remove(bestKey);
        return best;
    }

    bool ApplyOnce(PersistWork work)
    {
        if (_store == null)
            return false;
        work.Attempts++;
        try
        {
            long started = Stopwatch.GetTimestamp();
            LastWriterThreadId = Environment.CurrentManagedThreadId;
            bool accepted = work.Apply(_store);
            FineageTelemetry.PersistWriteMilliseconds.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("kind", "snapshot"));
            if (!accepted)
            {
                LogFailure(work, "store returned false");
                return false;
            }

            FineageTelemetry.PersistSnapshots.Add(1, Result("written"));
            return true;
        }
        catch (Exception ex)
        {
            LogFailure(work, $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    void RequeueFront(PersistWork work)
    {
        int delay = RetryDelayMs(work.Attempts);
        lock (_gate)
        {
            if (QueueHasNewerLocked(work))
            {
                _warn(
                    $"persist snapshot coalesced after failure domain={work.Domain} character={work.OwnerId} kind={work.Kind} attempt={work.Attempts}");
                FineageTelemetry.PersistSnapshots.Add(1, Result("coalesced"));
                return;
            }

            work.NotBefore = Stopwatch.GetTimestamp() + (long)(delay / 1000d * Stopwatch.Frequency);
            var key = (work.Domain, work.OwnerId);
            if (!_pending.TryGetValue(key, out var queue))
            {
                queue = [];
                _pending.Add(key, queue);
            }

            queue.Insert(0, work);
            _pendingCount++;
            Monitor.PulseAll(_gate);
        }
    }

    bool QueueHasNewerLocked(PersistWork work)
    {
        if (!_pending.TryGetValue((work.Domain, work.OwnerId), out var queue))
            return false;
        foreach (var queued in queue)
        {
            if (queued.Kind == work.Kind && queued.Version > work.Version)
                return true;
        }

        return false;
    }

    static int RetryDelayMs(int attempts)
    {
        int shift = Math.Min(Math.Max(attempts - 1, 0), 16);
        long delay = (long)RetryBaseMs << shift;
        return delay > RetryCapMs ? RetryCapMs : (int)delay;
    }

    static int WaitMilliseconds(long notBefore)
    {
        double ms = -Stopwatch.GetElapsedTime(notBefore).TotalMilliseconds;
        if (ms < 1)
            return 1;
        if (ms > 250)
            return 250;
        return (int)ms;
    }

    void LogFailure(PersistWork work, string reason) =>
        _error(
            $"persist snapshot failed domain={work.Domain} character={work.OwnerId} kind={work.Kind} attempt={work.Attempts}: {reason}");

    bool HasNewerSameKind(PersistWork work)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue((work.Domain, work.OwnerId), out var queue))
                return false;
            foreach (var queued in queue)
            {
                if (queued.Kind == work.Kind && queued.Version > work.Version)
                    return true;
            }

            return false;
        }
    }

    bool OwnerPendingLocked((string Domain, int OwnerId) key) =>
        _pending.TryGetValue(key, out var queue) && queue.Count > 0;

    bool DomainBusyLocked(string domain)
    {
        if (_inFlightWork != null && _inFlightWork.Domain == domain)
            return true;
        foreach (var (key, queue) in _pending)
        {
            if (key.Domain == domain && queue.Count > 0)
                return true;
        }

        return false;
    }

    void NoteEpochLocked(PersistWork work)
    {
        var key = (work.Domain, work.OwnerId);
        _ownerEpoch[key] = _ownerEpoch.GetValueOrDefault(key) + 1;
        _domainEpoch[work.Domain] = _domainEpoch.GetValueOrDefault(work.Domain) + 1;
    }

    bool InFlightIs((string Domain, int OwnerId) key) =>
        _inFlightWork != null && _inFlightWork.Domain == key.Domain && _inFlightWork.OwnerId == key.OwnerId;

    bool InFlightMatches(string? domain, int? ownerId)
    {
        var work = _inFlightWork;
        if (work == null)
            return false;
        if (domain != null && work.Domain != domain)
            return false;
        return ownerId == null || work.OwnerId == ownerId;
    }

    List<PersistWork> StealMatchingLocked(string? domain, int? ownerId)
    {
        var batch = new List<PersistWork>();
        var empty = new List<(string Domain, int OwnerId)>();
        foreach (var (key, queue) in _pending)
        {
            if (domain != null && key.Domain != domain)
                continue;
            if (ownerId != null && key.OwnerId != ownerId)
                continue;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                batch.Add(queue[i]);
                queue.RemoveAt(i);
                _pendingCount--;
            }

            if (queue.Count == 0)
                empty.Add(key);
        }

        foreach (var key in empty)
            _pending.Remove(key);
        batch.Sort(static (a, b) => a.Version.CompareTo(b.Version));
        return batch;
    }

    void LogLeftoversLocked(string reason)
    {
        foreach (var (key, queue) in _pending)
        {
            foreach (var work in queue)
                _error(
                    $"{reason} domain={key.Domain} character={key.OwnerId} kind={work.Kind} attempt={work.Attempts}");
        }

        if (_inFlightWork != null)
            _error(
                $"{reason} in-flight domain={_inFlightWork.Domain} character={_inFlightWork.OwnerId} kind={_inFlightWork.Kind} attempt={_inFlightWork.Attempts}");
    }

    static KeyValuePair<string, object?> Result(string result) => new("result", result);

    sealed class PersistWork(string domain, int ownerId, string kind, string account, Func<IAccountStore, bool> apply)
    {
        public string Domain { get; } = domain;
        public int OwnerId { get; } = ownerId;
        public string Kind { get; } = kind;
        public string Account { get; } = account;
        public long Version { get; set; }
        public int Attempts { get; set; }
        public long EnqueuedAt { get; set; }
        public long NotBefore { get; set; }
        public Func<IAccountStore, bool> Apply { get; } = apply;
    }
}
