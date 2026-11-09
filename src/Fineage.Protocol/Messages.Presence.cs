namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

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
    /// <summary>0 human … race ordinals. Net clients need this on the owner sheet.</summary>
    public int Race { get; set; }
    /// <summary>0 male, 1 female.</summary>
    public int Sex { get; set; }
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
    public int Evasion { get; set; }
    public int CritRate { get; set; }
    /// <summary>Mobius currentLoad (inventory template weight × count).</summary>
    public int CurrentWeight { get; set; }
    /// <summary>Mobius getMaxLoad (CON bonus × 69000 × AltWeightLimit, then weightLimit passives).</summary>
    public int MaxWeight { get; set; }
}

/// <summary>Opcode <see cref="MsgType.DeleteObject"/> (1007). Left knownlist.</summary>
public sealed class DeleteObjectMsg
{
    public int ObjectId { get; set; }
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

/// <summary>Opcode <see cref="MsgType.MyTargetSelected"/> (1008).</summary>
public sealed class MyTargetSelectedMsg
{
    public int ObjectId { get; set; }
    public int ColorHint { get; set; } = InterludeStatus.TargetColorHostile;
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

/// <summary>Opcode <see cref="MsgType.TeleportToLocation"/> (1076). Instant move (respawn, dungeon, …).</summary>
public sealed class TeleportToLocationMsg
{
    public int ObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }
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
