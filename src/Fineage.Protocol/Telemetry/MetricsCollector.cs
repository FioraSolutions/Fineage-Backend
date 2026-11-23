using System.Diagnostics.Metrics;

namespace Fineage.Protocol.Telemetry;

public sealed class MetricsCollector : IDisposable
{
    readonly MeterListener _listener = new();
    readonly object _gate = new();
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
    readonly Dictionary<string, MutableStage> _deathPathByPhase = new(StringComparer.Ordinal);
    readonly Dictionary<string, double> _deathPathMaxUnits = new(StringComparer.Ordinal);
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
    }

    public MetricsSnapshot Snapshot()
    {
        _listener.RecordObservableInstruments();
        lock (_gate)
        {
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
                DeathPathByPhase = FreezeLabeled(_deathPathByPhase),
                DeathPathMaxUnits = _deathPathMaxUnits
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
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

    public void Dispose() => _listener.Dispose();

    void OnLong(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (!IsOurs(instrument))
            return;
        string endpoint = EndpointOf(tags);
        lock (_gate)
        {
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
    }

    void OnInt(Instrument instrument, int value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (!IsOurs(instrument))
            return;
        lock (_gate)
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
    }

    void OnDouble(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (!IsOurs(instrument))
            return;
        lock (_gate)
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
                case "fineage.death.path.ms":
                    DeathPath(PhaseOf(tags)).Duration.Record(value);
                    break;
                case "fineage.death.path.max_units":
                    NoteDeathPathUnits(PhaseOf(tags), value);
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
    }

    MutableStage Stage(string stage) => LabeledStage(_stages, stage);

    MutableStage CommandTimeline(string phase) => LabeledStage(_commandTimelinePhases, phase);

    MutableStage CommandApply(string command) => LabeledStage(_commandApplyByName, command);

    MutableStage DeathPath(string phase) => LabeledStage(_deathPathByPhase, phase);

    void NoteDeathPathUnits(string phase, double units)
    {
        if (phase.Length == 0)
            return;
        _deathPathMaxUnits[phase] = Math.Max(_deathPathMaxUnits.GetValueOrDefault(phase), units);
    }

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
