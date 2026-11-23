using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

/// <summary>Per-lane mailbox counters: blocked, full, coalesced, evicted.</summary>
static class OutboundLaneMetrics
{
    static long _lastLogMs;

    public static void Blocked(OutboundLane lane, int count = 1) =>
        FineageTelemetry.OutboundLaneBlocked.Add(count, Tag(lane));

    public static void Full(OutboundLane lane, int count = 1) =>
        FineageTelemetry.OutboundLaneFull.Add(count, Tag(lane));

    public static void Coalesced(OutboundLane lane, int count = 1) =>
        FineageTelemetry.OutboundLaneCoalesced.Add(count, Tag(lane));

    public static void Evicted(OutboundLane lane, int count = 1) =>
        FineageTelemetry.OutboundLaneEvicted.Add(count, Tag(lane));

    /// <summary>Bytes removed by coalescing (movement) or shedding (visual) to keep a mailbox inside its budget.</summary>
    public static void Shed(OutboundLane lane, int bytes)
    {
        if (bytes > 0)
            FineageTelemetry.OutboundShedBytes.Add(bytes, Tag(lane));
    }

    /// <summary>Movement and visual eviction can be hot. Snapshot replace and reliable overflow log on their own.</summary>
    public static void LogLimited(string message)
    {
        long nowMs = Environment.TickCount64;
        long last = Volatile.Read(ref _lastLogMs);
        if (nowMs - last < 1000 || Interlocked.CompareExchange(ref _lastLogMs, nowMs, last) != last)
            return;
        NetHostLog.Error(message);
    }

    static KeyValuePair<string, object?> Tag(OutboundLane lane) =>
        new("lane", OutboundLanePolicy.Name(lane));
}
