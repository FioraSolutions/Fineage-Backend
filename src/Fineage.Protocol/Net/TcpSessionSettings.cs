namespace Fineage.Protocol.Net;

public static class TcpSessionSettings
{
    public const int DefaultIdleSeconds = 1800; // 30 min: client may idle (standing, AFK) without sending frames

    public static TimeSpan IdleTimeout
    {
        get
        {
            string? text = Environment.GetEnvironmentVariable("FINEAGE_TCP_IDLE_SECONDS");
            if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int seconds))
                seconds = DefaultIdleSeconds;
            return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 86_400));
        }
    }
}
