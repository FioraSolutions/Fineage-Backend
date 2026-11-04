namespace Fineage.Net.Host;

public static class NetSettings
{
    /// <summary>Forwarded / playtest login port — Net owns these; V2 moves to <see cref="V2LoginPort"/>.</summary>
    public const int DefaultLoginPort = 9106;
    /// <summary>Forwarded / playtest zone port — Net owns these; V2 moves to <see cref="V2ZonePort"/>.</summary>
    public const int DefaultZonePort = 9777;

    /// <summary>Legacy MessagePack V2 login when Net is enabled on the primary ports.</summary>
    public const int V2LoginPort = 9206;
    /// <summary>Legacy MessagePack V2 zone when Net is enabled on the primary ports.</summary>
    public const int V2ZonePort = 9877;

    public static bool Enabled =>
        IsTruthy(Environment.GetEnvironmentVariable("FINEAGE_NET_ENABLE"));

    public static int LoginPort =>
        ParsePort(Environment.GetEnvironmentVariable("FINEAGE_NET_LOGIN_PORT"), DefaultLoginPort);

    public static int ZonePort =>
        ParsePort(Environment.GetEnvironmentVariable("FINEAGE_NET_ZONE_PORT"), DefaultZonePort);

    public static int InboxCapacity =>
        ParsePositive(Environment.GetEnvironmentVariable("FINEAGE_NET_INBOX"), 256);

    static bool IsTruthy(string? text) =>
        text is "1" or "true" or "yes" or "on"
        || string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase);

    static int ParsePort(string? text, int fallback)
    {
        if (int.TryParse(text, out int p) && p is >= 1 and <= 65535)
            return p;
        return fallback;
    }

    static int ParsePositive(string? text, int fallback)
    {
        if (int.TryParse(text, out int n) && n > 0)
            return n;
        return fallback;
    }
}
