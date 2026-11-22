using System.Threading;
using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

/// <summary>Observable fan-out backpressure: counter by opcode_group and rate-limited diagnostics.</summary>
public static class NetFanoutDropTelemetry
{
    static long _lastWarnTickMs;

    public static void RecordStripeBatchFull(
        ulong connectionId,
        string endpoint,
        ushort opcode,
        int stripeBatchDepth,
        int outboundQueued)
    {
        string group = FanoutOpcodeGroup.For(opcode);
        FineageTelemetry.FanoutDropped.Add(
            1,
            new KeyValuePair<string, object?>("opcode_group", group));
        NetTelemetry.OutboundDropped(endpoint);

        long nowMs = Environment.TickCount64;
        long last = Volatile.Read(ref _lastWarnTickMs);
        if (nowMs - last < 1000 || Interlocked.CompareExchange(ref _lastWarnTickMs, nowMs, last) != last)
            return;

        NetHostLog.Warn(
            $"fanout drop: connectionId={connectionId} endpoint={endpoint} opcode_group={group} "
            + $"stripe_batch_depth={stripeBatchDepth} outbound_queued={outboundQueued}");
    }
}