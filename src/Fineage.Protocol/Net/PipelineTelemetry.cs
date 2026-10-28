using Fineage.Protocol.Telemetry;

namespace Fineage.Protocol.Net;

public static class PipelineTelemetry
{
    public static void FrameIn(string endpoint) =>
        FineageTelemetry.FramesIn.Add(1, new KeyValuePair<string, object?>("endpoint", endpoint));

    public static void FrameOut(string endpoint) =>
        FineageTelemetry.FramesOut.Add(1, new KeyValuePair<string, object?>("endpoint", endpoint));
}
