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
/// same kind replaces an older one that has not started writing (coalesce). In-flight writes are not replaced.</item>
/// <item>A store <c>false</c> is a refusal: logged once, not retried, not reported as success.</item>
/// <item>A thrown write is retried up to <see cref="MaxAttempts"/> (delays 0 / 20 / 80 / 200 ms). Each attempt
/// logs the exception. If a newer same-kind snapshot arrived, the failed one is coalesced and the newer one is
/// written instead. After the last attempt the snapshot is dropped and logged; nothing is substituted.</item>
/// <item>Currency, items, warehouse, freight and the economy ledger do not use this worker.</item>
/// </list>
/// </remarks>
sealed class CharacterPersistWorker
{
    public const int MaxAttempts = 4;
    internal const string CharacterDomain = "character";
    internal const string PetDomain = "pet";
    static readonly int[] RetryDelayMs = [0, 20, 80, 200];

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
                return _pendingCount;
        }
    }

    public int LastWriterThreadId { get; private set; }

    public void Start()
    {
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

    /// <summary>Writes every queued snapshot, then stops the thread. Leftovers after <paramref name="budget"/> are logged.</summary>
    public void Stop(TimeSpan budget)
    {
        Drain(budget);
        lock (_gate)
        {
            _running = 0;
            Monitor.PulseAll(_gate);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
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

    /// <summary>How many of this owner's snapshots have finished (written, refused, or dropped).</summary>
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

                work = TakeOldestLocked();
                if (work == null)
                {
                    if (_running == 0)
                        break;
                    Monitor.Wait(_gate, 50);
                    continue;
                }

                _inFlight++;
                _inFlightWork = work;
            }

            try
            {
                Write(work);
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight--;
                    if (work != null)
                        NoteEpochLocked(work);
                    _inFlightWork = null;
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }

    PersistWork? TakeOldestLocked()
    {
        (string Domain, int OwnerId) bestKey = default;
        PersistWork? best = null;
        foreach (var (key, queue) in _pending)
        {
            if (queue.Count == 0)
                continue;
            var head = queue[0];
            if (best == null || head.Version < best.Version)
            {
                best = head;
                bestKey = key;
            }
        }

        if (best == null)
            return null;
        var queueOf = _pending[bestKey];
        queueOf.RemoveAt(0);
        _pendingCount--;
        if (queueOf.Count == 0)
            _pending.Remove(bestKey);
        return best;
    }

    void Write(PersistWork work)
    {
        if (_store == null)
            return;
        Exception? last = null;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                if (HasNewerSameKind(work))
                {
                    _warn(
                        $"persist snapshot coalesced after failure domain={work.Domain} owner={work.OwnerId} kind={work.Kind} account={work.Account} version={work.Version}: {last!.GetType().Name}: {last.Message}");
                    FineageTelemetry.PersistSnapshots.Add(1, Result("coalesced"));
                    return;
                }

                Thread.Sleep(RetryDelayMs[attempt - 1]);
            }

            try
            {
                long started = Stopwatch.GetTimestamp();
                LastWriterThreadId = Environment.CurrentManagedThreadId;
                bool accepted = work.Apply(_store);
                FineageTelemetry.StageMilliseconds.Record(
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    new KeyValuePair<string, object?>("stage", "persist.snapshot"));
                if (!accepted)
                {
                    _warn(
                        $"persist snapshot refused domain={work.Domain} owner={work.OwnerId} kind={work.Kind} account={work.Account} version={work.Version}: store returned false");
                    FineageTelemetry.PersistSnapshots.Add(1, Result("refused"));
                    return;
                }

                FineageTelemetry.PersistSnapshots.Add(1, Result("written"));
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                _warn(
                    $"persist snapshot failed domain={work.Domain} owner={work.OwnerId} kind={work.Kind} account={work.Account} version={work.Version} attempt={attempt}/{MaxAttempts}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _error(
            $"persist snapshot dropped domain={work.Domain} owner={work.OwnerId} kind={work.Kind} account={work.Account} version={work.Version} after {MaxAttempts} attempts: {last!.GetType().Name}: {last.Message}");
        FineageTelemetry.PersistSnapshots.Add(1, Result("failed"));
    }

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

    void LogLeftoversLocked(string reason)
    {
        foreach (var (key, queue) in _pending)
        {
            foreach (var work in queue)
                _error(
                    $"{reason} domain={key.Domain} owner={key.OwnerId} kind={work.Kind} account={work.Account} version={work.Version}");
        }

        if (_inFlightWork != null)
            _error(
                $"{reason} in-flight domain={_inFlightWork.Domain} owner={_inFlightWork.OwnerId} kind={_inFlightWork.Kind} account={_inFlightWork.Account} version={_inFlightWork.Version}");
    }

    static KeyValuePair<string, object?> Result(string result) => new("result", result);

    sealed class PersistWork(string domain, int ownerId, string kind, string account, Func<IAccountStore, bool> apply)
    {
        public string Domain { get; } = domain;
        public int OwnerId { get; } = ownerId;
        public string Kind { get; } = kind;
        public string Account { get; } = account;
        public long Version { get; set; }
        public Func<IAccountStore, bool> Apply { get; } = apply;
    }
}
