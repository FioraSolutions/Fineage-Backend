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
    [Key(10)] public int HairStyle { get; set; }
    [Key(11)] public int HairColor { get; set; }
    [Key(12)] public int Face { get; set; }
    [Key(13)] public int DeleteSecondsLeft { get; set; }
    [Key(14)] public bool IsLastPlayed { get; set; }
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

/// <summary>Same keys as <see cref="Fineage.Protocol.DeleteObjectMsg"/>; zone opcode <see cref="MsgType.DeleteObject"/>.</summary>
[MessagePackObject]
public sealed partial class DeleteObjectMsg
{
    [Key(0)] public int ObjectId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.CharInfoMsg"/>; zone opcode <see cref="MsgType.CharInfo"/>.</summary>
[MessagePackObject]
public sealed partial class CharInfoMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int X { get; set; }
    [Key(3)] public int Y { get; set; }
    [Key(4)] public int Z { get; set; }
    [Key(5)] public int ClassId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.NpcInfoMsg"/>; zone opcode <see cref="MsgType.NpcInfo"/>.</summary>
[MessagePackObject]
public sealed partial class NpcInfoMsg
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

/// <summary>Same keys as <see cref="Fineage.Protocol.SayMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SayMsg
{
    [Key(0)] public string Text { get; set; } = "";
    [Key(1)] public int ChatType { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.CreatureSayMsg"/>.</summary>
[MessagePackObject]
public sealed partial class CreatureSayMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public string Text { get; set; } = "";
    [Key(3)] public int ChatType { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.AbnormalStatusUpdateMsg"/>.</summary>
[MessagePackObject]
public sealed partial class AbnormalStatusUpdateMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public AbnormalEffectMsg[] Effects { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.AbnormalEffectMsg"/>.</summary>
[MessagePackObject]
public sealed partial class AbnormalEffectMsg
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public int Value { get; set; }
    [Key(2)] public int DurationSeconds { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SpawnItemMsg"/>; zone opcode <see cref="MsgType.SpawnItem"/>.</summary>
[MessagePackObject]
public sealed partial class SpawnItemMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int ItemId { get; set; }
    [Key(2)] public int X { get; set; }
    [Key(3)] public int Y { get; set; }
    [Key(4)] public int Z { get; set; }
    [Key(5)] public bool Stackable { get; set; }
    [Key(6)] public int Count { get; set; } = 1;
    [Key(7)] public int Unknown { get; set; }
}
