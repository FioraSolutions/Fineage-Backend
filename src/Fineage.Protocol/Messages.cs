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
    /// <summary>0 male, 1 female.</summary>
    [Key(3)] public int Sex { get; set; }
    /// <summary>0–4 male, 0–6 female. Omitted by older clients (defaults to 0).</summary>
    [Key(4)] public int HairStyle { get; set; }
    /// <summary>0–3.</summary>
    [Key(5)] public int HairColor { get; set; }
    /// <summary>0–2.</summary>
    [Key(6)] public int Face { get; set; }
}

[MessagePackObject]
public sealed class CharacterCreatedMsg
{
    [Key(0)] public int CharacterId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int ClassId { get; set; }
    [Key(3)] public int Race { get; set; }
    [Key(4)] public int Sex { get; set; }
    [Key(5)] public int HairStyle { get; set; }
    [Key(6)] public int HairColor { get; set; }
    [Key(7)] public int Face { get; set; }
    [Key(8)] public int X { get; set; }
    [Key(9)] public int Y { get; set; }
    [Key(10)] public int Z { get; set; }
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
    /// <summary>Index in creation order. Older clients ignore this key.</summary>
    [Key(6)] public int Slot { get; set; }
    [Key(7)] public int HairStyle { get; set; }
    [Key(8)] public int HairColor { get; set; }
    [Key(9)] public int Face { get; set; }
    [Key(10)] public int X { get; set; }
    [Key(11)] public int Y { get; set; }
    [Key(12)] public int Z { get; set; }
    /// <summary>Whole seconds until hard delete, or 0 when the character is not pending.</summary>
    [Key(13)] public int DeleteSecondsLeft { get; set; }
    [Key(14)] public bool IsLastPlayed { get; set; }
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
    /// <summary>Used when <see cref="CharacterId"/> is 0 and <see cref="Name"/> is empty. −1 means the client did not send a slot. Slot 0 is the first character.</summary>
    [Key(2)] public int Slot { get; set; } = -1;
}

/// <summary>Opcode <see cref="MsgType.DeleteCharacter"/> (24). A positive <see cref="CharacterId"/> wins over <see cref="Slot"/>.</summary>
[MessagePackObject]
public sealed class DeleteCharacterMsg
{
    /// <summary>−1 when the client is deleting by id.</summary>
    [Key(0)] public int Slot { get; set; } = -1;
    /// <summary>0 when the client is deleting by slot.</summary>
    [Key(1)] public int CharacterId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RestoreCharacter"/> (25). Same keys as <see cref="DeleteCharacterMsg"/>.</summary>
[MessagePackObject]
public sealed class RestoreCharacterMsg
{
    [Key(0)] public int Slot { get; set; } = -1;
    [Key(1)] public int CharacterId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.CharacterDeleted"/> (1025). A <see cref="CharacterListMsg"/> follows.</summary>
[MessagePackObject]
public sealed class CharacterDeletedMsg
{
    [Key(0)] public int Slot { get; set; }
    [Key(1)] public int CharacterId { get; set; }
    [Key(2)] public bool HardDeleted { get; set; }
}

/// <summary>Opcode <see cref="MsgType.CharacterDeleteFailed"/> (1026).</summary>
[MessagePackObject]
public sealed class CharacterDeleteFailedMsg
{
    [Key(0)] public int Slot { get; set; }
    [Key(1)] public int CharacterId { get; set; }
    [Key(2)] public string Code { get; set; } = "";
    [Key(3)] public string Message { get; set; } = "";
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
/// Opcode <see cref="MsgType.CharInfo"/> (1005). Another player entered knownlist.
/// Keys match tools/zone-server <c>CharInfoMsg</c>.
/// </summary>
[MessagePackObject]
public sealed class CharInfoMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int X { get; set; }
    [Key(3)] public int Y { get; set; }
    [Key(4)] public int Z { get; set; }
    [Key(5)] public int ClassId { get; set; }
    /// <summary>
    /// Item ids in paperdoll order, length 17. Index 0 is Under, 7 is the right hand, 10 is chest, 11 is legs, 16 is Hair2.
    /// Zero means the slot is empty. Older clients ignore this key.
    /// </summary>
    [Key(6)] public int[] Paperdoll { get; set; } = [];
}

/// <summary>One paperdoll slot in <see cref="UserInfoMsg"/>. Object id and item id are 0 when the slot is empty.</summary>
[MessagePackObject]
public sealed class PaperdollSlotMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int ItemId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.UserInfo"/> (1016). Sent to the entering player before <see cref="ItemListMsg"/>.
/// <see cref="Paperdoll"/> has 17 slots in <c>PaperdollSlot</c> order (right hand is index 7).
/// </summary>
[MessagePackObject]
public sealed class UserInfoMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int ClassId { get; set; }
    [Key(3)] public int X { get; set; }
    [Key(4)] public int Y { get; set; }
    [Key(5)] public int Z { get; set; }
    [Key(6)] public float RunSpeed { get; set; } = 120f;
    [Key(7)] public float WalkSpeed { get; set; } = 80f;
    [Key(8)] public bool Running { get; set; } = true;
    [Key(9)] public PaperdollSlotMsg[] Paperdoll { get; set; } = [];
}

/// <summary>One row of <see cref="ItemListMsg"/> or the item fields of <see cref="InventoryUpdateEntryMsg"/>.</summary>
[MessagePackObject]
public sealed class ItemListEntryMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int ItemId { get; set; }
    [Key(2)] public int Count { get; set; }
    /// <summary>Paperdoll index while worn, or -1 in the bag.</summary>
    [Key(3)] public int EquippedSlot { get; set; } = -1;
    /// <summary>none, rhand, lhand, lrhand, chest, legs, fullarmor, and the other body tokens.</summary>
    [Key(4)] public string BodySlot { get; set; } = "none";
    /// <summary>weapon, armor, or etc.</summary>
    [Key(5)] public string Kind { get; set; } = "etc";
    [Key(6)] public string Icon { get; set; } = "";
    /// <summary>sword, dagger, blunt, and the other weapon tokens, or empty when this is not a weapon.</summary>
    [Key(7)] public string WeaponType { get; set; } = "";
    /// <summary>light, heavy, magic, or empty when this is not armor.</summary>
    [Key(8)] public string ArmorType { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.ItemList"/> (1014). Every item in the entering player's inventory.</summary>
[MessagePackObject]
public sealed class ItemListMsg
{
    [Key(0)] public ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>0 add, 1 modify, 2 remove. There is no client equip request yet; the zone defines the row for later.</summary>
public static class InventoryChange
{
    public const int Add = 0;
    public const int Modify = 1;
    public const int Remove = 2;
}

/// <summary>One row of <see cref="InventoryUpdateMsg"/>.</summary>
[MessagePackObject]
public sealed class InventoryUpdateEntryMsg
{
    /// <summary><see cref="InventoryChange.Add"/>, <see cref="InventoryChange.Modify"/>, or <see cref="InventoryChange.Remove"/>.</summary>
    [Key(0)] public int Change { get; set; }
    [Key(1)] public int ObjectId { get; set; }
    [Key(2)] public int ItemId { get; set; }
    [Key(3)] public int Count { get; set; }
    /// <summary>Paperdoll index while worn, or -1 in the bag. Remove rows still carry the last slot or -1.</summary>
    [Key(4)] public int EquippedSlot { get; set; } = -1;
    [Key(5)] public string BodySlot { get; set; } = "none";
    [Key(6)] public string Kind { get; set; } = "etc";
    [Key(7)] public string Icon { get; set; } = "";
    [Key(8)] public string WeaponType { get; set; } = "";
    [Key(9)] public string ArmorType { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.InventoryUpdate"/> (1015).</summary>
[MessagePackObject]
public sealed class InventoryUpdateMsg
{
    [Key(0)] public InventoryUpdateEntryMsg[] Changes { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.DeleteObject"/> (1007). Left knownlist.</summary>
[MessagePackObject]
public sealed class DeleteObjectMsg
{
    [Key(0)] public int ObjectId { get; set; }
}

[MessagePackObject]
public sealed class PingMsg
{
    [Key(0)] public long ClientUnixMs { get; set; }
}

[MessagePackObject]
public sealed class PongMsg
{
    [Key(0)] public long ClientUnixMs { get; set; }
    [Key(1)] public long ServerUnixMs { get; set; }
}

/// <summary>Opcode <see cref="MsgType.Say"/> (8). Client zone chat.</summary>
[MessagePackObject]
public sealed class SayMsg
{
    [Key(0)] public string Text { get; set; } = "";
    [Key(1)] public int ChatType { get; set; }
}

/// <summary>Opcode <see cref="MsgType.CreatureSay"/> (1012). Zone chat fan-out.</summary>
[MessagePackObject]
public sealed class CreatureSayMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public string Text { get; set; } = "";
    [Key(3)] public int ChatType { get; set; }
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

/// <summary>Opcode <see cref="MsgType.SelectTarget"/> (5).</summary>
[MessagePackObject]
public sealed class SelectTargetMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.AttackRequest"/> (6). Uses session target when zero.</summary>
[MessagePackObject]
public sealed class AttackRequestMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.MyTargetSelected"/> (1008).</summary>
[MessagePackObject]
public sealed class MyTargetSelectedMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int ColorHint { get; set; } = InterludeStatus.TargetColorHostile;
}

/// <summary>Opcode <see cref="MsgType.Attack"/> (1009).</summary>
[MessagePackObject]
public sealed class AttackMsg
{
    [Key(0)] public int AttackerObjectId { get; set; }
    [Key(1)] public int TargetObjectId { get; set; }
    [Key(2)] public int Damage { get; set; }
    [Key(3)] public byte Flags { get; set; }
    [Key(4)] public int AttackerX { get; set; }
    [Key(5)] public int AttackerY { get; set; }
    [Key(6)] public int AttackerZ { get; set; }
    [Key(7)] public int TargetX { get; set; }
    [Key(8)] public int TargetY { get; set; }
    [Key(9)] public int TargetZ { get; set; }
}

/// <summary>Opcode <see cref="MsgType.StatusUpdate"/> (1010). Interlude attribute ids in <see cref="AttributeIds"/>.</summary>
[MessagePackObject]
public sealed class StatusUpdateMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int[] AttributeIds { get; set; } = [];
    [Key(2)] public int[] Values { get; set; } = [];
}

/// <summary>Interlude StatusUpdate attribute ids (see client StatusUpdate.AttributeType).</summary>
public static class InterludeStatus
{
    public const int CurHp = 0x09;
    public const int MaxHp = 0x0a;
    public const int TargetColorHostile = 11;
}

/// <summary>Opcode <see cref="MsgType.GrantItemRequest"/> (13). Adds items to the requesting player's bag.</summary>
[MessagePackObject]
public sealed class GrantItemRequestMsg
{
    [Key(0)] public int ItemId { get; set; }
    [Key(1)] public int Count { get; set; } = 1;
}

/// <summary>Opcode <see cref="MsgType.SpawnItem"/> (1018). Ground item in AOI.</summary>
[MessagePackObject]
public sealed class SpawnItemMsg
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

/// <summary>One row of <see cref="AbnormalStatusUpdateMsg"/>. Keys match client <c>EffectHolder</c>.</summary>
[MessagePackObject]
public sealed class AbnormalEffectMsg
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public int Value { get; set; }
    [Key(2)] public int DurationSeconds { get; set; }
}

/// <summary>Opcode <see cref="MsgType.AbnormalStatusUpdate"/> (1017). Full effect list for one object.</summary>
[MessagePackObject]
public sealed class AbnormalStatusUpdateMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public AbnormalEffectMsg[] Effects { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.BuffRequest"/> (12). Playtest toggle until real skills land.</summary>
[MessagePackObject]
public sealed class BuffRequestMsg
{
    /// <summary>0 means the requesting player's object id.</summary>
    [Key(0)] public int TargetObjectId { get; set; }
    [Key(1)] public int EffectId { get; set; }
    /// <summary>When true, removes <see cref="EffectId"/> or clears all effects when zero.</summary>
    [Key(2)] public bool Remove { get; set; }
}
