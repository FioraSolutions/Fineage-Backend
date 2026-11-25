using System.Net.Sockets;

namespace Fineage.Net.Host;

static class NetDisconnectDiagnostics
{
    static long _lastWarnMs;

    public static void Log(string loop, ulong connectionId, string endpoint, Exception ex, int outboundFrames)
    {
        if (!ShouldWarn())
            return;
        string socket = ex is SocketException se ? $" socketError={se.SocketErrorCode}" : "";
        Console.Error.WriteLine(
            $"{DateTimeOffset.UtcNow:O} warn: net disconnect loop={loop} connectionId={connectionId} "
            + $"endpoint={endpoint} outboundFrames={outboundFrames}"
            + $"{socket} exception={ex.GetType().Name}: {ex}");
    }

    static bool ShouldWarn()
    {
        long now = Environment.TickCount64;
        long last = Volatile.Read(ref _lastWarnMs);
        return now - last >= 1000 && Interlocked.CompareExchange(ref _lastWarnMs, now, last) == last;
    }
}