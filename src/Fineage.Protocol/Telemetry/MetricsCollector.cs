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
    readonly Dictionary<string, MutableStage> _stages = new(StringComparer.Ordinal);
    long _tickOverruns;
    long _ticksSkipped;
    long _regionHandoffs;
    readonly Dictionary<string, long> _ledgerAdjustments = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _persistSnapshots = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _fanoutDropped = new(StringComparer.Ordinal);
    long _ledgerFlushFailures;
    int _persistPending;
    double _persistOldestAgeMs;
    int _online;
    int _commandIngressQueued;

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
                Stages = _stages
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Value.Freeze(pair.Key))
                    .ToList(),
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
                case "fineage.persist.pending":
                    _persistPending = value;
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
            }
        }
    }

    MutableStage Stage(string stage)
    {
        if (!_stages.TryGetValue(stage, out MutableStage? row))
        {
            row = new MutableStage();
            _stages.Add(stage, row);
        }

        return row;
    }

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
            Handler = Handler.Freeze()
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
}
