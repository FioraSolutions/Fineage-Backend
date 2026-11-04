namespace Fineage.Net.Host;

public static class NetSettings
{
    /// <summary>Login port. The Net protocol is the only client protocol.</summary>
    public const int DefaultLoginPort = 9106;
    /// <summary>Zone port. The Net protocol is the only client protocol.</summary>
    public const int DefaultZonePort = 9777;

    /// <summary>Legacy MessagePack V2 login, only served when <see cref="LegacyV2Enabled"/> is set.</summary>
    public const int V2LoginPort = 9206;
    /// <summary>Legacy MessagePack V2 zone, only served when <see cref="LegacyV2Enabled"/> is set.</summary>
    public const int V2ZonePort = 9877;

    /// <summary>Net is the only client transport; the flag stays for compatibility with older run scripts.</summary>
    public static bool Enabled => true;

    /// <summary>
    /// Legacy V2 MessagePack listeners. Off by default: the Unity client dropped V2 and only speaks Net.
    /// Set <c>FINEAGE_LEGACY_V2_ENABLE=1</c> to bring the old ports back for a regression comparison.
    /// </summary>
    public static bool LegacyV2Enabled =>
        IsTruthy(Environment.GetEnvironmentVariable("FINEAGE_LEGACY_V2_ENABLE"));

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
