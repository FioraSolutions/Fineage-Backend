using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Fineage.Protocol.Telemetry;

/// <summary>
/// Aggregates the Fineage meter for Prometheus scrapes.
/// <para>
/// Recording never takes a lock: each recording thread appends to its own single-producer log
/// (<see cref="ThreadBuffer"/>), and the aggregation below runs only on the drain thread or inside
/// <see cref="Snapshot"/>, both under <c>_gate</c>. Before this, every counter add and histogram record took one
/// process-wide lock, so the fan-out worker, the stripe workers and the socket pumps serialized on telemetry.
/// A recording thread that outruns the drain thread seals its full block onto a lock-free queue and starts a new
/// one, so the recording path never waits on <c>_gate</c> and no measurement is dropped.
/// </para>
/// </summary>
public sealed class MetricsCollector : IDisposable
{
    /// <summary>
    /// 512 slots of ~104 bytes keep each ring under the 85 KB large-object threshold. At 8192 every recording
    /// thread pinned an ~850 KB reference-holding array on the LOH; with ~100 threads that was tens of MB of
    /// card-marked old-generation memory every ephemeral GC had to scan. The shorter drain interval keeps the
    /// smaller ring from filling.
    /// </summary>
    const int BufferCapacity = 512;
    static readonly TimeSpan DrainInterval = TimeSpan.FromMilliseconds(10);

    readonly MeterListener _listener = new();
    readonly object _gate = new();
    readonly ThreadLocal<ThreadBuffer?> _local = new();
    readonly List<ThreadBuffer> _buffers = [];
    readonly KeyValuePair<string, object?>[] _tagScratch = new KeyValuePair<string, object?>[ThreadBuffer.InlineTags];
    readonly ManualResetEventSlim _stop = new(false);
    readonly Thread _drainer;
    readonly Dictionary<string, MutableEndpoint> _rows = new(StringComparer.Ordinal);
    readonly DurationHistogram _tick = new();
    readonly DurationHistogram _enterTry = new();
    readonly DurationHistogram _enterTryAoiBind = new();
    readonly DurationHistogram _enterPlan = new();
    readonly DurationHistogram _enterSelf = new();
    readonly DurationHistogram _enterKnownSnapshot = new();
    readonly DurationHistogram _enterKnownEncode = new();
    readonly DurationHistogram _enterPeerFanout = new();
    readonly DurationHistogram _enterKnownEntities = new();
    readonly DurationHistogram _enterKnownPlayers = new();
    readonly DurationHistogram _enterFrames = new();
    readonly DurationHistogram _enterBytes = new();
    readonly DurationHistogram _tickStepFanoutDrain = new();
    readonly DurationHistogram _commandToMailboxes = new();
    readonly DurationHistogram _fanoutBatchProcess = new();
    readonly DurationHistogram _fanoutQueueAge = new();
    readonly DurationHistogram _fanoutDispatchWait = new();
    readonly DurationHistogram _fanoutEnqueueToSocketWrite = new();
    readonly BoundedCountHistogram _fanoutQueueDepth = new(MetricsText.CountBounds);
    readonly BoundedCountHistogram _fanoutQueueWork = new(MetricsText.CountBounds);
    readonly BoundedCountHistogram _fanoutEventsPerBatch = new(MetricsText.CountBounds);
    readonly BoundedCountHistogram _fanoutRecipientsPerEvent = new(MetricsText.CountBounds);
    readonly BoundedCountHistogram _fanoutQueueBytes = new(MetricsText.SocketWriteByteBounds);
    readonly BoundedCountHistogram _outboundQueueFrames = new(MetricsText.SocketWriteFrameBounds);
    readonly BoundedCountHistogram _outboundQueueBytes = new(MetricsText.SocketWriteByteBounds);
    readonly DurationHistogram _outboundQueueAge = new();
    readonly BoundedCountHistogram _pathfindNodes = new(MetricsText.CountBounds);
    readonly BoundedCountHistogram _pathfindQueued = new(MetricsText.CountBounds);
    readonly Dictionary<string, MutableStage> _pathfindWait = new(StringComparer.Ordinal);
    readonly Dictionary<string, MutableStage> _pathfindOldestAge = new(StringComparer.Ordinal);
    readonly Dictionary<string, MutableStage> _pathfindUnservedWait = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _pathfindStarvedSteps = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _pathfindSearches = new(StringComparer.Ordinal);
    long _pathfindBudgetExhausted;
    long _pathfindNoRoute;
    long _pathfindNoRouteCached;
    long _pathfindEmptyPath;
    readonly Dictionary<string, long> _approachAttempts = new(StringComparer.Ordinal);
    long _approachClippedToOrigin;
    long _fanoutOverflow;
    long _fanoutCoalesced;
    long _fanoutShedVisual;
    long _fanoutOverflowShed;
    long _reliableOverflow;
    readonly Dictionary<string, MutableStage> _stages = new(StringComparer.Ordinal);
    readonly Dictionary<string, MutableStage> _commandTimelinePhases = new(StringComparer.Ordinal);
    readonly Dictionary<string, MutableStage> _commandApplyByName = new(StringComparer.Ordinal);
    readonly Dictionary<string, MutableStage> _commandQueueWaitByClass = new(StringComparer.Ordinal);
    readonly Dictionary<string, MutableStage> _persistWriteByKind = new(StringComparer.Ordinal);
    long _tickOverruns;
    long _fanoutBacklogSoftExceeded;
    long _fanoutBacklogEmergency;
    long _outboundSlowClientDisconnect;
    long _outboundShedBytes;
    long _ticksSkipped;
    long _regionHandoffs;
    readonly Dictionary<string, long> _ledgerAdjustments = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _persistSnapshots = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _fanoutDropped = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _laneBlocked = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _laneFull = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _laneCoalesced = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _laneEvicted = new(StringComparer.Ordinal);
    long _ledgerFlushFailures;
    long _ledgerInvariantViolations;
    int _persistPending;
    readonly Dictionary<string, int> _activationBacklog = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> _activationBacklogMax = new(StringComparer.Ordinal);
    double _persistOldestAgeMs;
    int _online;
    int _commandIngressQueued;
    readonly BoundedCountHistogram _commandPlayerQueueDepth = new(MetricsText.CountBounds);
    readonly DurationHistogram _commandQueueAge = new();
    long _commandCoalesced;
    readonly Dictionary<string, long> _commandRejected = new(StringComparer.Ordinal);
    double _tickMaxMs;
    double _commandQueueAgeMaxMs;
    double _watchdogStallMaxMs;
    long _watchdogStalls;

    /// <summary>
    /// #992: operational counters whose series are their tag values (economy persist, audit, reconcile, data
    /// fallbacks, audience shed). Before #992 these instruments were recorded but never reached /metrics.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> OperationalCounterTags = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["fineage.data.silent_fallback"] = ["kind"],
        ["fineage.fanout.reliable.audience_shed"] = [],
        ["fineage.state.audit_issues"] = ["code", "container", "severity"],
        ["fineage.economy.persist.batches"] = ["result"],
        ["fineage.economy.persist.failures"] = ["reason"],
        ["fineage.economy.reconcile.runs"] = ["phase", "result"],
        ["fineage.economy.reconcile.violations"] = ["phase", "code"],
    };

    readonly Dictionary<string, Dictionary<string, long>> _operationalCounters = new(StringComparer.Ordinal);
    int _economyPersistPending;
    double _economyPersistOldestAgeMs;
    readonly DurationHistogram _fanoutDispatcherEnqueueWait = new();
    readonly DurationHistogram _fanoutStripeEnqueueWait = new();

    public MetricsCollector()
        : this(onlyMeter: null)
    {
    }

    /// <summary>
    /// Test seam: when <paramref name="onlyMeter"/> is given, this collector listens to that meter instance only.
    /// Every collector otherwise subscribes by meter name, so two collectors in one process see each other's
    /// instruments — a test that asserts on its own backlog would count a parallel test class's recordings.
    /// </summary>
    internal MetricsCollector(Meter? onlyMeter)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            bool wanted = onlyMeter != null
                ? ReferenceEquals(instrument.Meter, onlyMeter)
                : instrument.Meter.Name == FineageTelemetry.MeterName;
            if (wanted)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(OnLong);
        _listener.SetMeasurementEventCallback<int>(OnInt);
        _listener.SetMeasurementEventCallback<double>(OnDouble);
        _listener.Start();
        _drainer = new Thread(DrainLoop) { IsBackground = true, Name = "metrics-drain" };
        _drainer.Start();
    }

    public MetricsSnapshot Snapshot()
    {
        _listener.RecordObservableInstruments();
        lock (_gate)
        {
            DrainAllLocked();
            var endpoints = new List<EndpointMetrics>(_rows.Count);
            foreach (KeyValuePair<string, MutableEndpoint> pair in _rows.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (pair.Key.Length == 0)
                    continue;
                endpoints.Add(pair.Value.Freeze(pair.Key));
            }

            return new MetricsSnapshot
            {
                PlayersOnline = _online,
                CommandIngressQueued = _commandIngressQueued,
                Tick = _tick.Freeze(),
                EnterTry = _enterTry.Freeze(),
                EnterTryAoiBind = _enterTryAoiBind.Freeze(),
                EnterPlan = _enterPlan.Freeze(),
                EnterSelf = _enterSelf.Freeze(),
                EnterKnownSnapshot = _enterKnownSnapshot.Freeze(),
                EnterKnownEncode = _enterKnownEncode.Freeze(),
                EnterPeerFanout = _enterPeerFanout.Freeze(),
                EnterKnownEntities = _enterKnownEntities.Freeze(),
                EnterKnownPlayers = _enterKnownPlayers.Freeze(),
                EnterFrames = _enterFrames.Freeze(),
                EnterBytes = _enterBytes.Freeze(),
                TickStepFanoutDrain = _tickStepFanoutDrain.Freeze(),
                CommandToMailboxes = _commandToMailboxes.Freeze(),
                FanoutBatchProcess = _fanoutBatchProcess.Freeze(),
                FanoutQueueOldestAge = _fanoutQueueAge.Freeze(),
                FanoutDispatchWait = _fanoutDispatchWait.Freeze(),
                FanoutEnqueueToSocketWrite = _fanoutEnqueueToSocketWrite.Freeze(),
                FanoutQueueDepth = _fanoutQueueDepth.Freeze(),
                FanoutQueueWork = _fanoutQueueWork.Freeze(),
                FanoutEventsPerBatch = _fanoutEventsPerBatch.Freeze(),
                FanoutRecipientsPerEvent = _fanoutRecipientsPerEvent.Freeze(),
                FanoutQueueBytes = _fanoutQueueBytes.Freeze(),
                OutboundQueueFrames = _outboundQueueFrames.Freeze(),
                OutboundQueueBytes = _outboundQueueBytes.Freeze(),
                OutboundQueueOldestAge = _outboundQueueAge.Freeze(),
                PathfindNodes = _pathfindNodes.Freeze(),
                PathfindQueued = _pathfindQueued.Freeze(),
                PathfindWait = FreezeLabeled(_pathfindWait),
                PathfindOldestAge = FreezeLabeled(_pathfindOldestAge),
                PathfindUnservedWait = FreezeLabeled(_pathfindUnservedWait),
                PathfindStarvedSteps = new Dictionary<string, long>(_pathfindStarvedSteps, StringComparer.Ordinal),
                PathfindSearches = new Dictionary<string, long>(_pathfindSearches, StringComparer.Ordinal),
                PathfindNoRoute = _pathfindNoRoute,
                PathfindNoRouteCached = _pathfindNoRouteCached,
                PathfindEmptyPath = _pathfindEmptyPath,
                ApproachAttempts = new Dictionary<string, long>(_approachAttempts, StringComparer.Ordinal),
                ApproachClippedToOrigin = _approachClippedToOrigin,
                FanoutOverflow = _fanoutOverflow,
                FanoutCoalesced = _fanoutCoalesced,
                FanoutShedVisual = _fanoutShedVisual,
                FanoutOverflowShed = _fanoutOverflowShed,
                ReliableOverflow = _reliableOverflow,
                TickOverruns = _tickOverruns,
                FanoutBacklogSoftExceeded = _fanoutBacklogSoftExceeded,
                FanoutBacklogEmergency = _fanoutBacklogEmergency,
                OutboundSlowClientDisconnect = _outboundSlowClientDisconnect,
                OutboundShedBytes = _outboundShedBytes,
                PathfindBudgetExhausted = _pathfindBudgetExhausted,
                TicksSkipped = _ticksSkipped,
                RegionHandoffs = _regionHandoffs,
                LedgerAdjustments = _ledgerAdjustments
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                LedgerFlushFailures = _ledgerFlushFailures,
                LedgerInvariantViolations = _ledgerInvariantViolations,
                PersistPending = _persistPending,
                ActivationBacklog = new Dictionary<string, int>(_activationBacklog, StringComparer.Ordinal),
                ActivationBacklogMax = new Dictionary<string, int>(_activationBacklogMax, StringComparer.Ordinal),
                PersistOldestAgeMs = _persistOldestAgeMs,
                PersistSnapshots = _persistSnapshots
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                FanoutDropped = _fanoutDropped
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                OutboundLaneBlocked = FreezeLane(_laneBlocked),
                OutboundLaneFull = FreezeLane(_laneFull),
                OutboundLaneCoalesced = FreezeLane(_laneCoalesced),
                OutboundLaneEvicted = FreezeLane(_laneEvicted),
                Stages = _stages
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Value.Freeze(pair.Key))
                    .ToList(),
                CommandTimelinePhases = FreezeLabeled(_commandTimelinePhases),
                CommandApplyByName = FreezeLabeled(_commandApplyByName),
                CommandQueueWaitByClass = FreezeLabeled(_commandQueueWaitByClass),
                PersistWriteByKind = FreezeLabeled(_persistWriteByKind),
                CommandPlayerQueueDepth = _commandPlayerQueueDepth.Freeze(),
                CommandQueueAge = _commandQueueAge.Freeze(),
                CommandCoalesced = _commandCoalesced,
                CommandRejected = _commandRejected
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                TickMaxMs = _tickMaxMs,
                CommandQueueAgeMaxMs = _commandQueueAgeMaxMs,
                WatchdogStallMaxMs = _watchdogStallMaxMs,
                WatchdogStalls = _watchdogStalls,
                OperationalCounters = _operationalCounters.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyDictionary<string, long>)new SortedDictionary<string, long>(pair.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal),
                EconomyPersistPending = _economyPersistPending,
                EconomyPersistOldestAgeMs = _economyPersistOldestAgeMs,
                FanoutDispatcherEnqueueWait = _fanoutDispatcherEnqueueWait.Freeze(),
                FanoutStripeEnqueueWait = _fanoutStripeEnqueueWait.Freeze(),
                Endpoints = endpoints
            };
        }
    }


    /// <remarks>
    /// <see cref="MeterListener.Dispose"/> does not wait for callbacks already running on other threads, so a producer
    /// can still be inside <see cref="Buffer"/> after it returns. The thread-local slot is therefore left to its
    /// finalizer instead of being disposed here (disposing it crashed the test host with ObjectDisposedException on a
    /// recording thread, #992); <see cref="_disposed"/> makes late callbacks drop their measurement.
    /// </remarks>
    public void Dispose()
    {
        _disposed = true;
        _listener.Dispose();
        _stop.Set();
        _drainer.Join();
        _stop.Dispose();
    }

    volatile bool _disposed;

    /// <summary>
    /// Tests: stops the drain thread so a producer can outrun it on purpose. <see cref="Snapshot"/> still drains under
    /// the gate, so the collector stays usable; this exists only to make the backlog path reachable on demand instead of
    /// by racing a 10 ms timer.
    /// </summary>
    internal void PauseDrainerForTest()
    {
        _stop.Set();
        _drainer.Join();
    }

    /// <summary>
    /// Tests and the soak harness: measurements the recording threads have published but no reader has applied yet. Zero
    /// after a drain means the collector is keeping up; a number that only grows under load is the evidence that the
    /// recorder, not the simulation, is the bottleneck.
    /// </summary>
    internal long TelemetryBacklogSlotsForTest()
    {
        lock (_gate)
        {
            long total = 0;
            foreach (ThreadBuffer buffer in _buffers)
                total += buffer.BacklogSlots;
            return total;
        }
    }

    void OnLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (!_disposed && IsOurs(instrument))
            Buffer().Append(instrument, ThreadBuffer.KindLong, value, 0, tags);
    }

    void OnInt(Instrument instrument, int value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (!_disposed && IsOurs(instrument))
            Buffer().Append(instrument, ThreadBuffer.KindInt, value, 0, tags);
    }

    void OnDouble(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (!_disposed && IsOurs(instrument))
            Buffer().Append(instrument, ThreadBuffer.KindDouble, 0, value, tags);
    }

    ThreadBuffer Buffer()
    {
        ThreadBuffer? buffer = _local.Value;
        if (buffer != null)
            return buffer;

        buffer = new ThreadBuffer(this, Thread.CurrentThread);
        lock (_gate)
            _buffers.Add(buffer);
        _local.Value = buffer;
        return buffer;
    }

    void DrainLoop()
    {
        while (!_stop.Wait(DrainInterval))
        {
            lock (_gate)
                DrainAllLocked();
        }

        lock (_gate)
            DrainAllLocked();
    }

    /// <summary>Applies every published measurement; drops buffers whose thread has exited once they are empty.</summary>
    void DrainAllLocked()
    {
        for (int i = _buffers.Count - 1; i >= 0; i--)
        {
            ThreadBuffer buffer = _buffers[i];
            DrainLocked(buffer);
            if (!buffer.Owner.IsAlive && buffer.IsEmpty)
                _buffers.RemoveAt(i);
        }
    }

    void DrainLocked(ThreadBuffer buffer)
    {
        while (buffer.TryTake(out ThreadBuffer.Slot slot))
        {
            ReadOnlySpan<KeyValuePair<string, object?>> tags = slot.Extra ?? TagsOf(ref slot);
            switch (slot.Kind)
            {
                case ThreadBuffer.KindLong:
                    ApplyLong(slot.Instrument!, slot.Long, tags);
                    break;
                case ThreadBuffer.KindInt:
                    ApplyInt(slot.Instrument!, (int)slot.Long, tags);
                    break;
                case ThreadBuffer.KindDouble:
                    ApplyDouble(slot.Instrument!, slot.Double, tags);
                    break;
                default:
                    throw new InvalidOperationException($"metrics buffer slot has unknown kind {slot.Kind}");
            }
        }
    }

    ReadOnlySpan<KeyValuePair<string, object?>> TagsOf(ref ThreadBuffer.Slot slot)
    {
        if (slot.TagCount == 0)
            return ReadOnlySpan<KeyValuePair<string, object?>>.Empty;
        _tagScratch[0] = slot.Tag0;
        if (slot.TagCount > 1)
            _tagScratch[1] = slot.Tag1;
        if (slot.TagCount > 2)
            _tagScratch[2] = slot.Tag2;
        if (slot.TagCount > 3)
            _tagScratch[3] = slot.Tag3;
        return _tagScratch.AsSpan(0, slot.TagCount);
    }

    /// <summary>
    /// Single-producer log owned by one recording thread. The producer appends to the block it is filling and
    /// publishes that block's count; the consumer (always under the collector gate) reads the published slots.
    /// <para>
    /// The log is a chain of fixed blocks instead of one wrapping array, so <see cref="Append"/> never takes a lock
    /// and never loses a measurement. The old flat ring had to wrap, and wrapping meant overwriting slots the drain
    /// thread had not read yet, so a producer that caught up with the consumer drained its own ring under
    /// <c>_gate</c> — the same lock the drain thread and every snapshot hold. That is the coupling that put the
    /// fan-out worker and the socket pumps behind telemetry. A full block is now sealed onto a lock-free queue and
    /// a fresh one started, so the producer always has room.
    /// </para>
    /// </summary>
    sealed class ThreadBuffer
    {
        readonly MetricsCollector owner;

        public ThreadBuffer(MetricsCollector owner, Thread thread)
        {
            this.owner = owner;
            Owner = thread;
            _current = _first;
        }
        public const byte KindLong = 1;
        public const byte KindInt = 2;
        public const byte KindDouble = 3;
        public const int InlineTags = 4;

        public struct Slot
        {
            public Instrument? Instrument;
            public long Long;
            public double Double;
            public byte Kind;
            public byte TagCount;
            public KeyValuePair<string, object?> Tag0;
            public KeyValuePair<string, object?> Tag1;
            public KeyValuePair<string, object?> Tag2;
            public KeyValuePair<string, object?> Tag3;
            public KeyValuePair<string, object?>[]? Extra;
        }

        /// <summary>One fixed block. The producer seals it onto the queue once it is full.</summary>
        sealed class Block(long id)
        {
            public readonly Slot[] Slots = new Slot[BufferCapacity];
            long _count;

            public long Id { get; } = id;

            public long Count => Volatile.Read(ref _count);

            public void Publish(long count) => Volatile.Write(ref _count, count);
        }

        readonly ConcurrentQueue<Block> _sealed = new();
        readonly Block _first = new(0);
        Block _current;
        int _currentCount;
        long _nextId = 1;
        // Consumer side: the block it is reading, how far, and the highest block it fully consumed. The drain
        // thread and Snapshot are the only readers, both under the collector gate.
        Block? _readBlock;
        int _readAt;
        long _consumedThroughId = -1;

        public Thread Owner { get; }

        public bool IsEmpty
        {
            get
            {
                if (_readBlock != null && _readAt < _readBlock.Count)
                    return false;
                Block current = Volatile.Read(ref _current);
                return _sealed.IsEmpty && current.Count == (ReferenceEquals(_readBlock, current) ? _readAt : 0);
            }
        }

        /// <summary>
        /// Consumer side, under the collector gate: sealed blocks still waiting, plus what the current block holds unread.
        /// Zero means the drain has caught up. A soak reads this as evidence the collector is not accumulating backlog
        /// behind a busy producer — the alternative is only inferring it from a publish source that has no queue at all.
        /// </summary>
        public long BacklogSlots
        {
            get
            {
                long sealed_ = (long)_sealed.Count * BufferCapacity;
                Block current = Volatile.Read(ref _current);
                long unread = current.Count - (ReferenceEquals(_readBlock, current) ? _readAt : 0);
                return sealed_ + Math.Max(0, unread);
            }
        }

        /// <summary>Consumer side, under the collector gate: the next published slot, or false when caught up.</summary>
        public bool TryTake(out Slot slot)
        {
            while (true)
            {
                if (_readBlock != null)
                {
                    if (_readAt < _readBlock.Count)
                    {
                        slot = _readBlock.Slots[_readAt++];
                        return true;
                    }

                    if (!ReferenceEquals(_readBlock, Volatile.Read(ref _current)))
                    {
                        // Fully read and the producer already moved on; it sealed this block onto the queue.
                        _consumedThroughId = _readBlock.Id;
                        _readBlock = null;
                        continue;
                    }

                    // The block being filled has nothing new yet.
                    slot = default;
                    return false;
                }

                Block? next = null;
                while (_sealed.TryDequeue(out Block? candidate))
                {
                    if (candidate.Id > _consumedThroughId)
                    {
                        next = candidate;
                        break;
                    }
                }

                next ??= Volatile.Read(ref _current);
                if (next.Id <= _consumedThroughId)
                {
                    slot = default;
                    return false;
                }

                _readBlock = next;
                _readAt = 0;
            }
        }

        public void Append(Instrument instrument, byte kind, long longValue, double doubleValue,
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            if (_currentCount >= BufferCapacity)
            {
                // Seal the full block and start a new one. The producer never writes a sealed block again, so the
                // consumer can read it at its own pace and this path stays lock-free. Sealing is not lossy by contract:
                // a block waits on the lock-free queue until the drain thread reads it, however far behind it is.
                _current.Publish(BufferCapacity);
                _sealed.Enqueue(_current);
                _current = new Block(_nextId++);
                _currentCount = 0;
            }

            ref Slot slot = ref _current.Slots[_currentCount];
            slot.Instrument = instrument;
            slot.Kind = kind;
            slot.Long = longValue;
            slot.Double = doubleValue;
            if (tags.Length <= InlineTags)
            {
                slot.TagCount = (byte)tags.Length;
                slot.Extra = null;
                if (tags.Length > 0)
                    slot.Tag0 = tags[0];
                if (tags.Length > 1)
                    slot.Tag1 = tags[1];
                if (tags.Length > 2)
                    slot.Tag2 = tags[2];
                if (tags.Length > 3)
                    slot.Tag3 = tags[3];
            }
            else
            {
                slot.TagCount = 0;
                slot.Extra = tags.ToArray();
            }

            _currentCount++;
            _current.Publish(_currentCount);
        }
    }

    void ApplyLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (OperationalCounterTags.TryGetValue(instrument.Name, out string[]? tagNames))
        {
            if (!_operationalCounters.TryGetValue(instrument.Name, out var series))
                _operationalCounters[instrument.Name] = series = new Dictionary<string, long>(StringComparer.Ordinal);
            string key = SeriesKey(tags, tagNames);
            series[key] = series.GetValueOrDefault(key) + value;
            return;
        }

        string endpoint = EndpointOf(tags);
        switch (instrument.Name)
        {
            case "fineage.connections":
                Row(endpoint).Connections += value;
                break;
            case "fineage.connections.rejected":
                Row(endpoint).ConnectionsRejected += value;
                break;
            case "fineage.frames.in":
                Row(endpoint).FramesIn += value;
                break;
            case "fineage.frames.out":
                Row(endpoint).FramesOut += value;
                break;
            case "fineage.bytes.in":
                Row(endpoint).BytesIn += value;
                break;
            case "fineage.bytes.out":
                Row(endpoint).BytesOut += value;
                break;
            case "fineage.outbound.dropped":
                Row(endpoint).OutboundDropped += value;
                break;
            case "fineage.packets.rejected":
                Row(endpoint).PacketsRejected += value;
                break;
            case "fineage.packets.errored":
                Row(endpoint).PacketsErrored += value;
                break;
            case "fineage.tick.overruns":
                _tickOverruns += value;
                break;
            case "fineage.fanout.backlog.soft_exceeded":
                _fanoutBacklogSoftExceeded += value;
                break;
            case "fineage.fanout.backlog.emergency":
                _fanoutBacklogEmergency += value;
                break;
            case "fineage.outbound.slow_client_disconnect":
                _outboundSlowClientDisconnect += value;
                break;
            case "fineage.outbound.shed_bytes":
                _outboundShedBytes += value;
                break;
            case "fineage.tick.skipped":
                _ticksSkipped += value;
                break;
            case "fineage.region.handoffs":
                _regionHandoffs += value;
                break;
            case "fineage.tick.stage.failures":
                Stage(StageOf(tags)).Failures += value;
                break;
            case "fineage.ledger.adjustments":
                _ledgerAdjustments[MoveKindOf(tags)] = _ledgerAdjustments.GetValueOrDefault(MoveKindOf(tags)) + value;
                break;
            case "fineage.ledger.flush_failures":
                _ledgerFlushFailures += value;
                break;
            case "fineage.ledger.invariant_violations":
                _ledgerInvariantViolations += value;
                break;
            case "fineage.persist.snapshots":
                string result = Tag(tags, "result");
                _persistSnapshots[result] = _persistSnapshots.GetValueOrDefault(result) + value;
                break;
            case "fineage.fanout.dropped":
                string group = Tag(tags, "opcode_group");
                if (group.Length == 0)
                    group = "other";
                _fanoutDropped[group] = _fanoutDropped.GetValueOrDefault(group) + value;
                break;
            case "fineage.fanout.queue.overflow":
                _fanoutOverflow += value;
                break;
            case "fineage.npc.pathfind.budget_exhausted":
                _pathfindBudgetExhausted += value;
                break;
            case "fineage.npc.pathfind.starved_steps":
                _pathfindStarvedSteps[PriorityOf(tags)] = _pathfindStarvedSteps.GetValueOrDefault(PriorityOf(tags)) + value;
                break;
            case "fineage.npc.pathfind.no_route":
                _pathfindNoRoute += (long)value;
                break;
            case "fineage.npc.pathfind.no_route_cached":
                _pathfindNoRouteCached += (long)value;
                break;
            case "fineage.npc.pathfind.searches":
                _pathfindSearches[PriorityOf(tags)] = _pathfindSearches.GetValueOrDefault(PriorityOf(tags)) + value;
                break;
            case "fineage.npc.pathfind.empty_path":
                _pathfindEmptyPath += (long)value;
                break;
            case "fineage.approach.attempts":
                string outcome = Tag(tags, "outcome");
                if (outcome.Length == 0)
                    outcome = "other";
                _approachAttempts[outcome] = _approachAttempts.GetValueOrDefault(outcome) + value;
                break;
            case "fineage.approach.clipped_to_origin":
                _approachClippedToOrigin += (long)value;
                break;
            case "fineage.fanout.coalesced":
                _fanoutCoalesced += value;
                break;
            case "fineage.fanout.shed_visual":
                _fanoutShedVisual += value;
                break;
            case "fineage.fanout.overflow.shed":
                _fanoutOverflowShed += value;
                break;
            case "fineage.outbound.reliable_overflow":
                _reliableOverflow += value;
                break;
            case "fineage.command.coalesced":
                _commandCoalesced += value;
                break;
            case "fineage.command.rejected":
                string reason = Tag(tags, "reason");
                if (reason.Length == 0)
                    reason = "unspecified";
                _commandRejected[reason] = _commandRejected.GetValueOrDefault(reason) + value;
                break;
            case "fineage.step.watchdog.stalls":
                _watchdogStalls = value;
                break;
            case "fineage.outbound.lane.blocked":
                AddLane(_laneBlocked, Tag(tags, "lane"), value);
                break;
            case "fineage.outbound.lane.full":
                AddLane(_laneFull, Tag(tags, "lane"), value);
                break;
            case "fineage.outbound.lane.coalesced":
                AddLane(_laneCoalesced, Tag(tags, "lane"), value);
                break;
            case "fineage.outbound.lane.evicted":
                AddLane(_laneEvicted, Tag(tags, "lane"), value);
                break;
        }
    }

    void ApplyInt(Instrument instrument, int value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        switch (instrument.Name)
        {
            case "fineage.zone.online":
                _online = value;
                break;
            case "fineage.command.ingress.queued":
                _commandIngressQueued = value;
                break;
            case "fineage.command.queue.depth":
                _commandPlayerQueueDepth.Record(value);
                break;
            case "fineage.npc.activation.backlog":
                _activationBacklog[Tag(tags, "kind")] = value;
                break;
            case "fineage.npc.activation.backlog_max":
                _activationBacklogMax[Tag(tags, "kind")] = value;
                break;
            case "fineage.persist.pending":
                _persistPending = value;
                break;
            case "fineage.economy.persist.pending":
                _economyPersistPending = value;
                break;
            case "fineage.socket_write.bytes":
                Row(EndpointOf(tags)).SocketWriteBytes.Record(value);
                break;
            case "fineage.socket_write.frames":
                Row(EndpointOf(tags)).SocketWriteFrames.Record(value);
                break;
            case "fineage.fanout.queue.depth":
                _fanoutQueueDepth.Record(value);
                break;
            case "fineage.fanout.queue.work":
                _fanoutQueueWork.Record(value);
                break;
            case "fineage.npc.pathfind.nodes":
                _pathfindNodes.Record(value);
                break;
            case "fineage.npc.pathfind.queued":
                _pathfindQueued.Record(value);
                break;
            case "fineage.fanout.events_per_batch":
                _fanoutEventsPerBatch.Record(value);
                break;
            case "fineage.fanout.recipients_per_event":
                _fanoutRecipientsPerEvent.Record(value);
                break;
            case "fineage.fanout.queue.bytes":
                _fanoutQueueBytes.Record(value);
                break;
            case "fineage.outbound.queue.frames":
                _outboundQueueFrames.Record(value);
                break;
            case "fineage.outbound.queue.bytes":
                _outboundQueueBytes.Record(value);
                break;
        }
    }

    void ApplyDouble(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        switch (instrument.Name)
        {
            case "fineage.persist.oldest_age_ms":
                _persistOldestAgeMs = value;
                break;
            case "fineage.economy.persist.oldest_age_ms":
                _economyPersistOldestAgeMs = value;
                break;
            case "fineage.fanout.dispatcher.enqueue_wait.ms":
                _fanoutDispatcherEnqueueWait.Record(value);
                break;
            case "fineage.fanout.stripe.enqueue_wait.ms":
                _fanoutStripeEnqueueWait.Record(value);
                break;
            case "fineage.handler.ms":
                Row(EndpointOf(tags)).Handler.Record(value);
                break;
            case "fineage.tick.ms":
                _tick.Record(value);
                break;
            case "fineage.tick.stage.ms":
                Stage(StageOf(tags)).Duration.Record(value);
                break;
            case "fineage.enter.try_ms":
                _enterTry.Record(value);
                break;
            case "fineage.enter.try_aoi_bind_ms":
                _enterTryAoiBind.Record(value);
                break;
            case "fineage.enter.plan_ms":
                _enterPlan.Record(value);
                break;
            case "fineage.command.apply_ms":
                CommandApply(CommandOf(tags)).Duration.Record(value);
                break;
            case "fineage.command.timeline.ms":
                CommandTimeline(PhaseOf(tags)).Duration.Record(value);
                break;
            case "fineage.command.queue.wait.ms":
                CommandQueueWait(IngressClassOf(tags)).Duration.Record(value);
                break;
            case "fineage.command.queue.age.ms":
                _commandQueueAge.Record(value);
                break;
            case "fineage.persist.write.ms":
                PersistWrite(PersistKindOf(tags)).Duration.Record(value);
                break;
            case "fineage.tick.max_ms":
                _tickMaxMs = value;
                break;
            case "fineage.command.queue.age_max_ms":
                _commandQueueAgeMaxMs = value;
                break;
            case "fineage.step.watchdog.stall_max_ms":
                _watchdogStallMaxMs = value;
                break;
            case "fineage.enter.self_projection_ms":
                _enterSelf.Record(value);
                break;
            case "fineage.enter.known_snapshot_ms":
                _enterKnownSnapshot.Record(value);
                break;
            case "fineage.enter.known_encode_ms":
                _enterKnownEncode.Record(value);
                break;
            case "fineage.enter.peer_fanout_ms":
                _enterPeerFanout.Record(value);
                break;
            case "fineage.enter.known_entities":
                _enterKnownEntities.Record(value);
                break;
            case "fineage.enter.known_players":
                _enterKnownPlayers.Record(value);
                break;
            case "fineage.enter.frames":
                _enterFrames.Record(value);
                break;
            case "fineage.enter.bytes":
                _enterBytes.Record(value);
                break;
            case "fineage.tick.step.fanout_drain.ms":
                _tickStepFanoutDrain.Record(value);
                break;
            case "fineage.command_to_mailboxes.ms":
                _commandToMailboxes.Record(value);
                break;
            case "fineage.fanout.batch_process.ms":
                _fanoutBatchProcess.Record(value);
                break;
            case "fineage.fanout.dispatch_wait.ms":
                _fanoutDispatchWait.Record(value);
                break;
            case "fineage.fanout.enqueue_to_socket_write.ms":
                _fanoutEnqueueToSocketWrite.Record(value);
                break;
            case "fineage.fanout.queue.oldest_age.ms":
                _fanoutQueueAge.Record(value);
                break;
            case "fineage.npc.pathfind.wait.ms":
                LabeledStage(_pathfindWait, PriorityOf(tags)).Duration.Record(value);
                break;
            case "fineage.npc.pathfind.oldest_age.ms":
                LabeledStage(_pathfindOldestAge, PriorityOf(tags)).Duration.Record(value);
                break;
            case "fineage.npc.pathfind.unserved_wait.ms":
                LabeledStage(_pathfindUnservedWait, PriorityOf(tags)).Duration.Record(value);
                break;
            case "fineage.outbound.queue.oldest_age.ms":
                _outboundQueueAge.Record(value);
                break;
            case "fineage.socket_write.ms":
                Row(EndpointOf(tags)).SocketWriteMs.Record(value);
                break;
        }
    }

    MutableStage Stage(string stage) => LabeledStage(_stages, stage);

    MutableStage CommandTimeline(string phase) => LabeledStage(_commandTimelinePhases, phase);

    MutableStage CommandApply(string command) => LabeledStage(_commandApplyByName, command);

    MutableStage CommandQueueWait(string ingressClass) => LabeledStage(_commandQueueWaitByClass, ingressClass);

    MutableStage PersistWrite(string kind) => LabeledStage(_persistWriteByKind, kind);

    static MutableStage LabeledStage(Dictionary<string, MutableStage> map, string key)
    {
        if (!map.TryGetValue(key, out MutableStage? row))
        {
            row = new MutableStage();
            map.Add(key, row);
        }

        return row;
    }

    static List<StageMetrics> FreezeLabeled(Dictionary<string, MutableStage> map) =>
        map.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value.Freeze(pair.Key))
            .ToList();

    static string StageOf(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == "stage" && tag.Value is string value)
                return value;
        }

        return "";
    }

    static string MoveKindOf(ReadOnlySpan<KeyValuePair<string, object?>> tags) => Tag(tags, "move_kind");

    /// <summary>Path priority tag: <c>p1</c> / <c>p2</c> / <c>p3</c>, matching the enum's numeric value.</summary>
    static string PriorityOf(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        Tag(tags, "priority") is { Length: > 0 } priority ? priority : "unknown";

    static string CommandOf(ReadOnlySpan<KeyValuePair<string, object?>> tags) => Tag(tags, "command");

    static string IngressClassOf(ReadOnlySpan<KeyValuePair<string, object?>> tags) => Tag(tags, "class");

    static string PhaseOf(ReadOnlySpan<KeyValuePair<string, object?>> tags) => Tag(tags, "phase");

    static string PersistKindOf(ReadOnlySpan<KeyValuePair<string, object?>> tags) => Tag(tags, "kind");

    static void AddLane(Dictionary<string, long> counts, string lane, long value)
    {
        if (lane.Length == 0)
            lane = "reliable";
        counts[lane] = counts.GetValueOrDefault(lane) + value;
    }

    static Dictionary<string, long> FreezeLane(Dictionary<string, long> counts) =>
        counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>The tag values in <paramref name="names"/> order, unit-separator joined; a missing tag is "unspecified".</summary>
    internal static string SeriesKey(ReadOnlySpan<KeyValuePair<string, object?>> tags, string[] names)
    {
        if (names.Length == 0)
            return "";
        var values = new string[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string value = "";
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == names[i])
                {
                    value = Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    break;
                }
            }

            values[i] = value.Length == 0 ? "unspecified" : value;
        }

        return string.Join('\u001f', values);
    }

    static string Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string name)
    {
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == name && tag.Value is string value)
                return value;
        }

        return "";
    }

    MutableEndpoint Row(string endpoint)
    {
        if (!_rows.TryGetValue(endpoint, out MutableEndpoint? row))
        {
            row = new MutableEndpoint();
            _rows.Add(endpoint, row);
        }

        return row;
    }

    static bool IsOurs(Instrument instrument) => instrument.Meter.Name == FineageTelemetry.MeterName;

    static string EndpointOf(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == "endpoint" && tag.Value is string value)
                return value;
        }

        return "";
    }

    sealed class MutableEndpoint
    {
        public long Connections;
        public long FramesIn;
        public long FramesOut;
        public long BytesIn;
        public long BytesOut;
        public long PacketsRejected;
        public long PacketsErrored;
        public long ConnectionsRejected;
        public long OutboundDropped;
        public DurationHistogram Handler { get; } = new();
        public DurationHistogram SocketWriteMs { get; } = new();
        public BoundedCountHistogram SocketWriteBytes { get; } = new(MetricsText.SocketWriteByteBounds);
        public BoundedCountHistogram SocketWriteFrames { get; } = new(MetricsText.SocketWriteFrameBounds);

        public EndpointMetrics Freeze(string endpoint) => new()
        {
            Endpoint = endpoint,
            Connections = Connections,
            FramesIn = FramesIn,
            FramesOut = FramesOut,
            BytesIn = BytesIn,
            BytesOut = BytesOut,
            PacketsRejected = PacketsRejected,
            PacketsErrored = PacketsErrored,
            ConnectionsRejected = ConnectionsRejected,
            OutboundDropped = OutboundDropped,
            Handler = Handler.Freeze(),
            SocketWriteMs = SocketWriteMs.Freeze(),
            SocketWriteBytes = SocketWriteBytes.Freeze(),
            SocketWriteFrames = SocketWriteFrames.Freeze()
        };
    }

    sealed class MutableStage
    {
        public long Failures;
        public DurationHistogram Duration { get; } = new();

        public StageMetrics Freeze(string stage) => new()
        {
            Stage = stage,
            Duration = Duration.Freeze(),
            Failures = Failures
        };
    }

    sealed class DurationHistogram
    {
        readonly long[] _buckets = new long[MetricsText.DurationBoundsMs.Length + 1];
        double _sum;
        long _count;

        public void Record(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                return;
            _sum += value;
            _count++;
            double[] bounds = MetricsText.DurationBoundsMs;
            int index = bounds.Length;
            for (int i = 0; i < bounds.Length; i++)
            {
                if (value <= bounds[i])
                {
                    index = i;
                    break;
                }
            }

            _buckets[index]++;
        }

        public HistogramMetrics Freeze()
        {
            var cumulative = new long[_buckets.Length];
            long running = 0;
            for (int i = 0; i < _buckets.Length; i++)
            {
                running += _buckets[i];
                cumulative[i] = running;
            }

            return new HistogramMetrics { Sum = _sum, Count = _count, CumulativeBuckets = cumulative };
        }
    }

    sealed class BoundedCountHistogram(double[] bounds)
    {
        readonly long[] _buckets = new long[bounds.Length + 1];
        double _sum;
        long _count;

        public void Record(int value)
        {
            if (value < 0)
                return;
            _sum += value;
            _count++;
            int index = bounds.Length;
            for (int i = 0; i < bounds.Length; i++)
            {
                if (value <= bounds[i])
                {
                    index = i;
                    break;
                }
            }

            _buckets[index]++;
        }

        public HistogramMetrics Freeze()
        {
            var cumulative = new long[_buckets.Length];
            long running = 0;
            for (int i = 0; i < _buckets.Length; i++)
            {
                running += _buckets[i];
                cumulative[i] = running;
            }

            return new HistogramMetrics { Sum = _sum, Count = _count, CumulativeBuckets = cumulative };
        }
    }
}
