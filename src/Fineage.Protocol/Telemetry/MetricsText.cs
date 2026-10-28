using System.Globalization;
using System.Text;

namespace Fineage.Protocol.Telemetry;

public readonly record struct TrafficTotals(
    long Connections,
    int PlayersOnline,
    long FramesIn,
    long FramesOut,
    long BytesIn,
    long BytesOut,
    long PacketsRejected,
    long PacketsErrored,
    double HandlerMsSum,
    long HandlerCount,
    double TickMsSum,
    long TickCount);

public sealed class HistogramMetrics
{
    public double Sum { get; init; }
    public long Count { get; init; }
    public long[] CumulativeBuckets { get; init; } = [];
}

public sealed class EndpointMetrics
{
    public required string Endpoint { get; init; }
    public long Connections { get; init; }
    public long FramesIn { get; init; }
    public long FramesOut { get; init; }
    public long BytesIn { get; init; }
    public long BytesOut { get; init; }
    public long PacketsRejected { get; init; }
    public long PacketsErrored { get; init; }
    public long ConnectionsRejected { get; init; }
    public long OutboundDropped { get; init; }
    public HistogramMetrics Handler { get; init; } = new();
}

public sealed class MetricsSnapshot
{
    public int PlayersOnline { get; init; }
    public HistogramMetrics Tick { get; init; } = new();
    public IReadOnlyList<EndpointMetrics> Endpoints { get; init; } = [];
}

public static class MetricsText
{
    public static readonly double[] DurationBoundsMs = [0.5, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000];

    public static TrafficTotals Totals(MetricsSnapshot snapshot)
    {
        long connections = 0;
        long framesIn = 0;
        long framesOut = 0;
        long bytesIn = 0;
        long bytesOut = 0;
        long rejected = 0;
        long errored = 0;
        double handlerSum = 0;
        long handlerCount = 0;
        foreach (EndpointMetrics endpoint in snapshot.Endpoints)
        {
            connections += endpoint.Connections;
            framesIn += endpoint.FramesIn;
            framesOut += endpoint.FramesOut;
            bytesIn += endpoint.BytesIn;
            bytesOut += endpoint.BytesOut;
            rejected += endpoint.PacketsRejected;
            errored += endpoint.PacketsErrored;
            handlerSum += endpoint.Handler.Sum;
            handlerCount += endpoint.Handler.Count;
        }

        return new TrafficTotals(
            connections,
            snapshot.PlayersOnline,
            framesIn,
            framesOut,
            bytesIn,
            bytesOut,
            rejected,
            errored,
            handlerSum,
            handlerCount,
            snapshot.Tick.Sum,
            snapshot.Tick.Count);
    }

    public static string FormatSummary(TrafficTotals previous, TrafficTotals current, double elapsedSeconds)
    {
        double inPps = Rate(current.FramesIn, previous.FramesIn, elapsedSeconds);
        double outPps = Rate(current.FramesOut, previous.FramesOut, elapsedSeconds);
        double tickAvg = Average(current.TickMsSum - previous.TickMsSum, current.TickCount - previous.TickCount);
        double handlerAvg = Average(current.HandlerMsSum - previous.HandlerMsSum, current.HandlerCount - previous.HandlerCount);
        return FormattableString.Invariant(
            $"fineage conn={current.Connections} online={current.PlayersOnline} in_pps={inPps:0.0} out_pps={outPps:0.0} bytes_in={Delta(current.BytesIn, previous.BytesIn)} bytes_out={Delta(current.BytesOut, previous.BytesOut)} tick_ms_avg={tickAvg:0.00} handler_ms_avg={handlerAvg:0.00} rejected={Delta(current.PacketsRejected, previous.PacketsRejected)} errored={Delta(current.PacketsErrored, previous.PacketsErrored)}");
    }

    public static double Rate(long current, long previous, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0)
            return 0;
        long delta = current - previous;
        if (delta < 0)
            delta = 0;
        return delta / elapsedSeconds;
    }

    public static long Delta(long current, long previous)
    {
        long delta = current - previous;
        return delta < 0 ? 0 : delta;
    }

    public static double Average(double sumDelta, long countDelta)
    {
        if (countDelta <= 0 || sumDelta < 0 || double.IsNaN(sumDelta))
            return 0;
        return sumDelta / countDelta;
    }

    public static string Prometheus(MetricsSnapshot snapshot, double inPacketsPerSecond, double outPacketsPerSecond)
    {
        var sb = new StringBuilder(2048);
        AppendGauge(sb, "fineage_connections", "Open TCP sessions", snapshot.Endpoints, static e => e.Connections);
        AppendCounter(sb, "fineage_connections_rejected_total", "Accepts dropped because the process was at its connection cap", snapshot.Endpoints, static e => e.ConnectionsRejected);
        AppendCounter(sb, "fineage_frames_in_total", "Decoded inbound packets", snapshot.Endpoints, static e => e.FramesIn);
        AppendCounter(sb, "fineage_frames_out_total", "Outbound packets written to a session", snapshot.Endpoints, static e => e.FramesOut);
        AppendCounter(sb, "fineage_bytes_in_total", "Inbound packet bytes, including the frame header", snapshot.Endpoints, static e => e.BytesIn);
        AppendCounter(sb, "fineage_bytes_out_total", "Outbound packet bytes, including the frame header", snapshot.Endpoints, static e => e.BytesOut);
        AppendCounter(sb, "fineage_outbound_dropped_total", "Frames dropped because a session outbound queue was full", snapshot.Endpoints, static e => e.OutboundDropped);
        AppendCounter(sb, "fineage_packets_rejected_total", "Packets refused before normal handling", snapshot.Endpoints, static e => e.PacketsRejected);
        AppendCounter(sb, "fineage_packets_errored_total", "Packets that produced an error response or failed while handling", snapshot.Endpoints, static e => e.PacketsErrored);
        AppendHelp(sb, "fineage_packets_per_second", "Packet rate over the most recent summary or scrape window");
        sb.Append("# TYPE fineage_packets_per_second gauge\n");
        sb.Append("fineage_packets_per_second{direction=\"in\"} ").Append(Num(inPacketsPerSecond)).Append('\n');
        sb.Append("fineage_packets_per_second{direction=\"out\"} ").Append(Num(outPacketsPerSecond)).Append('\n');
        AppendHistogram(sb, "fineage_handler_ms", "Packet handler duration", snapshot.Endpoints);
        AppendHelp(sb, "fineage_tick_ms", "Zone simulation tick duration");
        sb.Append("# TYPE fineage_tick_ms histogram\n");
        AppendHistogramSamples(sb, "fineage_tick_ms", "", snapshot.Tick);
        AppendHelp(sb, "fineage_zone_online", "Players currently in a zone simulation");
        sb.Append("# TYPE fineage_zone_online gauge\n");
        sb.Append("fineage_zone_online ").Append(snapshot.PlayersOnline.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return sb.ToString();
    }

    static void AppendGauge(StringBuilder sb, string name, string help, IReadOnlyList<EndpointMetrics> endpoints, Func<EndpointMetrics, long> value)
    {
        if (!HasEndpoint(endpoints))
            return;
        AppendHelp(sb, name, help);
        sb.Append("# TYPE ").Append(name).Append(" gauge\n");
        foreach (EndpointMetrics endpoint in endpoints)
            sb.Append(name).Append("{endpoint=\"").Append(Escape(endpoint.Endpoint)).Append("\"} ")
                .Append(value(endpoint).ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    static void AppendCounter(StringBuilder sb, string name, string help, IReadOnlyList<EndpointMetrics> endpoints, Func<EndpointMetrics, long> value)
    {
        if (!HasEndpoint(endpoints))
            return;
        AppendHelp(sb, name, help);
        sb.Append("# TYPE ").Append(name).Append(" counter\n");
        foreach (EndpointMetrics endpoint in endpoints)
            sb.Append(name).Append("{endpoint=\"").Append(Escape(endpoint.Endpoint)).Append("\"} ")
                .Append(value(endpoint).ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    static void AppendHistogram(StringBuilder sb, string name, string help, IReadOnlyList<EndpointMetrics> endpoints)
    {
        if (!HasEndpoint(endpoints))
            return;
        AppendHelp(sb, name, help);
        sb.Append("# TYPE ").Append(name).Append(" histogram\n");
        foreach (EndpointMetrics endpoint in endpoints)
        {
            string labels = "endpoint=\"" + Escape(endpoint.Endpoint) + "\"";
            AppendHistogramSamples(sb, name, labels, endpoint.Handler);
        }
    }

    static void AppendHistogramSamples(StringBuilder sb, string name, string labels, HistogramMetrics histogram)
    {
        long[] buckets = histogram.CumulativeBuckets;
        for (int i = 0; i < DurationBoundsMs.Length; i++)
        {
            long cumulative = i < buckets.Length ? buckets[i] : 0;
            sb.Append(name).Append("_bucket{");
            if (labels.Length > 0)
                sb.Append(labels).Append(',');
            sb.Append("le=\"").Append(DurationBoundsMs[i].ToString("0.###", CultureInfo.InvariantCulture)).Append("\"} ")
                .Append(cumulative.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        long inf = buckets.Length > DurationBoundsMs.Length ? buckets[DurationBoundsMs.Length] : (buckets.Length > 0 ? buckets[^1] : 0);
        sb.Append(name).Append("_bucket{");
        if (labels.Length > 0)
            sb.Append(labels).Append(',');
        sb.Append("le=\"+Inf\"} ").Append(inf.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(name).Append("_sum");
        AppendBareLabels(sb, labels);
        sb.Append(' ').Append(Num(histogram.Sum)).Append('\n');
        sb.Append(name).Append("_count");
        AppendBareLabels(sb, labels);
        sb.Append(' ').Append(histogram.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    static void AppendBareLabels(StringBuilder sb, string labels)
    {
        if (labels.Length == 0)
            return;
        sb.Append('{').Append(labels).Append('}');
    }

    static void AppendHelp(StringBuilder sb, string name, string help)
    {
        sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
    }

    static bool HasEndpoint(IReadOnlyList<EndpointMetrics> endpoints) => endpoints.Count > 0;

    static string Num(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            return "0";
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
