using MessagePack;

namespace Fineage.Protocol.V2;

[MessagePackObject]
public sealed partial class HelloMsg
{
    [Key(0)] public int ProtocolVersion { get; set; } = 1;
    [Key(1)] public string ClientBuild { get; set; } = "";
}

[MessagePackObject]
public sealed partial class HelloAckMsg
{
    [Key(0)] public int ProtocolVersion { get; set; } = 1;
    [Key(1)] public string ServerName { get; set; } = "";
}

[MessagePackObject]
public sealed partial class AuthMsg
{
    [Key(0)] public string Account { get; set; } = "";
    [Key(1)] public string Password { get; set; } = "";
}

[MessagePackObject]
public sealed partial class AuthOkMsg
{
    [Key(0)] public int SessionId { get; set; }
    [Key(1)] public string CharacterName { get; set; } = "";
}

[MessagePackObject]
public sealed partial class EnterWorldMsg
{
    [Key(0)] public int CharacterSlot { get; set; }
}

[MessagePackObject]
public sealed partial class PlayerSpawnedMsg
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

[MessagePackObject]
public sealed partial class MoveToMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int DestX { get; set; }
    [Key(2)] public int DestY { get; set; }
    [Key(3)] public int DestZ { get; set; }
    [Key(4)] public int OriginX { get; set; }
    [Key(5)] public int OriginY { get; set; }
    [Key(6)] public int OriginZ { get; set; }
}

[MessagePackObject]
public sealed partial class ErrorMsg
{
    [Key(0)] public string Code { get; set; } = "";
    [Key(1)] public string Message { get; set; } = "";
}

[MessagePackObject]
public sealed partial class AuthTokenMsg
{
    [Key(0)] public string Account { get; set; } = "";
    [Key(1)] public int PlayKey1 { get; set; }
    [Key(2)] public int PlayKey2 { get; set; }
    [Key(3)] public int LoginKey1 { get; set; }
    [Key(4)] public int LoginKey2 { get; set; }
}

[MessagePackObject]
public sealed partial class SelectServerMsg
{
    [Key(0)] public int ServerId { get; set; } = 1;
}

[MessagePackObject]
public sealed partial class ServerEntryMsg
{
    [Key(0)] public int ServerId { get; set; }
    [Key(1)] public string Host { get; set; } = "127.0.0.1";
    [Key(2)] public int Port { get; set; }
    [Key(3)] public int CurrentPlayers { get; set; }
    [Key(4)] public int MaxPlayers { get; set; }
    [Key(5)] public string Region { get; set; } = "local";
    [Key(6)] public string Name { get; set; } = "";
    [Key(7)] public int Status { get; set; } = 1;
}

[MessagePackObject]
public sealed partial class ServerListMsg
{
    [Key(0)] public ServerEntryMsg[] Servers { get; set; } = [];
}

[MessagePackObject]
public sealed partial class PlayTokenMsg
{
    [Key(0)] public int PlayKey1 { get; set; }
    [Key(1)] public int PlayKey2 { get; set; }
    [Key(2)] public int LoginKey1 { get; set; }
    [Key(3)] public int LoginKey2 { get; set; }
    [Key(4)] public string ZoneHost { get; set; } = "127.0.0.1";
    [Key(5)] public int ZonePort { get; set; }
    [Key(6)] public int ServerId { get; set; }
    [Key(7)] public string Region { get; set; } = "local";
}

[MessagePackObject]
public sealed partial class CreateCharacterMsg
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public int Race { get; set; }
    [Key(2)] public int Sex { get; set; }
    [Key(3)] public int ClassId { get; set; }
    [Key(4)] public int HairStyle { get; set; }
    [Key(5)] public int HairColor { get; set; }
    [Key(6)] public int Face { get; set; }
}

[MessagePackObject]
public sealed partial class CharacterSummaryMsg
{
    [Key(0)] public int Slot { get; set; }
    [Key(1)] public int ObjectId { get; set; }
    [Key(2)] public string Name { get; set; } = "";
    [Key(3)] public int Race { get; set; }
    [Key(4)] public int Sex { get; set; }
    [Key(5)] public int ClassId { get; set; }
    [Key(6)] public int Level { get; set; }
    [Key(7)] public int X { get; set; }
    [Key(8)] public int Y { get; set; }
    [Key(9)] public int Z { get; set; }
}

[MessagePackObject]
public sealed partial class CharacterRosterMsg
{
    [Key(0)] public CharacterSummaryMsg[] Characters { get; set; } = [];
}

[MessagePackObject]
public sealed partial class CreateCharacterResultMsg
{
    [Key(0)] public bool Ok { get; set; }
    [Key(1)] public string Reason { get; set; } = "";
    [Key(2)] public CharacterSummaryMsg Character { get; set; } = new();
}

[MessagePackObject]
public sealed partial class EntityLeftMsg
{
    [Key(0)] public int ObjectId { get; set; }
}
