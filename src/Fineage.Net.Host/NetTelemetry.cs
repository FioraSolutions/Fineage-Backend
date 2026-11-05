using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

/// <summary>
/// Transport counters for the Net path, the same instrument set the retired V2 pipeline published so
/// dashboards keep working. TCP instruments carry the <c>endpoint</c> tag (<c>login</c> or <c>zone</c>).
/// </summary>
public static class NetTelemetry
{
    public static void ConnectionOpened(string endpoint) =>
        FineageTelemetry.Connections.Add(1, Tag(endpoint));

    public static void ConnectionClosed(string endpoint) =>
        FineageTelemetry.Connections.Add(-1, Tag(endpoint));

    public static void ConnectionRejected(string endpoint) =>
        FineageTelemetry.ConnectionsRejected.Add(1, Tag(endpoint));

    public static void FrameIn(string endpoint, int bytes)
    {
        var tag = Tag(endpoint);
        FineageTelemetry.FramesIn.Add(1, tag);
        if (bytes > 0)
            FineageTelemetry.BytesIn.Add(bytes, tag);
    }

    public static void FrameOut(string endpoint, int bytes)
    {
        var tag = Tag(endpoint);
        FineageTelemetry.FramesOut.Add(1, tag);
        if (bytes > 0)
            FineageTelemetry.BytesOut.Add(bytes, tag);
    }

    public static void PacketRejected(string endpoint) =>
        FineageTelemetry.PacketsRejected.Add(1, Tag(endpoint));

    public static void PacketErrored(string endpoint) =>
        FineageTelemetry.PacketsErrored.Add(1, Tag(endpoint));

    public static void Handler(string endpoint, double milliseconds) =>
        FineageTelemetry.HandlerMilliseconds.Record(milliseconds, Tag(endpoint));

    static KeyValuePair<string, object?> Tag(string endpoint) => new("endpoint", endpoint);
}
