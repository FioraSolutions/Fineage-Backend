namespace Fineage.Zone;

/// <summary>Production zone TCP limits (env-tunable toward multi-region CCU).</summary>
public static class ZoneNetSettings
{
    public const int DefaultMaxConnections = 2048;
    public const int DefaultOutboundQueue = 32;
    public const int DefaultMaxMessagesPerSecond = 120;

    public static int MaxConnections => ReadInt("FINEAGE_ZONE_MAX_CONNECTIONS", DefaultMaxConnections, min: 1, max: 100_000);
    public static int OutboundQueue => ReadInt("FINEAGE_ZONE_OUTBOUND_QUEUE", DefaultOutboundQueue, min: 8, max: 512);
    public static int MaxMessagesPerSecond => ReadInt("FINEAGE_ZONE_MAX_MSG_PER_SEC", DefaultMaxMessagesPerSecond, min: 1, max: 10_000);

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }
}
