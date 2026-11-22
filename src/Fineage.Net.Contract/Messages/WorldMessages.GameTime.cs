namespace Fineage.Net.Contract;

/// <summary>Mobius <c>ClientSetTime</c> (wire <c>0xEC</c>) via <see cref="NetOpcode.ClientSetTime"/>.</summary>
public sealed class ClientSetTimeNetMsg
{
    public int GameMinutes { get; set; }

    public int ClockSpeed { get; set; }
}