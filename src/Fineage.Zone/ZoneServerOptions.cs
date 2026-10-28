namespace Fineage.Zone;

public sealed class ZoneServerOptions
{
    public int MaxMessagesPerSecond { get; init; } = ZoneNetSettings.DefaultMaxMessagesPerSecond;
}
