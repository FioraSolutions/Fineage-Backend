namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

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
