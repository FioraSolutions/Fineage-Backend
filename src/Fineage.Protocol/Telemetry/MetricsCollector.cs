using System.Diagnostics.Metrics;

namespace Fineage.Protocol.Telemetry;

/// <summary>
/// Aggregates the Fineage meter for Prometheus scrapes.
/// <para>
/// Recording never takes a lock: each recording thread appends to its own single-producer ring
/// (<see cref="ThreadBuffer"/>), and the aggregation below runs only on the drain thread or inside
/// <see cref="Snapshot"/>, both under <c>_gate</c>. Before this, every counter add and histogram record took one
/// process-wide lock, so the fan-out worker, the stripe workers and the socket pumps serialized on telemetry.
/// A ring that fills before the drain thread empties it is drained by its own producer under <c>_gate</c>
/// (counted in <see cref="BufferFullDrains"/>); values are never dropped.
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
    long _bufferFullDrains;
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
    readonly BoundedCountHistogram _fanoutEventsPerBatch = new(MetricsText.CountBounds);
    readonly BoundedCountHistogram _fanoutRecipientsPerEvent = new(MetricsText.CountBounds);
    readonly BoundedCountHistogram _fanoutQueueBytes = new(MetricsText.SocketWriteByteBounds);
    readonly BoundedCountHistogram _outboundQueueFrames = new(MetricsText.SocketWriteFrameBounds);
    readonly BoundedCountHistogram _outboundQueueBytes = new(MetricsText.SocketWriteByteBounds);
    readonly DurationHistogram _outboundQueueAge = new();
    long _fanoutOverflow;
    long _fanoutCoalesced;
    long _reliableOverflow;
    readonly Dictionary<string, MutableStage> _stages = new(StringComparer.Ordinal);
    readonly Dictionary<string, MutableStage> _commandTimelinePhases = new(StringComparer.Ordinal);
    readonly Dictionary<string, MutableStage> _commandApplyByName = new(StringComparer.Ordinal);
    readonly Dictionary<string, MutableStage> _commandQueueWaitByClass = new(StringComparer.Ordinal);
    long _tickOverruns;
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

    public MetricsCollector()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName)
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
                FanoutEventsPerBatch = _fanoutEventsPerBatch.Freeze(),
                FanoutRecipientsPerEvent = _fanoutRecipientsPerEvent.Freeze(),
                FanoutQueueBytes = _fanoutQueueBytes.Freeze(),
                OutboundQueueFrames = _outboundQueueFrames.Freeze(),
                OutboundQueueBytes = _outboundQueueBytes.Freeze(),
                OutboundQueueOldestAge = _outboundQueueAge.Freeze(),
                FanoutOverflow = _fanoutOverflow,
                FanoutCoalesced = _fanoutCoalesced,
                ReliableOverflow = _reliableOverflow,
                TickOverruns = _tickOverruns,
                TicksSkipped = _ticksSkipped,
                RegionHandoffs = _regionHandoffs,
                LedgerAdjustments = _ledgerAdjustments
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                LedgerFlushFailures = _ledgerFlushFailures,
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
                Endpoints = endpoints
            };
        }
    }


    public long BufferFullDrains => Interlocked.Read(ref _bufferFullDrains);

    public void Dispose()
    {
        _stop.Set();
        _drainer.Join();
        _listener.Dispose();
        _stop.Dispose();
        _local.Dispose();
    }

    void OnLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (IsOurs(instrument))
            Buffer().Append(instrument, ThreadBuffer.KindLong, value, 0, tags);
    }

    void OnInt(Instrument instrument, int value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (IsOurs(instrument))
            Buffer().Append(instrument, ThreadBuffer.KindInt, value, 0, tags);
    }

    void OnDouble(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (IsOurs(instrument))
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

    /// <summary>Applies every published measurement; drops rings whose thread has exited once they are empty.</summary>
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
        long head = buffer.Head;
        long tail = buffer.PublishedTail;
        for (long at = head; at < tail; at++)
        {
            ref ThreadBuffer.Slot slot = ref buffer.SlotAt(at);
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
                    throw new InvalidOperationException($"metrics ring slot has unknown kind {slot.Kind}");
            }

            slot = default;
        }

        buffer.Release(tail);
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
    /// Single-producer ring owned by one recording thread. The producer writes a slot and then publishes the tail;
    /// the consumer (always under the collector gate) reads up to the published tail and then releases the head.
    /// </summary>
    sealed class ThreadBuffer(MetricsCollector owner, Thread thread)
    {
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

        readonly Slot[] _slots = new Slot[BufferCapacity];
        long _head;
        long _tail;

        public Thread Owner { get; } = thread;

        public long Head => Volatile.Read(ref _head);

        public long PublishedTail => Volatile.Read(ref _tail);

        public bool IsEmpty => Volatile.Read(ref _head) == Volatile.Read(ref _tail);

        public ref Slot SlotAt(long at) => ref _slots[at % BufferCapacity];

        public void Release(long head) => Volatile.Write(ref _head, head);

        public void Append(Instrument instrument, byte kind, long longValue, double doubleValue,
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            long tail = _tail;
            if (tail - Volatile.Read(ref _head) >= BufferCapacity)
            {
                // The drain thread fell behind: this producer empties its own ring under the gate. Rare by design;
                // a dropped measurement would make the counters lie.
                Interlocked.Increment(ref owner._bufferFullDrains);
                lock (owner._gate)
                    owner.DrainLocked(this);
            }

            ref Slot slot = ref _slots[tail % BufferCapacity];
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

            Volatile.Write(ref _tail, tail + 1);
        }
    }

    void ApplyLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
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
            case "fineage.fanout.coalesced":
                _fanoutCoalesced += value;
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
            case "fineage.socket_write.bytes":
                Row(EndpointOf(tags)).SocketWriteBytes.Record(value);
                break;
            case "fineage.socket_write.frames":
                Row(EndpointOf(tags)).SocketWriteFrames.Record(value);
                break;
            case "fineage.fanout.queue.depth":
                _fanoutQueueDepth.Record(value);
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

    static string CommandOf(ReadOnlySpan<KeyValuePair<string, object?>> tags) => Tag(tags, "command");

    static string IngressClassOf(ReadOnlySpan<KeyValuePair<string, object?>> tags) => Tag(tags, "class");

    static string PhaseOf(ReadOnlySpan<KeyValuePair<string, object?>> tags) => Tag(tags, "phase");

    static void AddLane(Dictionary<string, long> counts, string lane, long value)
    {
        if (lane.Length == 0)
            lane = "reliable";
        counts[lane] = counts.GetValueOrDefault(lane) + value;
    }

    static Dictionary<string, long> FreezeLane(Dictionary<string, long> counts) =>
        counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

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
