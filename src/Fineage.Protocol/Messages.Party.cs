namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

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
