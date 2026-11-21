namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

/// <summary>Opcode <see cref="MsgType.Say"/> (8). Client zone chat.</summary>
public sealed class SayMsg
{
    public string Text { get; set; } = "";
    public int ChatType { get; set; }
}

/// <summary>Opcode <see cref="MsgType.CreatureSay"/> (1012). Zone chat fan-out.</summary>
public sealed class CreatureSayMsg
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public int ChatType { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.SystemMessage"/> (1040).
/// <see cref="Text"/> is the line to show. <see cref="Id"/> and <see cref="Params"/> match <see cref="ChatProtocol"/>.
/// </summary>
public sealed class SystemMessageMsg
{
    public int Id { get; set; }
    public string[] Params { get; set; } = [];
    public string Text { get; set; } = "";

    /// <summary>
    /// Localization key. Empty means <see cref="Text"/> is already the line to send
    /// (player chat, or a string that has not been moved to the catalog yet).
    /// The zone fills <see cref="Text"/> from the player's locale before the packet is encoded.
    /// The key is not on the wire.
    /// </summary>
    public string Key { get; set; } = "";
}
