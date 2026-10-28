using System.Diagnostics.Metrics;

namespace Fineage.Protocol.Telemetry;

public sealed class MetricsCollector : IDisposable
{
    readonly MeterListener _listener = new();
    readonly object _gate = new();
    readonly Dictionary<string, MutableEndpoint> _rows = new(StringComparer.Ordinal);
    readonly DurationHistogram _tick = new();
    int _online;

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
                Tick = _tick.Freeze(),
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
            }
        }
    }

    void OnInt(Instrument instrument, int value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (!IsOurs(instrument) || instrument.Name != "fineage.zone.online")
            return;
        lock (_gate)
            _online = value;
    }

    void OnDouble(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (!IsOurs(instrument))
            return;
        lock (_gate)
        {
            switch (instrument.Name)
            {
                case "fineage.handler.ms":
                    Row(EndpointOf(tags)).Handler.Record(value);
                    break;
                case "fineage.tick.ms":
                    _tick.Record(value);
                    break;
            }
        }
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
