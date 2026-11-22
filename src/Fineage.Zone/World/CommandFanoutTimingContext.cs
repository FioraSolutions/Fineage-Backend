using System.Diagnostics;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Thread-local span while a sealed fan-out batch tied to a command commit is being delivered (mailbox timing only).
/// </summary>
internal static class CommandFanoutTimingContext
{
    [ThreadStatic] static long _commitAtTicks;
    [ThreadStatic] static double _mailboxMaxMs;

    public static void BeginCommandBatch(long commitAtTicks)
    {
        _commitAtTicks = commitAtTicks;
        _mailboxMaxMs = 0;
    }

    public static void EndCommandBatch()
    {
        if (_commitAtTicks > 0 && _mailboxMaxMs > 0)
            FineageTelemetry.CommandToMailboxesMilliseconds.Record(_mailboxMaxMs);
        _commitAtTicks = 0;
        _mailboxMaxMs = 0;
    }

    public static bool TryRecordMailbox(out double milliseconds)
    {
        milliseconds = 0;
        if (_commitAtTicks <= 0)
            return false;
        milliseconds = (Stopwatch.GetTimestamp() - _commitAtTicks) * 1000.0 / Stopwatch.Frequency;
        if (milliseconds > _mailboxMaxMs)
            _mailboxMaxMs = milliseconds;
        return true;
    }
}