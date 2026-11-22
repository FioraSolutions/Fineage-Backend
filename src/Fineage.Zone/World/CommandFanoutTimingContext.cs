using System.Diagnostics;

namespace Fineage.Zone.World;

/// <summary>
/// Thread-local span while a sealed fan-out batch tied to a command commit is being delivered (mailbox timing only).
/// </summary>
internal static class CommandFanoutTimingContext
{
    [ThreadStatic] static long _commitAtTicks;
    [ThreadStatic] static int _mailboxSampled;

    public static void BeginCommandBatch(long commitAtTicks)
    {
        _commitAtTicks = commitAtTicks;
        _mailboxSampled = 0;
    }

    public static void EndCommandBatch()
    {
        _commitAtTicks = 0;
        _mailboxSampled = 0;
    }

    public static bool TryRecordMailbox(out double milliseconds)
    {
        milliseconds = 0;
        if (_commitAtTicks <= 0 || Interlocked.Exchange(ref _mailboxSampled, 1) != 0)
            return false;
        milliseconds = (Stopwatch.GetTimestamp() - _commitAtTicks) * 1000.0 / Stopwatch.Frequency;
        return true;
    }
}