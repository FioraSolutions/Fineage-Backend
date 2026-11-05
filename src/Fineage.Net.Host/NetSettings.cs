namespace Fineage.Net.Host;

/// <summary>
/// Listeners and framing for the Net protocol, the only client transport.
/// <para>
/// The legacy MessagePack V2 transport (login 9206 / zone 9877) has been removed. Ports 2106 and 7777 that the old
/// playtest clients dialled are gone with it; nothing in this repo can turn V2 back on.
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
