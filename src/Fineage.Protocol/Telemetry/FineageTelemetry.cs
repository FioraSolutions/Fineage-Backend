using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Fineage.Protocol.Telemetry;

/// <summary>
/// Process-wide counters and traces. Export by subscribing the OpenTelemetry SDK to
/// meter <see cref="MeterName"/> and activity source <see cref="ActivitySourceName"/>.
/// TCP instruments use tag <c>endpoint</c> (<c>login</c> or <c>zone</c> in production CLI).
/// </summary>
public static class FineageTelemetry
{
    public const string MeterName = "Fineage.Server";
    public const string ActivitySourceName = "Fineage.Server";

    public static readonly ActivitySource Activity = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName, "0.4.0");

    public static readonly UpDownCounter<long> Connections =
        Meter.CreateUpDownCounter<long>("fineage.connections", description: "Open TCP sessions");

    public static readonly Counter<long> ConnectionsRejected =
        Meter.CreateCounter<long>("fineage.connections.rejected", description: "Accepts dropped because the process was at its connection cap");

    public static readonly Counter<long> FramesIn =
        Meter.CreateCounter<long>("fineage.frames.in", description: "Decoded inbound packets");

    public static readonly Counter<long> FramesOut =
        Meter.CreateCounter<long>("fineage.frames.out", description: "Outbound packets written to a session");

    public static readonly Counter<long> BytesIn =
        Meter.CreateCounter<long>("fineage.bytes.in", unit: "By", description: "Inbound packet bytes, including the frame header");

    public static readonly Counter<long> BytesOut =
        Meter.CreateCounter<long>("fineage.bytes.out", unit: "By", description: "Outbound packet bytes, including the frame header");

    public static readonly Counter<long> OutboundDropped =
        Meter.CreateCounter<long>("fineage.outbound.dropped", description: "Frames dropped because a session outbound queue was full");

    public static readonly Counter<long> PacketsRejected =
        Meter.CreateCounter<long>("fineage.packets.rejected", description: "Packets refused before normal handling (rate limit, unknown opcode, wrong port)");

    public static readonly Counter<long> PacketsErrored =
        Meter.CreateCounter<long>("fineage.packets.errored", description: "Packets that produced an error response or failed while handling");

    public static readonly Histogram<double> HandlerMilliseconds =
        Meter.CreateHistogram<double>("fineage.handler.ms", unit: "ms", description: "Packet handler duration");

    public static readonly Histogram<double> TickMilliseconds =
        Meter.CreateHistogram<double>("fineage.tick.ms", unit: "ms", description: "Zone simulation tick duration");

    public static readonly ObservableGauge<double> PacketsPerSecond = Meter.CreateObservableGauge(
        "fineage.packets.per_second",
        ObservePacketRates,
        unit: "{packet}/s",
        description: "Packet rate over the most recent console summary or /metrics scrape window");

    static readonly object RateGate = new();
    static double _inboundPacketsPerSecond;
    static double _outboundPacketsPerSecond;

    public static void SetPacketRates(double inboundPerSecond, double outboundPerSecond)
    {
        if (double.IsNaN(inboundPerSecond) || inboundPerSecond < 0)
            inboundPerSecond = 0;
        if (double.IsNaN(outboundPerSecond) || outboundPerSecond < 0)
            outboundPerSecond = 0;
        lock (RateGate)
        {
            _inboundPacketsPerSecond = inboundPerSecond;
            _outboundPacketsPerSecond = outboundPerSecond;
        }
    }

    static IEnumerable<Measurement<double>> ObservePacketRates()
    {
        double inbound;
        double outbound;
        lock (RateGate)
        {
            inbound = _inboundPacketsPerSecond;
            outbound = _outboundPacketsPerSecond;
        }

        yield return new Measurement<double>(inbound, new KeyValuePair<string, object?>("direction", "in"));
        yield return new Measurement<double>(outbound, new KeyValuePair<string, object?>("direction", "out"));
    }

    public static readonly ObservableGauge<int> ZoneOnline = Meter.CreateObservableGauge(
        "fineage.zone.online",
        ObserveOnline,
        description: "Players currently in a zone simulation");

    public static Func<int>? OnlineCount { get; set; }

    static IEnumerable<Measurement<int>> ObserveOnline()
    {
        yield return new Measurement<int>(OnlineCount?.Invoke() ?? 0);
    }
}
