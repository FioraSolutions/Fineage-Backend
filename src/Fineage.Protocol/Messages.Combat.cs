namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

/// <summary>Opcode <see cref="MsgType.AttackRequest"/> (6). Uses session target when zero.</summary>
public sealed class AttackRequestMsg
{
    public int TargetObjectId { get; set; }
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

/// <summary>Admin/test: kill the requesting player. Empty body.</summary>
public sealed class AdminKillPlayerMsg;

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

/// <summary>Opcode <see cref="MsgType.SkillUseRequest"/> (50).</summary>
public sealed class SkillUseRequestMsg
{
    public int SkillId { get; set; }
    /// <summary>0 uses the selected target or self.</summary>
    public int TargetObjectId { get; set; }
    public bool CtrlPressed { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ReviveRequest"/> (52). Respawn after death.</summary>
public sealed class ReviveRequestMsg
{
    /// <summary><see cref="DeathProtocol.ReviveToVillage"/> is the MVP restart.</summary>
    public int Action { get; set; }
}

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
