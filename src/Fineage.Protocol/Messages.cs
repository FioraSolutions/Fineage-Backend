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
    /// <summary>
    /// Interlude base class. Human 0/10, Elf 18/25, Dark Elf 31/38, Orc 44/49, Dwarf 53.
    /// A non-base class is rejected.
    /// </summary>
    [Key(1)] public int ClassId { get; set; }
    /// <summary>0 Human, 1 Elf, 2 Dark Elf, 3 Orc, 4 Dwarf. Must match the class.</summary>
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
    /// <summary>CharSelect vitals and progress (Mobius CharSelectionInfo). Older clients ignore these keys.</summary>
    [Key(15)] public int CurHp { get; set; }
    [Key(16)] public int MaxHp { get; set; }
    [Key(17)] public int CurMp { get; set; }
    [Key(18)] public int MaxMp { get; set; }
    [Key(19)] public int CurCp { get; set; }
    [Key(20)] public int MaxCp { get; set; }
    [Key(21)] public long Exp { get; set; }
    [Key(22)] public int Sp { get; set; }
    [Key(23)] public int Karma { get; set; }
    /// <summary>Right-hand (or two-hand) item id, 0 when unarmed.</summary>
    [Key(24)] public int WeaponItemId { get; set; }
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
    /// <summary>0 when the character is not in a clan. Older clients ignore this key.</summary>
    [Key(7)] public int ClanId { get; set; }
    /// <summary>Pledge crest id. Always 0 until crests exist.</summary>
    [Key(8)] public int ClanCrestId { get; set; }
    /// <summary>
    /// Enchant level of each paperdoll slot, same order and length as <see cref="Paperdoll"/>.
    /// Zero when the slot is empty or the item is +0. Older clients ignore this key.
    /// </summary>
    [Key(9)] public int[] Enchant { get; set; } = [];
}

/// <summary>One paperdoll slot in <see cref="UserInfoMsg"/>. Object id and item id are 0 when the slot is empty.</summary>
[MessagePackObject]
public sealed class PaperdollSlotMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int ItemId { get; set; }
    /// <summary>Enchant level while this slot is occupied. 0 when empty or +0. Older clients ignore this key.</summary>
    [Key(2)] public int EnchantLevel { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.UserInfo"/> (1016). Sent to the entering player before <see cref="ItemListMsg"/>.
/// <see cref="Paperdoll"/> has 17 slots in <c>PaperdollSlot</c> order (right hand is index 7).
/// Keys 10–12 are the expertise grade penalty. They are 0 when the penalty is off or the gear is within grade.
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
    /// <summary>Weapon expertise penalty, 0–4. Zero when every equipped weapon is within Expertise. Older clients ignore this key.</summary>
    [Key(10)] public int WeaponGradePenalty { get; set; }
    /// <summary>Armor expertise penalty, 0–4. Arrows are not counted. Older clients ignore this key.</summary>
    [Key(11)] public int ArmorGradePenalty { get; set; }
    /// <summary>
    /// Grade-penalty level, the greater of <see cref="WeaponGradePenalty"/> and <see cref="ArmorGradePenalty"/>, or 0.
    /// Mobius EtcStatusUpdate sends 1 when this is non-zero. Stat multipliers are not applied.
    /// </summary>
    [Key(12)] public int GradePenalty { get; set; }
    /// <summary>0 when the character is not in a clan. Older clients ignore this key.</summary>
    [Key(13)] public int ClanId { get; set; }
    /// <summary>Pledge crest id. Always 0 until crests exist.</summary>
    [Key(14)] public int ClanCrestId { get; set; }
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
    /// <summary>Enchant level. 0 when the piece is not enchanted. Older clients ignore this key.</summary>
    [Key(9)] public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ItemList"/> (1014). Every item in the entering player's inventory.</summary>
[MessagePackObject]
public sealed class ItemListMsg
{
    [Key(0)] public ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>0 add, 1 modify, 2 remove. Equip and unequip send modify rows. Give and grant send add rows.</summary>
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
    /// <summary>Enchant level. Remove rows still carry the level the piece had. Older clients ignore this key.</summary>
    [Key(10)] public int EnchantLevel { get; set; }
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
/// Opcode <see cref="MsgType.SystemMessage"/> (1040).
/// <see cref="Text"/> is the line to show. <see cref="Id"/> and <see cref="Params"/> match <see cref="ChatProtocol"/>.
/// </summary>
[MessagePackObject]
public sealed class SystemMessageMsg
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string[] Params { get; set; } = [];
    [Key(2)] public string Text { get; set; } = "";
}

/// <summary>
/// Opcode <see cref="MsgType.NpcInfo"/> (1006). <see cref="NpcId"/> is the template id
/// (Gremlin = 20001), not the Interlude value with 1_000_000 added.
/// Keys 0–9 are unchanged. Key 10 <see cref="Title"/> and key 11 <see cref="NpcType"/>
/// were appended for town NPCs. Do not renumber.
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
    /// <summary>Mobius title, such as Trader or Gatekeeper. Empty when the template has none.</summary>
    [Key(10)] public string Title { get; set; } = "";
    /// <summary>Mobius instance type, such as Merchant, Guard, Teleporter, or Monster.</summary>
    [Key(11)] public string NpcType { get; set; } = "";
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
    public const int CurMp = 0x0b;
    public const int MaxMp = 0x0c;
    public const int TargetColorHostile = 11;

    /// <summary>Interlude MyTargetSelected color: attacker level minus target level (L2J/Mobius).</summary>
    public static int TargetColor(int attackerLevel, int targetLevel) => attackerLevel - targetLevel;
}

/// <summary>Opcode <see cref="MsgType.GrantItemRequest"/> (13). Adds items to the requesting player's bag.</summary>
[MessagePackObject]
public sealed class GrantItemRequestMsg
{
    [Key(0)] public int ItemId { get; set; }
    [Key(1)] public int Count { get; set; } = 1;
}

/// <summary>Opcode <see cref="MsgType.PickupRequest"/> (14). Picks up a visible ground item.</summary>
[MessagePackObject]
public sealed class PickupRequestMsg
{
    [Key(0)] public int ObjectId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.UseItem"/> (15).
/// An equippable item in the bag is equipped. The same object id, already worn, is unequipped.
/// </summary>
[MessagePackObject]
public sealed class UseItemMsg
{
    /// <summary>Inventory object id. Not an item template id.</summary>
    [Key(0)] public int ObjectId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.UnequipItem"/> (16).
/// <see cref="PaperdollSlot"/> is a paperdoll index (0 Under … 16 Hair2), not a body-slot token.
/// </summary>
[MessagePackObject]
public sealed class UnequipItemMsg
{
    [Key(0)] public int PaperdollSlot { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.GiveItem"/> (17). Admin/dev bag grant.
/// The zone accepts it only when <c>FINEAGE_ZONE_ADMIN</c> is <c>1</c>, <c>true</c>, or <c>yes</c>.
/// </summary>
[MessagePackObject]
public sealed class GiveItemMsg
{
    [Key(0)] public int ItemId { get; set; }
    [Key(1)] public int Count { get; set; } = 1;
}

/// <summary>Opcode <see cref="MsgType.AdminSpoilTarget"/> (30). Admin spoil hook before skill 254 exists.</summary>
[MessagePackObject]
public sealed class AdminSpoilTargetMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.AdminSweepCorpse"/> (31). Admin sweeper hook before skill 42 exists.</summary>
[MessagePackObject]
public sealed class AdminSweepCorpseMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.EnchantRequest"/> (39). Selects the scroll and opens the window.</summary>
[MessagePackObject]
public sealed class EnchantRequestMsg
{
    /// <summary>Inventory object id of the scroll. Not an item template id.</summary>
    [Key(0)] public int ObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.EnchantConfirm"/> (40). The scroll is the one selected by the request.</summary>
[MessagePackObject]
public sealed class EnchantConfirmMsg
{
    /// <summary>Inventory object id of the weapon or armor to enchant.</summary>
    [Key(0)] public int ObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.EnchantCancel"/> (41). An empty map is enough.</summary>
[MessagePackObject]
public sealed class EnchantCancelMsg
{
}

/// <summary>
/// Opcode <see cref="MsgType.EnchantResult"/> (1060).
/// <see cref="Result"/> is <see cref="EnchantProtocol.ResultCancel"/>, <see cref="EnchantProtocol.ResultSuccess"/>,
/// <see cref="EnchantProtocol.ResultKeep"/>, <see cref="EnchantProtocol.ResultReset"/>, or <see cref="EnchantProtocol.ResultCrystallize"/>.
/// On success <see cref="EnchantLevel"/> is the level after the increase. On reset it is 0.
/// On keep and on a success that does not increase (base chance 0) it is the unchanged level.
/// </summary>
[MessagePackObject]
public sealed class EnchantResultMsg
{
    [Key(0)] public int Result { get; set; }
    [Key(1)] public int EnchantLevel { get; set; }
    /// <summary>Target object id. 0 on cancel.</summary>
    [Key(2)] public int ObjectId { get; set; }
    /// <summary>Crystal item id gained from a normal failure. 0 when none are gained.</summary>
    [Key(3)] public int CrystalItemId { get; set; }
    [Key(4)] public int CrystalCount { get; set; }
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

/// <summary>Opcode <see cref="MsgType.PartyInviteRequest"/> (18).</summary>
[MessagePackObject]
public sealed class PartyInviteRequestMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
    /// <summary>Loot mode for a new party; ignored when inviter is already in a party.</summary>
    [Key(1)] public int ItemDistribution { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PartyAnswerJoinRequest"/> (19). Response 1 accept, 0 decline.</summary>
[MessagePackObject]
public sealed class PartyAnswerJoinRequestMsg
{
    [Key(0)] public int RequestorObjectId { get; set; }
    [Key(1)] public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PartyAskJoin"/> (1027).</summary>
[MessagePackObject]
public sealed class PartyAskJoinMsg
{
    [Key(0)] public int RequestorObjectId { get; set; }
    [Key(1)] public string RequestorName { get; set; } = "";
    [Key(2)] public int ItemDistribution { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PartyJoinResult"/> (1028). 1 accepted, 0 declined.</summary>
[MessagePackObject]
public sealed class PartyJoinResultMsg
{
    [Key(0)] public int Response { get; set; }
}

/// <summary>One row of <see cref="PartyMemberListMsg"/>.</summary>
[MessagePackObject]
public sealed class PartyMemberEntryMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int ClassId { get; set; }
    [Key(3)] public int Level { get; set; }
    [Key(4)] public int CurHp { get; set; }
    [Key(5)] public int MaxHp { get; set; }
    [Key(6)] public int CurMp { get; set; }
    [Key(7)] public int MaxMp { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PartyMemberList"/> (1029).</summary>
[MessagePackObject]
public sealed class PartyMemberListMsg
{
    [Key(0)] public int LeaderObjectId { get; set; }
    [Key(1)] public int PartyId { get; set; }
    [Key(2)] public PartyMemberEntryMsg[] Members { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.PartyLeaveRequest"/> (26). Empty body.</summary>
[MessagePackObject]
public sealed class PartyLeaveRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.PartyKickRequest"/> (27).</summary>
[MessagePackObject]
public sealed class PartyKickRequestMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeRequest"/> (28).</summary>
[MessagePackObject]
public sealed class TradeRequestMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeAnswerRequest"/> (29).</summary>
[MessagePackObject]
public sealed class TradeAnswerRequestMsg
{
    [Key(0)] public int RequestorObjectId { get; set; }
    [Key(1)] public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeAddItemRequest"/> (54).</summary>
[MessagePackObject]
public sealed class TradeAddItemRequestMsg
{
    [Key(0)] public int InventoryObjectId { get; set; }
    [Key(1)] public long Count { get; set; } = 1;
}

/// <summary>Opcode <see cref="MsgType.TradeDoneRequest"/> (55). Response 1 confirm, 0 cancel.</summary>
[MessagePackObject]
public sealed class TradeDoneRequestMsg
{
    [Key(0)] public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeAsk"/> (1073).</summary>
[MessagePackObject]
public sealed class TradeAskMsg
{
    [Key(0)] public int RequestorObjectId { get; set; }
    [Key(1)] public string RequestorName { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.TradeRequestResult"/> (1074).</summary>
[MessagePackObject]
public sealed class TradeRequestResultMsg
{
    [Key(0)] public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeStart"/> (1075).</summary>
[MessagePackObject]
public sealed class TradeStartMsg
{
    [Key(0)] public int PartnerObjectId { get; set; }
    [Key(1)] public string PartnerName { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.TradeOfferUpdate"/> (1076).</summary>
[MessagePackObject]
public sealed class TradeOfferUpdateMsg
{
    [Key(0)] public int IsOwn { get; set; }
    [Key(1)] public int InventoryObjectId { get; set; }
    [Key(2)] public int ItemId { get; set; }
    [Key(3)] public long Count { get; set; }
    [Key(4)] public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeConfirmState"/> (1077).</summary>
[MessagePackObject]
public sealed class TradeConfirmStateMsg
{
    [Key(0)] public int SelfConfirmed { get; set; }
    [Key(1)] public int PartnerConfirmed { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeFinished"/> (1078).</summary>
[MessagePackObject]
public sealed class TradeFinishedMsg
{
    [Key(0)] public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestQuestList"/> (42). Empty body.</summary>
[MessagePackObject]
public sealed class RequestQuestListMsg
{
}

/// <summary>Opcode <see cref="MsgType.RequestQuestAbort"/> (43).</summary>
[MessagePackObject]
public sealed class RequestQuestAbortMsg
{
    [Key(0)] public int QuestId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.NpcTalk"/> (44).
/// First-talk hook, or the NPC's HTML page when no quest step matches.
/// </summary>
[MessagePackObject]
public sealed class NpcTalkMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.BypassRequest"/> (95). Mobius <c>RequestBypassToServer</c>.
/// <see cref="Command"/> may be <c>npc_&lt;objectId&gt;_Chat 1</c> or the command alone.
/// </summary>
[MessagePackObject]
public sealed class BypassRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public string Command { get; set; } = "";
}

/// <summary>
/// Opcode <see cref="MsgType.QuestEvent"/> (45).
/// Empty <see cref="Event"/> is the talk hook. Any other token is the adv-event hook.
/// </summary>
[MessagePackObject]
public sealed class QuestEventMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int QuestId { get; set; }
    [Key(2)] public string Event { get; set; } = "";
}

/// <summary>One started quest in <see cref="QuestListMsg"/>.</summary>
[MessagePackObject]
public sealed class QuestJournalEntryMsg
{
    [Key(0)] public int QuestId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    /// <summary>1 while started. Completed one-time quests are not in this list.</summary>
    [Key(2)] public int State { get; set; }
    [Key(3)] public int Cond { get; set; }
    /// <summary>Raw <c>__compltdStateFlags</c>. Zero when the journal uses <see cref="Cond"/>.</summary>
    [Key(4)] public int ProgressFlags { get; set; }
    /// <summary>Flags when non-zero, otherwise <see cref="Cond"/>.</summary>
    [Key(5)] public int JournalWord { get; set; }
    [Key(6)] public bool Repeatable { get; set; }
}

/// <summary>Opcode <see cref="MsgType.QuestList"/> (1061).</summary>
[MessagePackObject]
public sealed class QuestListMsg
{
    [Key(0)] public QuestJournalEntryMsg[] Active { get; set; } = [];
    /// <summary>128 bytes. Bit <c>id % 8</c> of byte <c>id / 8</c> for a completed one-time quest whose id is 0..1023.</summary>
    [Key(1)] public byte[] CompletedMask { get; set; } = [];
    [Key(2)] public int[] CompletedQuestIds { get; set; } = [];
    [Key(3)] public int MaxActive { get; set; }
}

/// <summary>Opcode <see cref="MsgType.QuestDialog"/> (1062).</summary>
[MessagePackObject]
public sealed class QuestDialogMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int NpcId { get; set; }
    [Key(2)] public int QuestId { get; set; }
    /// <summary><c>first_talk</c>, <c>talk</c>, <c>event</c>, <c>kill</c>, or <c>default</c>.</summary>
    [Key(3)] public string Hook { get; set; } = "";
    [Key(4)] public string Event { get; set; } = "";
    [Key(5)] public string Html { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.QuestSound"/> (1063).</summary>
[MessagePackObject]
public sealed class QuestSoundMsg
{
    [Key(0)] public string Name { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.SkillUseRequest"/> (50).</summary>
[MessagePackObject]
public sealed class SkillUseRequestMsg
{
    [Key(0)] public int SkillId { get; set; }
    /// <summary>0 uses the selected target or self.</summary>
    [Key(1)] public int TargetObjectId { get; set; }
    [Key(2)] public bool CtrlPressed { get; set; }
}

/// <summary>Opcode <see cref="MsgType.CharacterActionRequest"/> (51). Mobius <c>RequestActionUse</c> action id.</summary>
[MessagePackObject]
public sealed class CharacterActionRequestMsg
{
    [Key(0)] public int ActionId { get; set; }
    [Key(1)] public bool CtrlPressed { get; set; }
    [Key(2)] public bool ShiftPressed { get; set; }
    /// <summary>Optional target for assist-style actions; unused in MVP.</summary>
    [Key(3)] public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ChangeWaitType"/> (1067). Interlude wait type on <see cref="ActionProtocol.WaitSitting"/> / standing.</summary>
[MessagePackObject]
public sealed class ChangeWaitTypeMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int WaitType { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SocialAction"/> (1068).</summary>
[MessagePackObject]
public sealed class SocialActionMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int ActionId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ChangeMoveType"/> (1069).</summary>
[MessagePackObject]
public sealed class ChangeMoveTypeMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public bool Running { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ReviveRequest"/> (52). Respawn after death.</summary>
[MessagePackObject]
public sealed class ReviveRequestMsg
{
    /// <summary><see cref="DeathProtocol.ReviveToVillage"/> is the MVP restart.</summary>
    [Key(0)] public int Action { get; set; }
}

/// <summary>Admin/test: kill the requesting player. Empty body.</summary>
[MessagePackObject]
public sealed class AdminKillPlayerMsg;

/// <summary>Opcode <see cref="MsgType.Die"/> (1074). Creature died; corpse stays in world for players.</summary>
[MessagePackObject]
public sealed class DieMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int KillerObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.Revive"/> (1075).</summary>
[MessagePackObject]
public sealed class ReviveMsg
{
    [Key(0)] public int ObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TeleportToLocation"/> (1076). Instant move (respawn, dungeon, …).</summary>
[MessagePackObject]
public sealed class TeleportToLocationMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int X { get; set; }
    [Key(2)] public int Y { get; set; }
    [Key(3)] public int Z { get; set; }
    [Key(4)] public int Heading { get; set; }
}

/// <summary>One row of <see cref="SkillListMsg"/>.</summary>
[MessagePackObject]
public sealed class SkillListEntryMsg
{
    [Key(0)] public int SkillId { get; set; }
    [Key(1)] public int Level { get; set; }
    /// <summary>Remaining reuse in milliseconds. Zero when ready.</summary>
    [Key(2)] public int ReuseDelayMs { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SkillList"/> (1065). Sent after <see cref="ItemListMsg"/> on enter.</summary>
[MessagePackObject]
public sealed class SkillListMsg
{
    [Key(0)] public SkillListEntryMsg[] Skills { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.MagicSkillUse"/> (1066).</summary>
[MessagePackObject]
public sealed class MagicSkillUseMsg
{
    [Key(0)] public int CasterObjectId { get; set; }
    [Key(1)] public int TargetObjectId { get; set; }
    [Key(2)] public int SkillId { get; set; }
    [Key(3)] public int SkillLevel { get; set; }
    [Key(4)] public int HitTimeMs { get; set; }
    [Key(5)] public int ReuseDelayMs { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.NpcHtml"/> (1094). Mobius <c>NpcHtmlMessage</c>.
/// Quest steps stay on <see cref="QuestDialogMsg"/>. <see cref="ItemId"/> is 0 until item HTML exists.
/// </summary>
[MessagePackObject]
public sealed class NpcHtmlMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int NpcId { get; set; }
    [Key(2)] public string Html { get; set; } = "";
    [Key(3)] public int ItemId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ClanCreateRequest"/> (32).</summary>
[MessagePackObject]
public sealed class ClanCreateRequestMsg
{
    [Key(0)] public string Name { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.ClanInviteRequest"/> (33). Pledge type must be 0 (main).</summary>
[MessagePackObject]
public sealed class ClanInviteRequestMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
    [Key(1)] public int PledgeType { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.ClanAnswerJoinRequest"/> (34).
/// Response 0 declines. Any other value accepts (Mobius <c>RequestAnswerJoinPledge</c>).
/// </summary>
[MessagePackObject]
public sealed class ClanAnswerJoinRequestMsg
{
    [Key(0)] public int RequestorObjectId { get; set; }
    [Key(1)] public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ClanLeaveRequest"/> (35). Empty body.</summary>
[MessagePackObject]
public sealed class ClanLeaveRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.ClanKickRequest"/> (36).</summary>
[MessagePackObject]
public sealed class ClanKickRequestMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ClanDissolveRequest"/> (37). Empty body.</summary>
[MessagePackObject]
public sealed class ClanDissolveRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.ClanCancelDissolveRequest"/> (38). Empty body.</summary>
[MessagePackObject]
public sealed class ClanCancelDissolveRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.ClanAskJoin"/> (1050).</summary>
[MessagePackObject]
public sealed class ClanAskJoinMsg
{
    [Key(0)] public int RequestorObjectId { get; set; }
    [Key(1)] public string RequestorName { get; set; } = "";
    [Key(2)] public int ClanId { get; set; }
    [Key(3)] public string ClanName { get; set; } = "";
    [Key(4)] public int PledgeType { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PledgeStatusChanged"/> (1051). Clan id 0 clears the pledge.</summary>
[MessagePackObject]
public sealed class PledgeStatusChangedMsg
{
    [Key(0)] public int ClanId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int Level { get; set; }
    [Key(3)] public int LeaderObjectId { get; set; }
    [Key(4)] public int CrestId { get; set; }
    [Key(5)] public int MemberCount { get; set; }
    [Key(6)] public long DissolvingExpiryUnixMs { get; set; }
    [Key(7)] public long CharPenaltyExpiryUnixMs { get; set; }
}

/// <summary>One row of <see cref="PledgeMemberListMsg"/>.</summary>
[MessagePackObject]
public sealed class PledgeMemberEntryMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int ClassId { get; set; }
    [Key(3)] public int Level { get; set; }
    [Key(4)] public bool Online { get; set; }
    [Key(5)] public int PledgeType { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PledgeMemberList"/> (1052).</summary>
[MessagePackObject]
public sealed class PledgeMemberListMsg
{
    [Key(0)] public int ClanId { get; set; }
    [Key(1)] public int LeaderObjectId { get; set; }
    [Key(2)] public PledgeMemberEntryMsg[] Members { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.ClanJoinResult"/> (1053). 0 declined, otherwise the accepted response.</summary>
[MessagePackObject]
public sealed class ClanJoinResultMsg
{
    [Key(0)] public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SevenSignsStatusRequest"/> (90). Page is ignored; the reply is the full record.</summary>
[MessagePackObject]
public sealed class SevenSignsStatusRequestMsg
{
    [Key(0)] public int Page { get; set; } = 1;
}

/// <summary>
/// Opcode <see cref="MsgType.SevenSignsActionRequest"/> (91).
/// <see cref="Action"/> is <see cref="SevenSignsProtocol.ActionJoin"/> or <see cref="SevenSignsProtocol.ActionContribute"/>.
/// </summary>
[MessagePackObject]
public sealed class SevenSignsActionRequestMsg
{
    [Key(0)] public int Action { get; set; }
    /// <summary>1 Dusk, 2 Dawn. Used by join.</summary>
    [Key(1)] public int Cabal { get; set; }
    /// <summary>1 Avarice, 2 Gnosis, 3 Strife. Used by join.</summary>
    [Key(2)] public int Seal { get; set; }
    [Key(3)] public int BlueStones { get; set; }
    [Key(4)] public int GreenStones { get; set; }
    [Key(5)] public int RedStones { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SevenSignsStatus"/> (1090). One record instead of the four Interlude SSQStatus pages.</summary>
[MessagePackObject]
public sealed class SevenSignsStatusMsg
{
    [Key(0)] public int Period { get; set; }
    [Key(1)] public int Cycle { get; set; }
    [Key(2)] public int PlayerCabal { get; set; }
    [Key(3)] public int PlayerSeal { get; set; }
    [Key(4)] public int PlayerStoneCount { get; set; }
    [Key(5)] public int PlayerAncientAdena { get; set; }
    [Key(6)] public int PlayerContribution { get; set; }
    [Key(7)] public int DawnScore { get; set; }
    [Key(8)] public int DuskScore { get; set; }
    [Key(9)] public int DawnFestivalScore { get; set; }
    [Key(10)] public int DuskFestivalScore { get; set; }
    [Key(11)] public long DawnStonePoints { get; set; }
    [Key(12)] public long DuskStonePoints { get; set; }
    [Key(13)] public int AvariceOwner { get; set; }
    [Key(14)] public int GnosisOwner { get; set; }
    [Key(15)] public int StrifeOwner { get; set; }
    [Key(16)] public int DawnMembers { get; set; }
    [Key(17)] public int DuskMembers { get; set; }
    [Key(18)] public int WinningCabal { get; set; }
    [Key(19)] public long NextChangeUnixMs { get; set; }
    [Key(20)] public int SkyState { get; set; }
    [Key(21)] public int DawnAvarice { get; set; }
    [Key(22)] public int DawnGnosis { get; set; }
    [Key(23)] public int DawnStrife { get; set; }
    [Key(24)] public int DuskAvarice { get; set; }
    [Key(25)] public int DuskGnosis { get; set; }
    [Key(26)] public int DuskStrife { get; set; }
    [Key(27)] public int PreviousWinner { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SevenSignsSky"/> (1091). State 0 normal, 1 dusk, 2 dawn.</summary>
[MessagePackObject]
public sealed class SevenSignsSkyMsg
{
    [Key(0)] public int State { get; set; }
    [Key(1)] public int Period { get; set; }
    [Key(2)] public int WinningCabal { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.DungeonEnterRequest"/> (92).
/// <see cref="Action"/> is <see cref="DungeonProtocol.ActionEnter"/> or <see cref="DungeonProtocol.ActionExit"/>.
/// </summary>
[MessagePackObject]
public sealed class DungeonEnterRequestMsg
{
    [Key(0)] public int DungeonId { get; set; }
    [Key(1)] public int Action { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.DungeonEntered"/> (1092).
/// <see cref="Action"/> is enter, exit, or the period-change kick.
/// <see cref="Inside"/> is true only after a successful entrance teleport.
/// </summary>
[MessagePackObject]
public sealed class DungeonEnteredMsg
{
    [Key(0)] public int DungeonId { get; set; }
    [Key(1)] public int X { get; set; }
    [Key(2)] public int Y { get; set; }
    [Key(3)] public int Z { get; set; }
    [Key(4)] public bool Inside { get; set; }
    [Key(5)] public int Action { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.SiegeRegisterRequest"/> (46).
/// <see cref="Attacker"/> 1 registers as attacker, 0 as defender.
/// <see cref="Joining"/> 1 registers, 0 withdraws. Mobius <c>RequestJoinSiege</c>.
/// </summary>
[MessagePackObject]
public sealed class SiegeRegisterRequestMsg
{
    [Key(0)] public int CastleId { get; set; }
    [Key(1)] public int Attacker { get; set; }
    [Key(2)] public int Joining { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.SiegeDefenderAnswerRequest"/> (47).
/// <see cref="Approved"/> 1 accepts a waiting defender. Any other value refuses. Mobius <c>RequestConfirmSiegeWaitingList</c>.
/// </summary>
[MessagePackObject]
public sealed class SiegeDefenderAnswerRequestMsg
{
    [Key(0)] public int CastleId { get; set; }
    [Key(1)] public int ClanId { get; set; }
    [Key(2)] public int Approved { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SiegeListRequest"/> (48).</summary>
[MessagePackObject]
public sealed class SiegeListRequestMsg
{
    [Key(0)] public int CastleId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.AdminCaptureCastle"/> (49). Off unless <c>FINEAGE_ZONE_ADMIN</c> is set.
/// Calls <c>CaptureCastle(clanId)</c>. Combat does not send this.
/// </summary>
[MessagePackObject]
public sealed class AdminCaptureCastleMsg
{
    [Key(0)] public int CastleId { get; set; }
    [Key(1)] public int ClanId { get; set; }
}

/// <summary>One clan on <see cref="SiegeInfoMsg"/>. Side is owner -1, defender 0, attacker 1, or pending 2.</summary>
[MessagePackObject]
public sealed class SiegeClanMsg
{
    [Key(0)] public int ClanId { get; set; }
    [Key(1)] public int Side { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SiegeInfo"/> (1064). The castle and its current registrations.</summary>
[MessagePackObject]
public sealed class SiegeInfoMsg
{
    [Key(0)] public int CastleId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int OwnerClanId { get; set; }
    [Key(3)] public int TaxPercent { get; set; }
    [Key(4)] public long SiegeUnixMs { get; set; }
    [Key(5)] public long RegistrationDeadlineUnixMs { get; set; }
    [Key(6)] public bool RegistrationClosed { get; set; }
    [Key(7)] public bool InProgress { get; set; }
    [Key(8)] public SiegeClanMsg[] Clans { get; set; } = [];
    /// <summary>Unix milliseconds when the siege ends. 0 when it is not in progress.</summary>
    [Key(9)] public long EndsUnixMs { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.BossStatusRequest"/> (93).
/// <see cref="NpcId"/> 0 asks for every scheduled boss.
/// </summary>
[MessagePackObject]
public sealed class BossStatusRequestMsg
{
    [Key(0)] public int NpcId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.AdminBossSchedule"/> (94).
/// <see cref="Action"/> is <see cref="BossProtocol.ActionMarkDead"/> or <see cref="BossProtocol.ActionForceSpawn"/>.
/// </summary>
[MessagePackObject]
public sealed class AdminBossScheduleMsg
{
    [Key(0)] public int NpcId { get; set; }
    [Key(1)] public int Action { get; set; }
}

/// <summary>Opcode <see cref="MsgType.BossStatus"/> (1093).</summary>
[MessagePackObject]
public sealed class BossStatusMsg
{
    [Key(0)] public BossStatusEntryMsg[] Bosses { get; set; } = [];
}

/// <summary>One row of <see cref="BossStatusMsg"/>. Keys are 0..10.</summary>
[MessagePackObject]
public sealed class BossStatusEntryMsg
{
    [Key(0)] public int NpcId { get; set; }
    [Key(1)] public int ObjectId { get; set; }
    /// <summary><see cref="BossProtocol.KindRaid"/> or <see cref="BossProtocol.KindGrand"/>.</summary>
    [Key(2)] public int Kind { get; set; }
    /// <summary>Alive 0, waiting 1, fighting 2, dead 3. See <see cref="BossProtocol"/>.</summary>
    [Key(3)] public int Status { get; set; }
    [Key(4)] public long RespawnUnixMs { get; set; }
    [Key(5)] public int CurrentHp { get; set; }
    [Key(6)] public int MaxHp { get; set; }
    [Key(7)] public int CurrentMp { get; set; }
    [Key(8)] public int MaxMp { get; set; }
    [Key(9)] public bool Enabled { get; set; }
    [Key(10)] public bool Spawned { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadStatusRequest"/> (60). Empty body.</summary>
[MessagePackObject]
public sealed class OlympiadStatusRequestMsg
{
}

/// <summary>
/// Opcode <see cref="MsgType.OlympiadRegisterRequest"/> (61).
/// <see cref="Classed"/> 1 is the class queue. 0 is the non-classed queue.
/// </summary>
[MessagePackObject]
public sealed class OlympiadRegisterRequestMsg
{
    [Key(0)] public int Classed { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadUnregisterRequest"/> (62). Empty body.</summary>
[MessagePackObject]
public sealed class OlympiadUnregisterRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.OlympiadLeaveRequest"/> (63). Empty body. Leaving a match is a loss.</summary>
[MessagePackObject]
public sealed class OlympiadLeaveRequestMsg
{
}

/// <summary>
/// Opcode <see cref="MsgType.AdminReportOlympiadResult"/> (64). Off unless <c>FINEAGE_ZONE_ADMIN</c> is set.
/// <see cref="WinnerObjectId"/> 0 is a draw. Combat calls <c>ReportOlympiadResult</c> with the same pair.
/// </summary>
[MessagePackObject]
public sealed class AdminReportOlympiadResultMsg
{
    [Key(0)] public int MatchId { get; set; }
    [Key(1)] public int WinnerObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadStatus"/> (1070).</summary>
[MessagePackObject]
public sealed class OlympiadStatusMsg
{
    [Key(0)] public int Period { get; set; }
    [Key(1)] public int Cycle { get; set; }
    [Key(2)] public bool InCompetition { get; set; }
    [Key(3)] public long CompetitionStartUnixMs { get; set; }
    [Key(4)] public long CompetitionEndUnixMs { get; set; }
    [Key(5)] public long OlympiadEndUnixMs { get; set; }
    [Key(6)] public long ValidationEndUnixMs { get; set; }
    [Key(7)] public long NextWeeklyUnixMs { get; set; }
    [Key(8)] public bool RegistrationOpen { get; set; }
    [Key(9)] public bool Noble { get; set; }
    [Key(10)] public int Points { get; set; }
    [Key(11)] public int CompetitionsDone { get; set; }
    [Key(12)] public int Won { get; set; }
    [Key(13)] public int Lost { get; set; }
    [Key(14)] public int Drawn { get; set; }
    /// <summary>0 none, 1 classed queue, 2 non-classed queue.</summary>
    [Key(15)] public int Queue { get; set; }
    [Key(16)] public int MatchId { get; set; }
    /// <summary>Stadium index, or -1 when this character is not in a match.</summary>
    [Key(17)] public int StadiumId { get; set; }
    [Key(18)] public int ClassId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadMatch"/> (1071). Sent to both participants.</summary>
[MessagePackObject]
public sealed class OlympiadMatchMsg
{
    [Key(0)] public int MatchId { get; set; }
    [Key(1)] public int StadiumId { get; set; }
    [Key(2)] public int ZoneId { get; set; }
    [Key(3)] public bool Classed { get; set; }
    /// <summary>0 countdown, 1 pause, 2 battle countdown, 3 fighting, 4 returning.</summary>
    [Key(4)] public int Phase { get; set; }
    [Key(5)] public int PlayerOneId { get; set; }
    [Key(6)] public int PlayerTwoId { get; set; }
    [Key(7)] public string PlayerOneName { get; set; } = "";
    [Key(8)] public string PlayerTwoName { get; set; } = "";
    [Key(9)] public int InstanceId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadResult"/> (1072).</summary>
[MessagePackObject]
public sealed class OlympiadResultMsg
{
    [Key(0)] public int MatchId { get; set; }
    /// <summary>0 tie, 1 player one won, 2 player two won, 3 player one disconnected, 4 player two disconnected, 5 both disconnected, 6 player one defaulted, 7 player two defaulted, 8 both defaulted.</summary>
    [Key(1)] public int Outcome { get; set; }
    /// <summary>0 when there is no single winner (tie, both crashed, or a default).</summary>
    [Key(2)] public int WinnerObjectId { get; set; }
    [Key(3)] public int PointsOne { get; set; }
    [Key(4)] public int PointsTwo { get; set; }
    [Key(5)] public int DeltaOne { get; set; }
    [Key(6)] public int DeltaTwo { get; set; }
    [Key(7)] public int RewardItemId { get; set; }
    [Key(8)] public int RewardCount { get; set; }
    [Key(9)] public int CompetitionsDoneOne { get; set; }
    [Key(10)] public int CompetitionsDoneTwo { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.OlympiadCountdown"/> (1073).
/// <see cref="Kind"/> 1 is the stadium teleport, 2 is the fight start, 3 is the return to town.
/// </summary>
[MessagePackObject]
public sealed class OlympiadCountdownMsg
{
    [Key(0)] public int MatchId { get; set; }
    [Key(1)] public int Kind { get; set; }
    [Key(2)] public int Seconds { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TeleportSelectRequest"/> (105).</summary>
[MessagePackObject]
public sealed class TeleportSelectRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public string ListName { get; set; } = "";
    [Key(2)] public int LocationId { get; set; }
}

/// <summary>One deposit or withdraw line. Object id is the inventory or warehouse instance.</summary>
[MessagePackObject]
public sealed class WarehouseCountMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int Count { get; set; }
}

/// <summary>Opcode <see cref="MsgType.WarehouseDepositRequest"/> (106).</summary>
[MessagePackObject]
public sealed class WarehouseDepositRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public WarehouseCountMsg[] Lines { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.WarehouseWithdrawRequest"/> (107).</summary>
[MessagePackObject]
public sealed class WarehouseWithdrawRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public WarehouseCountMsg[] Lines { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.SupportMagicRequest"/> (108).</summary>
[MessagePackObject]
public sealed class SupportMagicRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.GiveBlessingRequest"/> (109).</summary>
[MessagePackObject]
public sealed class GiveBlessingRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
}

/// <summary>One row of <see cref="TeleportListMsg"/>.</summary>
[MessagePackObject]
public sealed class TeleportLocationMsg
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int X { get; set; }
    [Key(3)] public int Y { get; set; }
    [Key(4)] public int Z { get; set; }
    [Key(5)] public int FeeItemId { get; set; }
    /// <summary>What this player pays now, after the free-level and evening discount rules. 0 is free.</summary>
    [Key(6)] public int FeeCount { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TeleportList"/> (1105).</summary>
[MessagePackObject]
public sealed class TeleportListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public string ListName { get; set; } = "";
    [Key(2)] public string ListType { get; set; } = "";
    [Key(3)] public TeleportLocationMsg[] Locations { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.WarehouseDepositList"/> (1106).</summary>
[MessagePackObject]
public sealed class WarehouseDepositListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int FeePerLine { get; set; }
    [Key(2)] public int SlotsUsed { get; set; }
    [Key(3)] public int SlotsMax { get; set; }
    [Key(4)] public int Adena { get; set; }
    [Key(5)] public ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.WarehouseWithdrawList"/> (1107).</summary>
[MessagePackObject]
public sealed class WarehouseWithdrawListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int SlotsUsed { get; set; }
    [Key(2)] public int SlotsMax { get; set; }
    [Key(3)] public int InventorySlotsUsed { get; set; }
    [Key(4)] public int InventorySlotsMax { get; set; }
    [Key(5)] public int CurrentWeight { get; set; }
    [Key(6)] public int MaxWeight { get; set; }
    [Key(7)] public ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.NewbieBuffResult"/> (1108).</summary>
[MessagePackObject]
public sealed class NewbieBuffResultMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int[] SkillIds { get; set; } = [];
    /// <summary>Empty when buffs were applied. Otherwise <see cref="NewbieBuffRefusal"/> name is not used; this is a short code: low, high, newbie, class, none.</summary>
    [Key(2)] public string Refusal { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.BlessingResult"/> (1109).</summary>
[MessagePackObject]
public sealed class BlessingResultMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int SkillId { get; set; }
    [Key(2)] public bool Accepted { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestBuyItem"/> (96). Counts are template counts, not object ids.</summary>
[MessagePackObject]
public sealed class RequestBuyItemMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int ListId { get; set; }
    [Key(2)] public ShopCountMsg[] Items { get; set; } = [];
}

/// <summary>One template id and count on <see cref="RequestBuyItemMsg"/>.</summary>
[MessagePackObject]
public sealed class ShopCountMsg
{
    [Key(0)] public int ItemId { get; set; }
    [Key(1)] public int Count { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestSellItem"/> (97). Counts come off one object id.</summary>
[MessagePackObject]
public sealed class RequestSellItemMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public ShopObjectCountMsg[] Items { get; set; } = [];
}

/// <summary>One bag object and count on <see cref="RequestSellItemMsg"/>.</summary>
[MessagePackObject]
public sealed class ShopObjectCountMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int Count { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.RequestMultiSellChoose"/> (98).
/// <see cref="EntryId"/> is the prepared id from <see cref="MultiSellEntryMsg"/>, not the template id.
/// </summary>
[MessagePackObject]
public sealed class RequestMultiSellChooseMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int ListId { get; set; }
    [Key(2)] public int EntryId { get; set; }
    [Key(3)] public int Amount { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestPreviewItem"/> (99). Wear stays stubbed.</summary>
[MessagePackObject]
public sealed class RequestPreviewItemMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int ListId { get; set; }
    [Key(2)] public int ItemId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.BuyList"/> (1095). <see cref="Price"/> is the taxed unit price.</summary>
[MessagePackObject]
public sealed class BuyListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int ListId { get; set; }
    [Key(2)] public int BaseTaxPercent { get; set; }
    [Key(3)] public int CastleTaxPercent { get; set; }
    [Key(4)] public BuyListItemMsg[] Items { get; set; } = [];
}

/// <summary>One buylist row. <see cref="Count"/> below 0 means the stock is not limited.</summary>
[MessagePackObject]
public sealed class BuyListItemMsg
{
    [Key(0)] public int ItemId { get; set; }
    [Key(1)] public int Price { get; set; }
    [Key(2)] public int Count { get; set; } = -1;
}

/// <summary>Opcode <see cref="MsgType.SellList"/> (1096). Only unequipped items the merchant will buy.</summary>
[MessagePackObject]
public sealed class SellListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public SellListItemMsg[] Items { get; set; } = [];
}

/// <summary>One sellable stack. <see cref="Price"/> is the reference price divided by 2.</summary>
[MessagePackObject]
public sealed class SellListItemMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int ItemId { get; set; }
    [Key(2)] public int Count { get; set; }
    [Key(3)] public int Price { get; set; }
    [Key(4)] public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.MultiSellList"/> (1097).</summary>
[MessagePackObject]
public sealed class MultiSellListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int ListId { get; set; }
    [Key(2)] public bool MaintainEnchantment { get; set; }
    [Key(3)] public bool InventoryOnly { get; set; }
    [Key(4)] public int CastleTaxPercent { get; set; }
    [Key(5)] public MultiSellEntryMsg[] Entries { get; set; } = [];
}

/// <summary>One prepared row. <see cref="EntryId"/> is what <see cref="RequestMultiSellChooseMsg"/> sends back.</summary>
[MessagePackObject]
public sealed class MultiSellEntryMsg
{
    [Key(0)] public int EntryId { get; set; }
    [Key(1)] public MultiSellPartMsg[] Ingredients { get; set; } = [];
    [Key(2)] public MultiSellPartMsg[] Products { get; set; } = [];
}

/// <summary>One ingredient or product. <see cref="EnchantLevel"/> is 0 when the row does not keep enchant.</summary>
[MessagePackObject]
public sealed class MultiSellPartMsg
{
    [Key(0)] public int ItemId { get; set; }
    [Key(1)] public int Count { get; set; }
    [Key(2)] public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ShopBalance"/> (1098). Castle tax is 0 when that castle has no owner.</summary>
[MessagePackObject]
public sealed class ShopBalanceMsg
{
    [Key(0)] public int Adena { get; set; }
    [Key(1)] public int Weight { get; set; }
    [Key(2)] public int MaxWeight { get; set; }
    [Key(3)] public int Slots { get; set; }
    [Key(4)] public int MaxSlots { get; set; }
    [Key(5)] public int BaseTaxPercent { get; set; }
    [Key(6)] public int CastleTaxPercent { get; set; }
}
