using System.Diagnostics;

namespace Fineage.Zone.World;

/// <summary>
/// Thread-local span while a sealed fan-out batch tied to a command commit is being delivered (mailbox timing only).
/// </summary>
internal static class CommandFanoutTimingContext
{
    [ThreadStatic] static long _commitAtTicks;
    [ThreadStatic] static int _mailboxSampled;
    [ThreadStatic] static int _deliveryActive;
    [ThreadStatic] static int _bytes;
    [ThreadStatic] static int _recipients;

    public static void BeginDelivery()
    {
        _deliveryActive = 1;
        _bytes = 0;
        _recipients = 0;
    }

    public static void EndDelivery() => _deliveryActive = 0;

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

    /// <summary>Timestamp to carry onto a mailbox frame, or 0 outside a command batch.</summary>
    public static long MailboxStamp() => _commitAtTicks > 0 ? Stopwatch.GetTimestamp() : 0;

    public static void NoteRecipient(int payloadBytes)
    {
        if (_deliveryActive == 0)
            return;
        _recipients++;
        if (payloadBytes > 0)
            _bytes += payloadBytes;
    }

    public static int TakeRecipients()
    {
        int n = _recipients;
        _recipients = 0;
        return n;
    }

    public static int TakeBytes()
    {
        int n = _bytes;
        _bytes = 0;
        return n;
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