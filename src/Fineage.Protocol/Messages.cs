using MessagePack;

namespace Fineage.Protocol;

// Field keys match Fineage tools/zone-server Wire/Messages.cs for the shared handshake.
// MessagePack-CSharp emits a fixarray when keys are 0..n-1, which is what ZoneServer deserializes.

[MessagePackObject]
public sealed class HelloMsg
{
    [Key(0)] public int ProtocolVersion { get; set; } = 1;
    [Key(1)] public string ClientBuild { get; set; } = "";
}

[MessagePackObject]
public sealed class HelloAckMsg
{
    [Key(0)] public int ProtocolVersion { get; set; } = 1;
    [Key(1)] public string ServerName { get; set; } = "FineageZone";
}

/// <summary>
/// Zone login. After character select, the client sends the account here and the handoff token as <see cref="Password"/>.
/// Today's ZoneServer accepts any password, so this payload is enough for it to open a session.
/// </summary>
[MessagePackObject]
public sealed class AuthMsg
{
    [Key(0)] public string Account { get; set; } = "";
    [Key(1)] public string Password { get; set; } = "";
}

[MessagePackObject]
public sealed class AuthOkMsg
{
    [Key(0)] public int SessionId { get; set; }
    [Key(1)] public string CharacterName { get; set; } = "Test";
}

[MessagePackObject]
public sealed class ErrorMsg
{
    [Key(0)] public string Code { get; set; } = "";
    [Key(1)] public string Message { get; set; } = "";
}

[MessagePackObject]
public sealed class RegisterMsg
{
    [Key(0)] public string Account { get; set; } = "";
    [Key(1)] public string Password { get; set; } = "";
}

[MessagePackObject]
public sealed class RegisterOkMsg
{
    [Key(0)] public string Account { get; set; } = "";
}

[MessagePackObject]
public sealed class LoginOkMsg
{
    [Key(0)] public string Account { get; set; } = "";
}

[MessagePackObject]
public sealed class CreateCharacterMsg
{
    [Key(0)] public string Name { get; set; } = "";
    /// <summary>0 Human Fighter, 10 Human Mystic. Other classes are rejected.</summary>
    [Key(1)] public int ClassId { get; set; }
    /// <summary>0 Human. Other races are rejected.</summary>
    [Key(2)] public int Race { get; set; }
    /// <summary>0 male. Other sexes are rejected.</summary>
    [Key(3)] public int Sex { get; set; }
}

[MessagePackObject]
public sealed class CharacterCreatedMsg
{
    [Key(0)] public int CharacterId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int ClassId { get; set; }
    [Key(3)] public int Race { get; set; }
    [Key(4)] public int Sex { get; set; }
}

[MessagePackObject]
public sealed class CharacterSummaryMsg
{
    [Key(0)] public int CharacterId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int ClassId { get; set; }
    [Key(3)] public int Race { get; set; }
    [Key(4)] public int Sex { get; set; }
    [Key(5)] public int Level { get; set; } = 1;
}

[MessagePackObject]
public sealed class CharacterListMsg
{
    [Key(0)] public CharacterSummaryMsg[] Characters { get; set; } = [];
}

[MessagePackObject]
public sealed class SelectCharacterMsg
{
    [Key(0)] public int CharacterId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}

/// <summary>
/// Character select result. <see cref="Token"/> is the zone session key (<see cref="AuthMsg.Password"/>).
/// </summary>
[MessagePackObject]
public sealed class HandoffMsg
{
    [Key(0)] public string Account { get; set; } = "";
    [Key(1)] public string CharacterName { get; set; } = "";
    [Key(2)] public int CharacterId { get; set; }
    [Key(3)] public int ClassId { get; set; }
    [Key(4)] public string Token { get; set; } = "";
    [Key(5)] public string ZoneHost { get; set; } = "127.0.0.1";
    [Key(6)] public int ZonePort { get; set; } = 9777;
    [Key(7)] public long ExpiresUnixSeconds { get; set; }
}

[MessagePackObject]
public sealed class EnterWorldMsg
{
    [Key(0)] public int CharacterSlot { get; set; }
}

/// <summary>Same keys as tools/zone-server <c>PlayerSpawnedMsg</c>.</summary>
[MessagePackObject]
public sealed class PlayerSpawnedMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int X { get; set; }
    [Key(3)] public int Y { get; set; }
    [Key(4)] public int Z { get; set; }
    [Key(5)] public float RunSpeed { get; set; } = 120f;
    [Key(6)] public float WalkSpeed { get; set; } = 80f;
    [Key(7)] public bool Running { get; set; } = true;
}

/// <summary>Same keys as tools/zone-server <c>MoveToMsg</c>. Also the body of <see cref="MsgType.MoveToAck"/>.</summary>
[MessagePackObject]
public sealed class MoveToMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int DestX { get; set; }
    [Key(2)] public int DestY { get; set; }
    [Key(3)] public int DestZ { get; set; }
    [Key(4)] public int OriginX { get; set; }
    [Key(5)] public int OriginY { get; set; }
    [Key(6)] public int OriginZ { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.NpcInfo"/> (1006). <see cref="NpcId"/> is the template id
/// (Gremlin = 20001), not the Interlude value with 1_000_000 added.
/// </summary>
[MessagePackObject]
public sealed class NpcInfoMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int NpcId { get; set; }
    [Key(2)] public string Name { get; set; } = "";
    [Key(3)] public int X { get; set; }
    [Key(4)] public int Y { get; set; }
    [Key(5)] public int Z { get; set; }
    [Key(6)] public int Heading { get; set; }
    [Key(7)] public bool Attackable { get; set; } = true;
    [Key(8)] public float RunSpeed { get; set; } = 80f;
    [Key(9)] public float WalkSpeed { get; set; } = 40f;
}
