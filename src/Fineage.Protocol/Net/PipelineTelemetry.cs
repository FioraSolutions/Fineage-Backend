using Fineage.Protocol.Telemetry;

namespace Fineage.Protocol.Net;

public static class PipelineTelemetry
{
    public static void FrameIn(string endpoint) => FrameIn(endpoint, 0);

    public static void FrameIn(string endpoint, int bytes)
    {
        var tag = Tag(endpoint);
        FineageTelemetry.FramesIn.Add(1, tag);
        if (bytes > 0)
            FineageTelemetry.BytesIn.Add(bytes, tag);
    }

    public static void FrameOut(string endpoint) => FrameOut(endpoint, 0);

    public static void FrameOut(string endpoint, int bytes)
    {
        var tag = Tag(endpoint);
        FineageTelemetry.FramesOut.Add(1, tag);
        if (bytes > 0)
            FineageTelemetry.BytesOut.Add(bytes, tag);
    }

    public static void OutboundDropped(string endpoint) =>
        FineageTelemetry.OutboundDropped.Add(1, Tag(endpoint));

    public static void PacketRejected(string endpoint) =>
        FineageTelemetry.PacketsRejected.Add(1, Tag(endpoint));

    public static void PacketErrored(string endpoint) =>
        FineageTelemetry.PacketsErrored.Add(1, Tag(endpoint));

    public static void Handler(string endpoint, double milliseconds) =>
        FineageTelemetry.HandlerMilliseconds.Record(milliseconds, Tag(endpoint));

    static KeyValuePair<string, object?> Tag(string endpoint) => new("endpoint", endpoint);
}
