using System.Net.Sockets;

namespace Fineage.LoadTest.Bot;

static class LoadTestDisconnectLog
{
    static long _lastWarnMs;

    public static void LogBotDisconnect(int botId, int selfObjectId, ushort lastSent, ushort lastRecv, Exception ex)
    {
        if (!ShouldWarn())
            return;
        string socket = ex is SocketException se ? $" SocketError={se.SocketErrorCode}" : "";
        Console.Error.WriteLine(
            $"{DateTimeOffset.UtcNow:O} warn: loadtest bot disconnect botId={botId} selfObjectId={selfObjectId} "
            + $"lastSentOpcode=0x{lastSent:X4} lastRecvOpcode=0x{lastRecv:X4}{socket} "
            + $"exception={ex.GetType().Name}: {ex}");
    }

    static bool ShouldWarn()
    {
        long now = Environment.TickCount64;
        long last = Volatile.Read(ref _lastWarnMs);
        return now - last >= 1000 && Interlocked.CompareExchange(ref _lastWarnMs, now, last) == last;
    }
}