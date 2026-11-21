using System.Diagnostics;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>Wall-time samples for command apply and move sub-phases (tagged OTel histograms).</summary>
static class CommandApplyTiming
{
    public static void RecordCommand(string commandName, double milliseconds) =>
        FineageTelemetry.CommandApplyMilliseconds.Record(milliseconds,
            new KeyValuePair<string, object?>("command", commandName));

    public static void RecordMovePhase(string phase, double milliseconds) =>
        FineageTelemetry.MoveApplyPhaseMilliseconds.Record(milliseconds,
            new KeyValuePair<string, object?>("phase", phase));

    public static void RecordReleasePhase(string phase, double milliseconds) =>
        FineageTelemetry.CommandReleasePhaseMilliseconds.Record(milliseconds,
            new KeyValuePair<string, object?>("phase", phase));

    public static double ElapsedMs(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}