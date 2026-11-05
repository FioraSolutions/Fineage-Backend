namespace Fineage.Net.Host;

/// <summary>
/// Listeners and framing for the Net protocol, the only client transport.
/// <para>
/// The legacy MessagePack V2 transport is deleted from the repo. The 2106 / 7777 listeners that old playtest
/// clients dialled are gone with it; only the Net ports below exist.
/// </para>
/// </summary>
public static class NetSettings
{
    /// <summary>Login port. The Net protocol is the only client protocol.</summary>
    public const int DefaultLoginPort = 9106;
    /// <summary>Zone port. The Net protocol is the only client protocol.</summary>
    public const int DefaultZonePort = 9777;

    public static int LoginPort =>
        ParsePort(Environment.GetEnvironmentVariable("FINEAGE_NET_LOGIN_PORT"), DefaultLoginPort);

    public static int ZonePort =>
        ParsePort(Environment.GetEnvironmentVariable("FINEAGE_NET_ZONE_PORT"), DefaultZonePort);

    public static int InboxCapacity =>
        ParsePositive(Environment.GetEnvironmentVariable("FINEAGE_NET_INBOX"), 256);

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
