
namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

public sealed class HelloMsg
{
    public int ProtocolVersion { get; set; } = 1;
    public string ClientBuild { get; set; } = "";
}

public sealed class HelloAckMsg
{
    public int ProtocolVersion { get; set; } = 1;
    public string ServerName { get; set; } = "FineageZone";
}

/// <summary>
/// Zone login. After character select, the client sends the account here and the handoff token as <see cref="Password"/>.
/// Today's ZoneServer accepts any password, so this payload is enough for it to open a session.
/// </summary>
public sealed class AuthMsg
{
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class AuthOkMsg
{
    public int SessionId { get; set; }
    public string CharacterName { get; set; } = "Test";
}

public sealed class ErrorMsg
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class RegisterMsg
{
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class RegisterOkMsg
{
    public string Account { get; set; } = "";
}

public sealed class LoginOkMsg
{
    public string Account { get; set; } = "";
}

public sealed class CreateCharacterMsg
{
    public string Name { get; set; } = "";
    /// <summary>
    /// Interlude base class. Human 0/10, Elf 18/25, Dark Elf 31/38, Orc 44/49, Dwarf 53.
    /// A non-base class is rejected.
    /// </summary>
    public int ClassId { get; set; }
    /// <summary>0 Human, 1 Elf, 2 Dark Elf, 3 Orc, 4 Dwarf. Must match the class.</summary>
    public int Race { get; set; }
    /// <summary>0 male, 1 female.</summary>
    public int Sex { get; set; }
    /// <summary>0–4 male, 0–6 female. Omitted by older clients (defaults to 0).</summary>
    public int HairStyle { get; set; }
    /// <summary>0–3.</summary>
    public int HairColor { get; set; }
    /// <summary>0–2.</summary>
    public int Face { get; set; }
}

public sealed class CharacterCreatedMsg
{
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int Race { get; set; }
    public int Sex { get; set; }
    public int HairStyle { get; set; }
    public int HairColor { get; set; }
    public int Face { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public sealed class CharacterSummaryMsg
{
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int Race { get; set; }
    public int Sex { get; set; }
    public int Level { get; set; } = 1;
    /// <summary>Index in creation order. Older clients ignore this key.</summary>
    public int Slot { get; set; }
    public int HairStyle { get; set; }
    public int HairColor { get; set; }
    public int Face { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    /// <summary>Whole seconds until hard delete, or 0 when the character is not pending.</summary>
    public int DeleteSecondsLeft { get; set; }
    public bool IsLastPlayed { get; set; }
    /// <summary>CharSelect vitals and progress (Mobius CharSelectionInfo). Older clients ignore these keys.</summary>
    public int CurHp { get; set; }
    public int MaxHp { get; set; }
    public int CurMp { get; set; }
    public int MaxMp { get; set; }
    public int CurCp { get; set; }
    public int MaxCp { get; set; }
    public long Exp { get; set; }
    public int Sp { get; set; }
    public int Karma { get; set; }
    /// <summary>Right-hand (or two-hand) item id, 0 when unarmed. Same as <see cref="Paperdoll"/>[7].</summary>
    public int WeaponItemId { get; set; }
    /// <summary>
    /// Equipped item ids in paperdoll order, length 17 (Under…Hair2). Index 7 is RHand, 9 gloves, 10 chest, 11 legs, 12 feet.
    /// Zero means empty. Older clients ignore this key.
    /// </summary>
    public int[] Paperdoll { get; set; } = [];
}

public sealed class CharacterListMsg
{
    public CharacterSummaryMsg[] Characters { get; set; } = [];
}

public sealed class SelectCharacterMsg
{
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Used when <see cref="CharacterId"/> is 0 and <see cref="Name"/> is empty. −1 means the client did not send a slot. Slot 0 is the first character.</summary>
    public int Slot { get; set; } = -1;
}

/// <summary>Opcode <see cref="MsgType.DeleteCharacter"/> (24). A positive <see cref="CharacterId"/> wins over <see cref="Slot"/>.</summary>
public sealed class DeleteCharacterMsg
{
    /// <summary>−1 when the client is deleting by id.</summary>
    public int Slot { get; set; } = -1;
    /// <summary>0 when the client is deleting by slot.</summary>
    public int CharacterId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RestoreCharacter"/> (25). Same keys as <see cref="DeleteCharacterMsg"/>.</summary>
public sealed class RestoreCharacterMsg
{
    public int Slot { get; set; } = -1;
    public int CharacterId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.CharacterDeleted"/> (1025). A <see cref="CharacterListMsg"/> follows.</summary>
public sealed class CharacterDeletedMsg
{
    public int Slot { get; set; }
    public int CharacterId { get; set; }
    public bool HardDeleted { get; set; }
}

/// <summary>Opcode <see cref="MsgType.CharacterDeleteFailed"/> (1026).</summary>
public sealed class CharacterDeleteFailedMsg
{
    public int Slot { get; set; }
    public int CharacterId { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>
/// Character select result. <see cref="Token"/> is the zone session key (<see cref="AuthMsg.Password"/>).
/// </summary>
public sealed class HandoffMsg
{
    public string Account { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public int CharacterId { get; set; }
    public int ClassId { get; set; }
    public string Token { get; set; } = "";
    public string ZoneHost { get; set; } = "127.0.0.1";
    public int ZonePort { get; set; } = 9777;
    public long ExpiresUnixSeconds { get; set; }
}

public sealed class EnterWorldMsg
{
    public int CharacterSlot { get; set; }
}

/// <summary>Same keys as tools/zone-server <c>PlayerSpawnedMsg</c>.</summary>
public sealed class PlayerSpawnedMsg
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public float RunSpeed { get; set; } = 120f;
    public float WalkSpeed { get; set; } = 80f;
    public bool Running { get; set; } = true;
}

/// <summary>Same keys as tools/zone-server <c>MoveToMsg</c>. Also the body of <see cref="MsgType.MoveToAck"/>.</summary>
public sealed class MoveToMsg
{
    public int ObjectId { get; set; }
    public int DestX { get; set; }
    public int DestY { get; set; }
    public int DestZ { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public int OriginZ { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.CharInfo"/> (1005). Another player entered knownlist.
/// Keys match tools/zone-server <c>CharInfoMsg</c>.
/// </summary>
public sealed class CharInfoMsg
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int ClassId { get; set; }
    /// <summary>
    /// Item ids in paperdoll order, length 17. Index 0 is Under, 7 is the right hand, 10 is chest, 11 is legs, 16 is Hair2.
    /// Zero means the slot is empty. Older clients ignore this key.
    /// </summary>
    public int[] Paperdoll { get; set; } = [];
    /// <summary>0 when the character is not in a clan. Older clients ignore this key.</summary>
    public int ClanId { get; set; }
    /// <summary>Pledge crest id. Always 0 until crests exist.</summary>
    public int ClanCrestId { get; set; }
    /// <summary>
    /// Enchant level of each paperdoll slot, same order and length as <see cref="Paperdoll"/>.
    /// Zero when the slot is empty or the item is +0. Older clients ignore this key.
    /// </summary>
    public int[] Enchant { get; set; } = [];
}

/// <summary>One paperdoll slot in <see cref="UserInfoMsg"/>. Object id and item id are 0 when the slot is empty.</summary>
public sealed class PaperdollSlotMsg
{
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    /// <summary>Enchant level while this slot is occupied. 0 when empty or +0. Older clients ignore this key.</summary>
    public int EnchantLevel { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.UserInfo"/> (1016). Sent to the entering player before <see cref="ItemListMsg"/>.
/// <see cref="Paperdoll"/> has 17 slots in <c>PaperdollSlot</c> order (right hand is index 7).
/// Keys 10–12 are the expertise grade penalty. They are 0 when the penalty is off or the gear is within grade.
/// </summary>
public sealed class UserInfoMsg
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public float RunSpeed { get; set; } = 120f;
    public float WalkSpeed { get; set; } = 80f;
    public bool Running { get; set; } = true;
    public PaperdollSlotMsg[] Paperdoll { get; set; } = [];
    /// <summary>Weapon expertise penalty, 0–4. Zero when every equipped weapon is within Expertise. Older clients ignore this key.</summary>
    public int WeaponGradePenalty { get; set; }
    /// <summary>Armor expertise penalty, 0–4. Arrows are not counted. Older clients ignore this key.</summary>
    public int ArmorGradePenalty { get; set; }
    /// <summary>
    /// Grade-penalty level, the greater of <see cref="WeaponGradePenalty"/> and <see cref="ArmorGradePenalty"/>, or 0.
    /// Mobius EtcStatusUpdate sends 1 when this is non-zero. Stat multipliers are not applied.
    /// </summary>
    public int GradePenalty { get; set; }
    /// <summary>0 when the character is not in a clan. Older clients ignore this key.</summary>
    public int ClanId { get; set; }
    /// <summary>Pledge crest id. Always 0 until crests exist.</summary>
    public int ClanCrestId { get; set; }
    /// <summary>CharSelect/world vitals. Older clients ignore keys 15+.</summary>
    public int Level { get; set; } = 1;
    public int CurHp { get; set; }
    public int MaxHp { get; set; }
    public int CurMp { get; set; }
    public int MaxMp { get; set; }
    public int CurCp { get; set; }
    public int MaxCp { get; set; }
    public long Exp { get; set; }
    public int Sp { get; set; }
    public int Str { get; set; }
    public int Dex { get; set; }
    public int Con { get; set; }
    public int Int { get; set; }
    public int Wit { get; set; }
    public int Men { get; set; }
    public int PAtk { get; set; }
    public int MAtk { get; set; }
    public int PDef { get; set; }
    public int MDef { get; set; }
    public int PAtkSpd { get; set; }
    public int MAtkSpd { get; set; }
    public int Accuracy { get; set; }
    public int CritRate { get; set; }
}

/// <summary>One row of <see cref="ItemListMsg"/> or the item fields of <see cref="InventoryUpdateEntryMsg"/>.</summary>
public sealed class ItemListEntryMsg
{
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }
    /// <summary>Paperdoll index while worn, or -1 in the bag.</summary>
    public int EquippedSlot { get; set; } = -1;
    /// <summary>none, rhand, lhand, lrhand, chest, legs, fullarmor, and the other body tokens.</summary>
    public string BodySlot { get; set; } = "none";
    /// <summary>weapon, armor, or etc.</summary>
    public string Kind { get; set; } = "etc";
    public string Icon { get; set; } = "";
    /// <summary>sword, dagger, blunt, and the other weapon tokens, or empty when this is not a weapon.</summary>
    public string WeaponType { get; set; } = "";
    /// <summary>light, heavy, magic, or empty when this is not armor.</summary>
    public string ArmorType { get; set; } = "";
    /// <summary>Enchant level. 0 when the piece is not enchanted. Older clients ignore this key.</summary>
    public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ItemList"/> (1014). Every item in the entering player's inventory.</summary>
public sealed class ItemListMsg
{
    public ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>0 add, 1 modify, 2 remove. Equip and unequip send modify rows. Give and grant send add rows.</summary>
public static class InventoryChange
{
    public const int Add = 0;
    public const int Modify = 1;
    public const int Remove = 2;
}

/// <summary>One row of <see cref="InventoryUpdateMsg"/>.</summary>
public sealed class InventoryUpdateEntryMsg
{
    /// <summary><see cref="InventoryChange.Add"/>, <see cref="InventoryChange.Modify"/>, or <see cref="InventoryChange.Remove"/>.</summary>
    public int Change { get; set; }
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }
    /// <summary>Paperdoll index while worn, or -1 in the bag. Remove rows still carry the last slot or -1.</summary>
    public int EquippedSlot { get; set; } = -1;
    public string BodySlot { get; set; } = "none";
    public string Kind { get; set; } = "etc";
    public string Icon { get; set; } = "";
    public string WeaponType { get; set; } = "";
    public string ArmorType { get; set; } = "";
    /// <summary>Enchant level. Remove rows still carry the level the piece had. Older clients ignore this key.</summary>
    public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.InventoryUpdate"/> (1015).</summary>
public sealed class InventoryUpdateMsg
{
    public InventoryUpdateEntryMsg[] Changes { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.DeleteObject"/> (1007). Left knownlist.</summary>
public sealed class DeleteObjectMsg
{
    public int ObjectId { get; set; }
}

public sealed class PingMsg
{
    public long ClientUnixMs { get; set; }
}

public sealed class PongMsg
{
    public long ClientUnixMs { get; set; }
    public long ServerUnixMs { get; set; }
}

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
}

/// <summary>
/// Opcode <see cref="MsgType.NpcInfo"/> (1006). <see cref="NpcId"/> is the template id
/// (Gremlin = 20001), not the Interlude value with 1_000_000 added.
/// Keys 0–9 are unchanged. Key 10 <see cref="Title"/> and key 11 <see cref="NpcType"/>
/// were appended for town NPCs. Do not renumber.
/// </summary>
public sealed class NpcInfoMsg
{
    public int ObjectId { get; set; }
    public int NpcId { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }
    public bool Attackable { get; set; } = true;
    public float RunSpeed { get; set; } = 80f;
    public float WalkSpeed { get; set; } = 40f;
    /// <summary>Mobius title, such as Trader or Gatekeeper. Empty when the template has none.</summary>
    public string Title { get; set; } = "";
    /// <summary>Mobius instance type, such as Merchant, Guard, Teleporter, or Monster.</summary>
    public string NpcType { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.SelectTarget"/> (5).</summary>
public sealed class SelectTargetMsg
{
    public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.AttackRequest"/> (6). Uses session target when zero.</summary>
public sealed class AttackRequestMsg
{
    public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.MyTargetSelected"/> (1008).</summary>
public sealed class MyTargetSelectedMsg
{
    public int ObjectId { get; set; }
    public int ColorHint { get; set; } = InterludeStatus.TargetColorHostile;
}

/// <summary>Opcode <see cref="MsgType.Attack"/> (1009).</summary>
public sealed class AttackMsg
{
    public int AttackerObjectId { get; set; }
    public int TargetObjectId { get; set; }
    public int Damage { get; set; }
    public byte Flags { get; set; }
    public int AttackerX { get; set; }
    public int AttackerY { get; set; }
    public int AttackerZ { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public int TargetZ { get; set; }
}

/// <summary>Opcode <see cref="MsgType.StatusUpdate"/> (1010). Interlude attribute ids in <see cref="AttributeIds"/>.</summary>
public sealed class StatusUpdateMsg
{
    public int ObjectId { get; set; }
    public int[] AttributeIds { get; set; } = [];
    public int[] Values { get; set; } = [];
}

/// <summary>
/// Zone → client: compact vitals sheet (level/exp/sp/hp/mp/cp). Entity wire ids are filled at the Net edge.
/// </summary>
public sealed class PlayerStatsMsg
{
    public int ObjectId { get; set; }
    public int Level { get; set; }
    public long Exp { get; set; }
    public int Sp { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Mp { get; set; }
    public int MaxMp { get; set; }
    public int Cp { get; set; }
    public int MaxCp { get; set; }
}

/// <summary>Interlude StatusUpdate attribute ids (see client StatusUpdate.AttributeType).</summary>
public static class InterludeStatus
{
    public const int Level = 0x01;
    public const int Exp = 0x02;
    public const int CurHp = 0x09;
    public const int MaxHp = 0x0a;
    public const int CurMp = 0x0b;
    public const int MaxMp = 0x0c;
    public const int Sp = 0x0d;
    public const int CurCp = 0x21;
    public const int MaxCp = 0x22;
    public const int TargetColorHostile = 11;

    /// <summary>Interlude MyTargetSelected color: attacker level minus target level (L2J/Mobius).</summary>
    public static int TargetColor(int attackerLevel, int targetLevel) => attackerLevel - targetLevel;

    /// <summary>
    /// Mobius <c>MyTargetSelected(Player, Creature)</c>: <c>target.isAutoAttackable(player) ? player.getLevel() - target.getLevel() : 0</c>.
    /// A friendly NPC (merchant, guide, village master) therefore comes back as 0 (white), never as a level delta.
    /// </summary>
    public static int TargetColor(int attackerLevel, int targetLevel, bool autoAttackable) =>
        autoAttackable ? TargetColor(attackerLevel, targetLevel) : 0;
}

/// <summary>Opcode <see cref="MsgType.GrantItemRequest"/> (13). Adds items to the requesting player's bag.</summary>
public sealed class GrantItemRequestMsg
{
    public int ItemId { get; set; }
    public int Count { get; set; } = 1;
}

/// <summary>Opcode <see cref="MsgType.PickupRequest"/> (14). Picks up a visible ground item.</summary>
public sealed class PickupRequestMsg
{
    public int ObjectId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.UseItem"/> (15).
/// An equippable item in the bag is equipped. The same object id, already worn, is unequipped.
/// </summary>
public sealed class UseItemMsg
{
    /// <summary>Inventory object id. Not an item template id.</summary>
    public int ObjectId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.UnequipItem"/> (16).
/// <see cref="PaperdollSlot"/> is a paperdoll index (0 Under … 16 Hair2), not a body-slot token.
/// </summary>
public sealed class UnequipItemMsg
{
    public int PaperdollSlot { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.GiveItem"/> (17). Admin/dev bag grant.
/// The zone accepts it only when <c>FINEAGE_ZONE_ADMIN</c> is <c>1</c>, <c>true</c>, or <c>yes</c>.
/// </summary>
public sealed class GiveItemMsg
{
    public int ItemId { get; set; }
    public int Count { get; set; } = 1;
}

/// <summary>Opcode <see cref="MsgType.AdminSpoilTarget"/> (30). Admin spoil hook before skill 254 exists.</summary>
public sealed class AdminSpoilTargetMsg
{
    public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.AdminSweepCorpse"/> (31). Admin sweeper hook before skill 42 exists.</summary>
public sealed class AdminSweepCorpseMsg
{
    public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.EnchantRequest"/> (39). Selects the scroll and opens the window.</summary>
public sealed class EnchantRequestMsg
{
    /// <summary>Inventory object id of the scroll. Not an item template id.</summary>
    public int ObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.EnchantConfirm"/> (40). The scroll is the one selected by the request.</summary>
public sealed class EnchantConfirmMsg
{
    /// <summary>Inventory object id of the weapon or armor to enchant.</summary>
    public int ObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.EnchantCancel"/> (41). An empty map is enough.</summary>
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
public sealed class EnchantResultMsg
{
    public int Result { get; set; }
    public int EnchantLevel { get; set; }
    /// <summary>Target object id. 0 on cancel.</summary>
    public int ObjectId { get; set; }
    /// <summary>Crystal item id gained from a normal failure. 0 when none are gained.</summary>
    public int CrystalItemId { get; set; }
    public int CrystalCount { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SpawnItem"/> (1018). Ground item in AOI.</summary>
public sealed class SpawnItemMsg
{
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public bool Stackable { get; set; }
    public int Count { get; set; } = 1;
    public int Unknown { get; set; }
}

/// <summary>Opcode <see cref="MsgType.DropItemRequest"/> (56). Discard from bag onto the ground.</summary>
public sealed class DropItemRequestMsg
{
    public int ObjectId { get; set; }
    public int Count { get; set; } = 1;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>Opcode <see cref="MsgType.DropItem"/> (1019). Ground drop with optional dropper for FX.</summary>
public sealed class DropItemMsg
{
    public int CharObjectId { get; set; }
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public bool Stackable { get; set; }
    public int Count { get; set; } = 1;
    public int Unknown { get; set; }
}

/// <summary>One row of <see cref="AbnormalStatusUpdateMsg"/>. Keys match client <c>EffectHolder</c>.</summary>
public sealed class AbnormalEffectMsg
{
    public int Id { get; set; }
    public int Value { get; set; }
    public int DurationSeconds { get; set; }
}

/// <summary>Opcode <see cref="MsgType.AbnormalStatusUpdate"/> (1017). Full effect list for one object.</summary>
public sealed class AbnormalStatusUpdateMsg
{
    public int ObjectId { get; set; }
    public AbnormalEffectMsg[] Effects { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.BuffRequest"/> (12). Playtest toggle until real skills land.</summary>
public sealed class BuffRequestMsg
{
    /// <summary>0 means the requesting player's object id.</summary>
    public int TargetObjectId { get; set; }
    public int EffectId { get; set; }
    /// <summary>When true, removes <see cref="EffectId"/> or clears all effects when zero.</summary>
    public bool Remove { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PartyInviteRequest"/> (18).</summary>
public sealed class PartyInviteRequestMsg
{
    public int TargetObjectId { get; set; }
    /// <summary>Loot mode for a new party; ignored when inviter is already in a party.</summary>
    public int ItemDistribution { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PartyAnswerJoinRequest"/> (19). Response 1 accept, 0 decline.</summary>
public sealed class PartyAnswerJoinRequestMsg
{
    public int RequestorObjectId { get; set; }
    public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PartyAskJoin"/> (1027).</summary>
public sealed class PartyAskJoinMsg
{
    public int RequestorObjectId { get; set; }
    public string RequestorName { get; set; } = "";
    public int ItemDistribution { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PartyJoinResult"/> (1028). 1 accepted, 0 declined.</summary>
public sealed class PartyJoinResultMsg
{
    public int Response { get; set; }
}

/// <summary>One row of <see cref="PartyMemberListMsg"/>.</summary>
public sealed class PartyMemberEntryMsg
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int Level { get; set; }
    public int CurHp { get; set; }
    public int MaxHp { get; set; }
    public int CurMp { get; set; }
    public int MaxMp { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PartyMemberList"/> (1029).</summary>
public sealed class PartyMemberListMsg
{
    public int LeaderObjectId { get; set; }
    public int PartyId { get; set; }
    public PartyMemberEntryMsg[] Members { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.PartyLeaveRequest"/> (26). Empty body.</summary>
public sealed class PartyLeaveRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.PartyKickRequest"/> (27).</summary>
public sealed class PartyKickRequestMsg
{
    public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeRequest"/> (28).</summary>
public sealed class TradeRequestMsg
{
    public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeAnswerRequest"/> (29).</summary>
public sealed class TradeAnswerRequestMsg
{
    public int RequestorObjectId { get; set; }
    public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeAddItemRequest"/> (54).</summary>
public sealed class TradeAddItemRequestMsg
{
    public int InventoryObjectId { get; set; }
    public long Count { get; set; } = 1;
}

/// <summary>Opcode <see cref="MsgType.TradeDoneRequest"/> (55). Response 1 confirm, 0 cancel.</summary>
public sealed class TradeDoneRequestMsg
{
    public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeAsk"/> (1073).</summary>
public sealed class TradeAskMsg
{
    public int RequestorObjectId { get; set; }
    public string RequestorName { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.TradeRequestResult"/> (1074).</summary>
public sealed class TradeRequestResultMsg
{
    public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeStart"/> (1075).</summary>
public sealed class TradeStartMsg
{
    public int PartnerObjectId { get; set; }
    public string PartnerName { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.TradeOfferUpdate"/> (1076).</summary>
public sealed class TradeOfferUpdateMsg
{
    public int IsOwn { get; set; }
    public int InventoryObjectId { get; set; }
    public int ItemId { get; set; }
    public long Count { get; set; }
    public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeConfirmState"/> (1077).</summary>
public sealed class TradeConfirmStateMsg
{
    public int SelfConfirmed { get; set; }
    public int PartnerConfirmed { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeFinished"/> (1078).</summary>
public sealed class TradeFinishedMsg
{
    public int Response { get; set; }
}

/// <summary>One sell-store listing row (Mobius TradeList sell item).</summary>
public sealed class PrivateStoreSellRowMsg
{
    public int InventoryObjectId { get; set; }
    public long Count { get; set; } = 1;
    public long Price { get; set; }
}

/// <summary>Open or refresh a private sell store (Mobius SetPrivateStoreListSell).</summary>
public sealed class SetPrivateStoreSellMsg
{
    public string Message { get; set; } = "";
    /// <summary>Mobius package sale: buyer must take the whole list.</summary>
    public bool Packaged { get; set; }
    public PrivateStoreSellRowMsg[] Items { get; set; } = [];
}

/// <summary>Close the active private store (Mobius RequestPrivateStoreQuitSell).</summary>
public sealed class PrivateStoreQuitMsg
{
}

/// <summary>Ask for a player's sell list (Mobius action on a store pawn → PrivateStoreListSell).</summary>
public sealed class PrivateStoreListRequestMsg
{
    public int StoreObjectId { get; set; }
}

/// <summary>Buy from a sell store (Mobius RequestPrivateStoreBuy).</summary>
public sealed class PrivateStoreBuyMsg
{
    public int StoreObjectId { get; set; }
    public PrivateStoreSellRowMsg[] Items { get; set; } = [];
}

/// <summary>One buy-store wish row (Mobius TradeList buy item — template id, not bag object).</summary>
public sealed class PrivateStoreBuyRowMsg
{
    public int ItemId { get; set; }
    public long Count { get; set; } = 1;
    public long Price { get; set; }
}

/// <summary>Open or refresh a private buy store (Mobius SetPrivateStoreListBuy).</summary>
public sealed class SetPrivateStoreBuyMsg
{
    public string Message { get; set; } = "";
    public PrivateStoreBuyRowMsg[] Items { get; set; } = [];
}

/// <summary>Sell items into a buy store (Mobius RequestPrivateStoreSell).</summary>
public sealed class PrivateStoreSellToMsg
{
    public int StoreObjectId { get; set; }
    public PrivateStoreSellRowMsg[] Items { get; set; } = [];
}

/// <summary>Zone → client: store type + title on the pawn (Mobius PrivateStoreMsg).</summary>
public sealed class PrivateStoreInfoMsg
{
    public int ObjectId { get; set; }
    public int StoreType { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>One sell-list row with template metadata for the buyer UI.</summary>
public sealed class PrivateStoreListedSellRowMsg
{
    public int InventoryObjectId { get; set; }
    public int ItemId { get; set; }
    public long Count { get; set; } = 1;
    public long Price { get; set; }
    public int EnchantLevel { get; set; }
}

/// <summary>Zone → client: a sell store's open list for a visitor.</summary>
public sealed class PrivateStoreListSellMsg
{
    public int StoreObjectId { get; set; }
    public string StoreName { get; set; } = "";
    public string Message { get; set; } = "";
    public int Packaged { get; set; }
    public long BuyerAdena { get; set; }
    public PrivateStoreListedSellRowMsg[] Items { get; set; } = [];
}

/// <summary>Zone → client: a buy store's open wish list for a visitor.</summary>
public sealed class PrivateStoreListBuyMsg
{
    public int StoreObjectId { get; set; }
    public string StoreName { get; set; } = "";
    public string Message { get; set; } = "";
    public long StoreAdena { get; set; }
    public PrivateStoreBuyRowMsg[] Items { get; set; } = [];
}

/// <summary>Open the recipe book (Mobius RequestRecipeBookOpen). Dwarven=true → dwarven tab.</summary>
public sealed class RecipeBookOpenMsg
{
    public bool Dwarven { get; set; }
}

/// <summary>Craft one recipe (Mobius RequestRecipeItemMakeSelf). <see cref="RecipeId"/> is the list id.</summary>
public sealed class RecipeMakeSelfMsg
{
    public int RecipeId { get; set; }
}

/// <summary>One manufacture listing (Mobius <c>ManufactureItem</c>: recipe list id + fee).</summary>
public sealed class RecipeShopRowMsg
{
    public int RecipeId { get; set; }
    public long Price { get; set; }
}

/// <summary>Open or refresh a manufacture shop (Mobius RequestRecipeShopListSet).</summary>
public sealed class SetRecipeShopMsg
{
    public string Message { get; set; } = "";
    public RecipeShopRowMsg[] Items { get; set; } = [];
}

/// <summary>Ask manufacturer MP / recipe status (Mobius RequestRecipeShopMakeInfo).</summary>
public sealed class RecipeShopMakeInfoMsg
{
    public int ManufacturerObjectId { get; set; }
    public int RecipeId { get; set; }
}

/// <summary>Request manufacture of one recipe (Mobius RequestRecipeShopMakeItem).</summary>
public sealed class RecipeShopMakeItemMsg
{
    public int ManufacturerObjectId { get; set; }
    public int RecipeId { get; set; }
}

/// <summary>Zone → client: known recipe ids for one craft book tab.</summary>
public sealed class RecipeBookListMsg
{
    public int Dwarven { get; set; }
    public int[] RecipeIds { get; set; } = [];
}

/// <summary>Zone → client: self-craft result (0 success, 1 fail, 2 cannot).</summary>
public sealed class RecipeItemMakeInfoMsg
{
    public int RecipeId { get; set; }
    public int Result { get; set; }
    public int CurrentMp { get; set; }
    public int MaxMp { get; set; }
}

/// <summary>Zone → client: manufacture recipe list for a visitor.</summary>
public sealed class RecipeShopSellListMsg
{
    public int ManufacturerObjectId { get; set; }
    public int CurrentMp { get; set; }
    public int MaxMp { get; set; }
    public long BuyerAdena { get; set; }
    public RecipeShopRowMsg[] Items { get; set; } = [];
}

/// <summary>Zone → client: manufacturer MP + recipe after make-info / make-item.</summary>
public sealed class RecipeShopItemInfoMsg
{
    public int ManufacturerObjectId { get; set; }
    public int RecipeId { get; set; }
    public int CurrentMp { get; set; }
    public int MaxMp { get; set; }
    /// <summary>0 = ok / success, 1 = fail (fee+mats consumed).</summary>
    public int Result { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestQuestList"/> (42). Empty body.</summary>
public sealed class RequestQuestListMsg
{
}

/// <summary>Opcode <see cref="MsgType.RequestQuestAbort"/> (43).</summary>
public sealed class RequestQuestAbortMsg
{
    public int QuestId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.NpcTalk"/> (44).
/// First-talk hook, or the NPC's HTML page when no quest step matches.
/// </summary>
public sealed class NpcTalkMsg
{
    public int NpcObjectId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.BypassRequest"/> (95). Mobius <c>RequestBypassToServer</c>.
/// <see cref="Command"/> may be <c>npc_&lt;objectId&gt;_Chat 1</c> or the command alone.
/// </summary>
public sealed class BypassRequestMsg
{
    public int NpcObjectId { get; set; }
    public string Command { get; set; } = "";
}

/// <summary>
/// Opcode <see cref="MsgType.QuestEvent"/> (45).
/// Empty <see cref="Event"/> is the talk hook. Any other token is the adv-event hook.
/// </summary>
public sealed class QuestEventMsg
{
    public int NpcObjectId { get; set; }
    public int QuestId { get; set; }
    public string Event { get; set; } = "";
}

/// <summary>One started quest in <see cref="QuestListMsg"/>.</summary>
public sealed class QuestJournalEntryMsg
{
    public int QuestId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>1 while started. Completed one-time quests are not in this list.</summary>
    public int State { get; set; }
    public int Cond { get; set; }
    /// <summary>Raw <c>__compltdStateFlags</c>. Zero when the journal uses <see cref="Cond"/>.</summary>
    public int ProgressFlags { get; set; }
    /// <summary>Flags when non-zero, otherwise <see cref="Cond"/>.</summary>
    public int JournalWord { get; set; }
    public bool Repeatable { get; set; }
}

/// <summary>Opcode <see cref="MsgType.QuestList"/> (1061).</summary>
public sealed class QuestListMsg
{
    public QuestJournalEntryMsg[] Active { get; set; } = [];
    /// <summary>128 bytes. Bit <c>id % 8</c> of byte <c>id / 8</c> for a completed one-time quest whose id is 0..1023.</summary>
    public byte[] CompletedMask { get; set; } = [];
    public int[] CompletedQuestIds { get; set; } = [];
    public int MaxActive { get; set; }
}

/// <summary>Opcode <see cref="MsgType.QuestDialog"/> (1062).</summary>
public sealed class QuestDialogMsg
{
    public int NpcObjectId { get; set; }
    public int NpcId { get; set; }
    public int QuestId { get; set; }
    /// <summary><c>first_talk</c>, <c>talk</c>, <c>event</c>, <c>kill</c>, or <c>default</c>.</summary>
    public string Hook { get; set; } = "";
    public string Event { get; set; } = "";
    public string Html { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.QuestSound"/> (1063).</summary>
public sealed class QuestSoundMsg
{
    public string Name { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.SkillUseRequest"/> (50).</summary>
public sealed class SkillUseRequestMsg
{
    public int SkillId { get; set; }
    /// <summary>0 uses the selected target or self.</summary>
    public int TargetObjectId { get; set; }
    public bool CtrlPressed { get; set; }
}

/// <summary>Opcode <see cref="MsgType.CharacterActionRequest"/> (51). Mobius <c>RequestActionUse</c> action id.</summary>
public sealed class CharacterActionRequestMsg
{
    public int ActionId { get; set; }
    public bool CtrlPressed { get; set; }
    public bool ShiftPressed { get; set; }
    /// <summary>Optional target for assist-style actions; unused in MVP.</summary>
    public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ChangeWaitType"/> (1067). Interlude wait type on <see cref="ActionProtocol.WaitSitting"/> / standing.</summary>
public sealed class ChangeWaitTypeMsg
{
    public int ObjectId { get; set; }
    public int WaitType { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SocialAction"/> (1068).</summary>
public sealed class SocialActionMsg
{
    public int ObjectId { get; set; }
    public int ActionId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ChangeMoveType"/> (1069).</summary>
public sealed class ChangeMoveTypeMsg
{
    public int ObjectId { get; set; }
    public bool Running { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ReviveRequest"/> (52). Respawn after death.</summary>
public sealed class ReviveRequestMsg
{
    /// <summary><see cref="DeathProtocol.ReviveToVillage"/> is the MVP restart.</summary>
    public int Action { get; set; }
}

/// <summary>Admin/test: kill the requesting player. Empty body.</summary>
public sealed class AdminKillPlayerMsg;

/// <summary>Opcode <see cref="MsgType.Die"/> (1074). Creature died; corpse stays in world for players.</summary>
public sealed class DieMsg
{
    public int ObjectId { get; set; }
    public int KillerObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.Revive"/> (1075).</summary>
public sealed class ReviveMsg
{
    public int ObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TeleportToLocation"/> (1076). Instant move (respawn, dungeon, …).</summary>
public sealed class TeleportToLocationMsg
{
    public int ObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }
}

/// <summary>One row of <see cref="SkillListMsg"/>.</summary>
public sealed class SkillListEntryMsg
{
    public int SkillId { get; set; }
    public int Level { get; set; }
    /// <summary>Remaining reuse in milliseconds. Zero when ready.</summary>
    public int ReuseDelayMs { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SkillList"/> (1065). Sent after <see cref="ItemListMsg"/> on enter.</summary>
public sealed class SkillListMsg
{
    public SkillListEntryMsg[] Skills { get; set; } = [];
}

/// <summary>One trainer offer row for <see cref="AcquireSkillListMsg"/> (Mobius <c>AcquireSkillList</c>).</summary>
public sealed class AcquireSkillEntryMsg
{
    public int SkillId { get; set; }
    public int NextLevel { get; set; }
    public int MaxLevel { get; set; }
    public int SpCost { get; set; }
    /// <summary>Mobius <c>requirements</c> bitmask (1 = needs the previous level, 2 = needs a book/item).</summary>
    public int Requirements { get; set; }
}

/// <summary>Trainer learnable rows (Zone → client). Net encodes as <c>AcquireSkillList</c>.</summary>
public sealed class AcquireSkillListMsg
{
    /// <summary>Mobius <c>AcquireSkillType</c> ordinal: 0 = class, 1 = fishing, 2 = pledge.</summary>
    public int SkillType { get; set; }
    public AcquireSkillEntryMsg[] Skills { get; set; } = [];
}

/// <summary>One requirement row for <see cref="AcquireSkillInfoMsg"/>.</summary>
public sealed class AcquireSkillRequirementMsg
{
    public int Type { get; set; }
    public int ItemId { get; set; }
    public long Count { get; set; }
    public int Unk { get; set; }
}

/// <summary>Confirmation page for one trainer offer (Zone → client). Net encodes as <c>AcquireSkillInfo</c>.</summary>
public sealed class AcquireSkillInfoMsg
{
    public int SkillId { get; set; }
    public int Level { get; set; }
    public int SpCost { get; set; }
    /// <summary>Mobius <c>AcquireSkillType</c> ordinal.</summary>
    public int SkillType { get; set; }
    public AcquireSkillRequirementMsg[] Requirements { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.MagicSkillUse"/> (1066).</summary>
public sealed class MagicSkillUseMsg
{
    public int CasterObjectId { get; set; }
    public int TargetObjectId { get; set; }
    public int SkillId { get; set; }
    public int SkillLevel { get; set; }
    public int HitTimeMs { get; set; }
    public int ReuseDelayMs { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.NpcHtml"/> (1094). Mobius <c>NpcHtmlMessage</c>.
/// Quest steps stay on <see cref="QuestDialogMsg"/>. <see cref="ItemId"/> is 0 until item HTML exists.
/// </summary>
public sealed class NpcHtmlMsg
{
    public int NpcObjectId { get; set; }
    public int NpcId { get; set; }
    public string Html { get; set; } = "";
    public int ItemId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ClanCreateRequest"/> (32).</summary>
public sealed class ClanCreateRequestMsg
{
    public string Name { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.ClanInviteRequest"/> (33). Pledge type must be 0 (main).</summary>
public sealed class ClanInviteRequestMsg
{
    public int TargetObjectId { get; set; }
    public int PledgeType { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.ClanAnswerJoinRequest"/> (34).
/// Response 0 declines. Any other value accepts (Mobius <c>RequestAnswerJoinPledge</c>).
/// </summary>
public sealed class ClanAnswerJoinRequestMsg
{
    public int RequestorObjectId { get; set; }
    public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ClanLeaveRequest"/> (35). Empty body.</summary>
public sealed class ClanLeaveRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.ClanKickRequest"/> (36).</summary>
public sealed class ClanKickRequestMsg
{
    public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ClanDissolveRequest"/> (37). Empty body.</summary>
public sealed class ClanDissolveRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.ClanCancelDissolveRequest"/> (38). Empty body.</summary>
public sealed class ClanCancelDissolveRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.ClanAskJoin"/> (1050).</summary>
public sealed class ClanAskJoinMsg
{
    public int RequestorObjectId { get; set; }
    public string RequestorName { get; set; } = "";
    public int ClanId { get; set; }
    public string ClanName { get; set; } = "";
    public int PledgeType { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PledgeStatusChanged"/> (1051). Clan id 0 clears the pledge.</summary>
public sealed class PledgeStatusChangedMsg
{
    public int ClanId { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int LeaderObjectId { get; set; }
    public int CrestId { get; set; }
    public int MemberCount { get; set; }
    public long DissolvingExpiryUnixMs { get; set; }
    public long CharPenaltyExpiryUnixMs { get; set; }
}

/// <summary>One row of <see cref="PledgeMemberListMsg"/>.</summary>
public sealed class PledgeMemberEntryMsg
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int Level { get; set; }
    public bool Online { get; set; }
    public int PledgeType { get; set; }
}

/// <summary>Opcode <see cref="MsgType.PledgeMemberList"/> (1052).</summary>
public sealed class PledgeMemberListMsg
{
    public int ClanId { get; set; }
    public int LeaderObjectId { get; set; }
    public PledgeMemberEntryMsg[] Members { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.ClanJoinResult"/> (1053). 0 declined, otherwise the accepted response.</summary>
public sealed class ClanJoinResultMsg
{
    public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SevenSignsStatusRequest"/> (90). Page is ignored; the reply is the full record.</summary>
public sealed class SevenSignsStatusRequestMsg
{
    public int Page { get; set; } = 1;
}

/// <summary>
/// Opcode <see cref="MsgType.SevenSignsActionRequest"/> (91).
/// <see cref="Action"/> is <see cref="SevenSignsProtocol.ActionJoin"/> or <see cref="SevenSignsProtocol.ActionContribute"/>.
/// </summary>
public sealed class SevenSignsActionRequestMsg
{
    public int Action { get; set; }
    /// <summary>1 Dusk, 2 Dawn. Used by join.</summary>
    public int Cabal { get; set; }
    /// <summary>1 Avarice, 2 Gnosis, 3 Strife. Used by join.</summary>
    public int Seal { get; set; }
    public int BlueStones { get; set; }
    public int GreenStones { get; set; }
    public int RedStones { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SevenSignsStatus"/> (1090). One record instead of the four Interlude SSQStatus pages.</summary>
public sealed class SevenSignsStatusMsg
{
    public int Period { get; set; }
    public int Cycle { get; set; }
    public int PlayerCabal { get; set; }
    public int PlayerSeal { get; set; }
    public int PlayerStoneCount { get; set; }
    public int PlayerAncientAdena { get; set; }
    public int PlayerContribution { get; set; }
    public int DawnScore { get; set; }
    public int DuskScore { get; set; }
    public int DawnFestivalScore { get; set; }
    public int DuskFestivalScore { get; set; }
    public long DawnStonePoints { get; set; }
    public long DuskStonePoints { get; set; }
    public int AvariceOwner { get; set; }
    public int GnosisOwner { get; set; }
    public int StrifeOwner { get; set; }
    public int DawnMembers { get; set; }
    public int DuskMembers { get; set; }
    public int WinningCabal { get; set; }
    public long NextChangeUnixMs { get; set; }
    public int SkyState { get; set; }
    public int DawnAvarice { get; set; }
    public int DawnGnosis { get; set; }
    public int DawnStrife { get; set; }
    public int DuskAvarice { get; set; }
    public int DuskGnosis { get; set; }
    public int DuskStrife { get; set; }
    public int PreviousWinner { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SevenSignsSky"/> (1091). State 0 normal, 1 dusk, 2 dawn.</summary>
public sealed class SevenSignsSkyMsg
{
    public int State { get; set; }
    public int Period { get; set; }
    public int WinningCabal { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.DungeonEnterRequest"/> (92).
/// <see cref="Action"/> is <see cref="DungeonProtocol.ActionEnter"/> or <see cref="DungeonProtocol.ActionExit"/>.
/// </summary>
public sealed class DungeonEnterRequestMsg
{
    public int DungeonId { get; set; }
    public int Action { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.DungeonEntered"/> (1092).
/// <see cref="Action"/> is enter, exit, or the period-change kick.
/// <see cref="Inside"/> is true only after a successful entrance teleport.
/// </summary>
public sealed class DungeonEnteredMsg
{
    public int DungeonId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public bool Inside { get; set; }
    public int Action { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.SiegeRegisterRequest"/> (46).
/// <see cref="Attacker"/> 1 registers as attacker, 0 as defender.
/// <see cref="Joining"/> 1 registers, 0 withdraws. Mobius <c>RequestJoinSiege</c>.
/// </summary>
public sealed class SiegeRegisterRequestMsg
{
    public int CastleId { get; set; }
    public int Attacker { get; set; }
    public int Joining { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.SiegeDefenderAnswerRequest"/> (47).
/// <see cref="Approved"/> 1 accepts a waiting defender. Any other value refuses. Mobius <c>RequestConfirmSiegeWaitingList</c>.
/// </summary>
public sealed class SiegeDefenderAnswerRequestMsg
{
    public int CastleId { get; set; }
    public int ClanId { get; set; }
    public int Approved { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SiegeListRequest"/> (48).</summary>
public sealed class SiegeListRequestMsg
{
    public int CastleId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.AdminCaptureCastle"/> (49). Off unless <c>FINEAGE_ZONE_ADMIN</c> is set.
/// Calls <c>CaptureCastle(clanId)</c>. Combat does not send this.
/// </summary>
public sealed class AdminCaptureCastleMsg
{
    public int CastleId { get; set; }
    public int ClanId { get; set; }
}

/// <summary>One clan on <see cref="SiegeInfoMsg"/>. Side is owner -1, defender 0, attacker 1, or pending 2.</summary>
public sealed class SiegeClanMsg
{
    public int ClanId { get; set; }
    public int Side { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SiegeInfo"/> (1064). The castle and its current registrations.</summary>
public sealed class SiegeInfoMsg
{
    public int CastleId { get; set; }
    public string Name { get; set; } = "";
    public int OwnerClanId { get; set; }
    public int TaxPercent { get; set; }
    public long SiegeUnixMs { get; set; }
    public long RegistrationDeadlineUnixMs { get; set; }
    public bool RegistrationClosed { get; set; }
    public bool InProgress { get; set; }
    public SiegeClanMsg[] Clans { get; set; } = [];
    /// <summary>Unix milliseconds when the siege ends. 0 when it is not in progress.</summary>
    public long EndsUnixMs { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.BossStatusRequest"/> (93).
/// <see cref="NpcId"/> 0 asks for every scheduled boss.
/// </summary>
public sealed class BossStatusRequestMsg
{
    public int NpcId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.AdminBossSchedule"/> (94).
/// <see cref="Action"/> is <see cref="BossProtocol.ActionMarkDead"/> or <see cref="BossProtocol.ActionForceSpawn"/>.
/// </summary>
public sealed class AdminBossScheduleMsg
{
    public int NpcId { get; set; }
    public int Action { get; set; }
}

/// <summary>Opcode <see cref="MsgType.BossStatus"/> (1093).</summary>
public sealed class BossStatusMsg
{
    public BossStatusEntryMsg[] Bosses { get; set; } = [];
}

/// <summary>One row of <see cref="BossStatusMsg"/>. Keys are 0..10.</summary>
public sealed class BossStatusEntryMsg
{
    public int NpcId { get; set; }
    public int ObjectId { get; set; }
    /// <summary><see cref="BossProtocol.KindRaid"/> or <see cref="BossProtocol.KindGrand"/>.</summary>
    public int Kind { get; set; }
    /// <summary>Alive 0, waiting 1, fighting 2, dead 3. See <see cref="BossProtocol"/>.</summary>
    public int Status { get; set; }
    public long RespawnUnixMs { get; set; }
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }
    public int CurrentMp { get; set; }
    public int MaxMp { get; set; }
    public bool Enabled { get; set; }
    public bool Spawned { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadStatusRequest"/> (60). Empty body.</summary>
public sealed class OlympiadStatusRequestMsg
{
}

/// <summary>
/// Opcode <see cref="MsgType.OlympiadRegisterRequest"/> (61).
/// <see cref="Classed"/> 1 is the class queue. 0 is the non-classed queue.
/// </summary>
public sealed class OlympiadRegisterRequestMsg
{
    public int Classed { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadUnregisterRequest"/> (62). Empty body.</summary>
public sealed class OlympiadUnregisterRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.OlympiadLeaveRequest"/> (63). Empty body. Leaving a match is a loss.</summary>
public sealed class OlympiadLeaveRequestMsg
{
}

/// <summary>
/// Opcode <see cref="MsgType.AdminReportOlympiadResult"/> (64). Off unless <c>FINEAGE_ZONE_ADMIN</c> is set.
/// <see cref="WinnerObjectId"/> 0 is a draw. Combat calls <c>ReportOlympiadResult</c> with the same pair.
/// </summary>
public sealed class AdminReportOlympiadResultMsg
{
    public int MatchId { get; set; }
    public int WinnerObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadStatus"/> (1070).</summary>
public sealed class OlympiadStatusMsg
{
    public int Period { get; set; }
    public int Cycle { get; set; }
    public bool InCompetition { get; set; }
    public long CompetitionStartUnixMs { get; set; }
    public long CompetitionEndUnixMs { get; set; }
    public long OlympiadEndUnixMs { get; set; }
    public long ValidationEndUnixMs { get; set; }
    public long NextWeeklyUnixMs { get; set; }
    public bool RegistrationOpen { get; set; }
    public bool Noble { get; set; }
    public int Points { get; set; }
    public int CompetitionsDone { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Drawn { get; set; }
    /// <summary>0 none, 1 classed queue, 2 non-classed queue.</summary>
    public int Queue { get; set; }
    public int MatchId { get; set; }
    /// <summary>Stadium index, or -1 when this character is not in a match.</summary>
    public int StadiumId { get; set; }
    public int ClassId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadMatch"/> (1071). Sent to both participants.</summary>
public sealed class OlympiadMatchMsg
{
    public int MatchId { get; set; }
    public int StadiumId { get; set; }
    public int ZoneId { get; set; }
    public bool Classed { get; set; }
    /// <summary>0 countdown, 1 pause, 2 battle countdown, 3 fighting, 4 returning.</summary>
    public int Phase { get; set; }
    public int PlayerOneId { get; set; }
    public int PlayerTwoId { get; set; }
    public string PlayerOneName { get; set; } = "";
    public string PlayerTwoName { get; set; } = "";
    public int InstanceId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadResult"/> (1072).</summary>
public sealed class OlympiadResultMsg
{
    public int MatchId { get; set; }
    /// <summary>0 tie, 1 player one won, 2 player two won, 3 player one disconnected, 4 player two disconnected, 5 both disconnected, 6 player one defaulted, 7 player two defaulted, 8 both defaulted.</summary>
    public int Outcome { get; set; }
    /// <summary>0 when there is no single winner (tie, both crashed, or a default).</summary>
    public int WinnerObjectId { get; set; }
    public int PointsOne { get; set; }
    public int PointsTwo { get; set; }
    public int DeltaOne { get; set; }
    public int DeltaTwo { get; set; }
    public int RewardItemId { get; set; }
    public int RewardCount { get; set; }
    public int CompetitionsDoneOne { get; set; }
    public int CompetitionsDoneTwo { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.OlympiadCountdown"/> (1073).
/// <see cref="Kind"/> 1 is the stadium teleport, 2 is the fight start, 3 is the return to town.
/// </summary>
public sealed class OlympiadCountdownMsg
{
    public int MatchId { get; set; }
    public int Kind { get; set; }
    public int Seconds { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TeleportSelectRequest"/> (105).</summary>
public sealed class TeleportSelectRequestMsg
{
    public int NpcObjectId { get; set; }
    public string ListName { get; set; } = "";
    public int LocationId { get; set; }
}

/// <summary>One deposit or withdraw line. Object id is the inventory or warehouse instance.</summary>
public sealed class WarehouseCountMsg
{
    public int ObjectId { get; set; }
    public int Count { get; set; }
}

/// <summary>Opcode <see cref="MsgType.WarehouseDepositRequest"/> (106).</summary>
public sealed class WarehouseDepositRequestMsg
{
    public int NpcObjectId { get; set; }
    public WarehouseCountMsg[] Lines { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.WarehouseWithdrawRequest"/> (107).</summary>
public sealed class WarehouseWithdrawRequestMsg
{
    public int NpcObjectId { get; set; }
    public WarehouseCountMsg[] Lines { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.SupportMagicRequest"/> (108).</summary>
public sealed class SupportMagicRequestMsg
{
    public int NpcObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.GiveBlessingRequest"/> (109).</summary>
public sealed class GiveBlessingRequestMsg
{
    public int NpcObjectId { get; set; }
}

/// <summary>One row of <see cref="TeleportListMsg"/>.</summary>
public sealed class TeleportLocationMsg
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int FeeItemId { get; set; }
    /// <summary>What this player pays now, after the free-level and evening discount rules. 0 is free.</summary>
    public int FeeCount { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TeleportList"/> (1105).</summary>
public sealed class TeleportListMsg
{
    public int NpcObjectId { get; set; }
    public string ListName { get; set; } = "";
    public string ListType { get; set; } = "";
    public TeleportLocationMsg[] Locations { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.WarehouseDepositList"/> (1106).</summary>
public sealed class WarehouseDepositListMsg
{
    public int NpcObjectId { get; set; }
    public int FeePerLine { get; set; }
    public int SlotsUsed { get; set; }
    public int SlotsMax { get; set; }
    public int Adena { get; set; }
    public ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.WarehouseWithdrawList"/> (1107).</summary>
public sealed class WarehouseWithdrawListMsg
{
    public int NpcObjectId { get; set; }
    public int SlotsUsed { get; set; }
    public int SlotsMax { get; set; }
    public int InventorySlotsUsed { get; set; }
    public int InventorySlotsMax { get; set; }
    public int CurrentWeight { get; set; }
    public int MaxWeight { get; set; }
    public ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.NewbieBuffResult"/> (1108).</summary>
public sealed class NewbieBuffResultMsg
{
    public int NpcObjectId { get; set; }
    public int[] SkillIds { get; set; } = [];
    /// <summary>Empty when buffs were applied. Otherwise <see cref="NewbieBuffRefusal"/> name is not used; this is a short code: low, high, newbie, class, none.</summary>
    public string Refusal { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.BlessingResult"/> (1109).</summary>
public sealed class BlessingResultMsg
{
    public int NpcObjectId { get; set; }
    public int SkillId { get; set; }
    public bool Accepted { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestBuyItem"/> (96). Counts are template counts, not object ids.</summary>
public sealed class RequestBuyItemMsg
{
    public int NpcObjectId { get; set; }
    public int ListId { get; set; }
    public ShopCountMsg[] Items { get; set; } = [];
}

/// <summary>One template id and count on <see cref="RequestBuyItemMsg"/>.</summary>
public sealed class ShopCountMsg
{
    public int ItemId { get; set; }
    public int Count { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestSellItem"/> (97). Counts come off one object id.</summary>
public sealed class RequestSellItemMsg
{
    public int NpcObjectId { get; set; }
    public ShopObjectCountMsg[] Items { get; set; } = [];
}

/// <summary>One bag object and count on <see cref="RequestSellItemMsg"/>.</summary>
public sealed class ShopObjectCountMsg
{
    public int ObjectId { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.RequestMultiSellChoose"/> (98).
/// <see cref="EntryId"/> is the prepared id from <see cref="MultiSellEntryMsg"/>, not the template id.
/// </summary>
public sealed class RequestMultiSellChooseMsg
{
    public int NpcObjectId { get; set; }
    public int ListId { get; set; }
    public int EntryId { get; set; }
    public int Amount { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestPreviewItem"/> (99). Wear stays stubbed.</summary>
public sealed class RequestPreviewItemMsg
{
    public int NpcObjectId { get; set; }
    public int ListId { get; set; }
    public int ItemId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.BuyList"/> (1095). <see cref="Price"/> is the taxed unit price.</summary>
public sealed class BuyListMsg
{
    public int NpcObjectId { get; set; }
    public int ListId { get; set; }
    public int BaseTaxPercent { get; set; }
    public int CastleTaxPercent { get; set; }
    public BuyListItemMsg[] Items { get; set; } = [];
}

/// <summary>One buylist row. <see cref="Count"/> below 0 means the stock is not limited.</summary>
public sealed class BuyListItemMsg
{
    public int ItemId { get; set; }
    public int Price { get; set; }
    public int Count { get; set; } = -1;
}

/// <summary>Opcode <see cref="MsgType.SellList"/> (1096). Only unequipped items the merchant will buy.</summary>
public sealed class SellListMsg
{
    public int NpcObjectId { get; set; }
    public SellListItemMsg[] Items { get; set; } = [];
}

/// <summary>One sellable stack. <see cref="Price"/> is the reference price divided by 2.</summary>
public sealed class SellListItemMsg
{
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }
    public int Price { get; set; }
    public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.MultiSellList"/> (1097).</summary>
public sealed class MultiSellListMsg
{
    public int NpcObjectId { get; set; }
    public int ListId { get; set; }
    public bool MaintainEnchantment { get; set; }
    public bool InventoryOnly { get; set; }
    public int CastleTaxPercent { get; set; }
    public MultiSellEntryMsg[] Entries { get; set; } = [];
}

/// <summary>One prepared row. <see cref="EntryId"/> is what <see cref="RequestMultiSellChooseMsg"/> sends back.</summary>
public sealed class MultiSellEntryMsg
{
    public int EntryId { get; set; }
    public MultiSellPartMsg[] Ingredients { get; set; } = [];
    public MultiSellPartMsg[] Products { get; set; } = [];
}

/// <summary>One ingredient or product. <see cref="EnchantLevel"/> is 0 when the row does not keep enchant.</summary>
public sealed class MultiSellPartMsg
{
    public int ItemId { get; set; }
    public int Count { get; set; }
    public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ShopBalance"/> (1098). Castle tax is 0 when that castle has no owner.</summary>
public sealed class ShopBalanceMsg
{
    public int Adena { get; set; }
    public int Weight { get; set; }
    public int MaxWeight { get; set; }
    public int Slots { get; set; }
    public int MaxSlots { get; set; }
    public int BaseTaxPercent { get; set; }
    public int CastleTaxPercent { get; set; }
}
