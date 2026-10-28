namespace Fineage.Zone;

public sealed class ZoneServerOptions
{
    public int MaxMessagesPerSecond { get; init; } = ZoneNetSettings.DefaultMaxMessagesPerSecond;

    /// <summary>
    /// When null, <see cref="ZoneAdmin.Enabled"/> is read.
    /// False leaves <see cref="Fineage.Protocol.MsgType.GiveItem"/> rejected.
    /// </summary>
    public bool? AdminCommands { get; init; }
}
