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
    public static readonly Meter Meter = new(MeterName, "0.3.0");

    public static readonly UpDownCounter<long> Connections =
        Meter.CreateUpDownCounter<long>("fineage.connections", description: "Open TCP sessions");

    public static readonly Counter<long> ConnectionsRejected =
        Meter.CreateCounter<long>("fineage.connections.rejected", description: "Accepts dropped because the process was at its connection cap");

    public static readonly Counter<long> FramesIn =
        Meter.CreateCounter<long>("fineage.frames.in");

    public static readonly Counter<long> FramesOut =
        Meter.CreateCounter<long>("fineage.frames.out");

    public static readonly Counter<long> OutboundDropped =
        Meter.CreateCounter<long>("fineage.outbound.dropped", description: "Frames dropped because a session outbound queue was full");

    public static readonly Histogram<double> TickMilliseconds =
        Meter.CreateHistogram<double>("fineage.tick.ms", unit: "ms", description: "Zone simulation tick duration");

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
