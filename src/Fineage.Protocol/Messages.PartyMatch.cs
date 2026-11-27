namespace Fineage.Protocol;

/// <summary>Opcode <see cref="MsgType.RequestPartyMatchConfig"/> — open room list / show current room.</summary>
public sealed class PartyMatchConfigMsg
{
    public int Auto { get; set; }
    public int Location { get; set; }
    public int Level { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestPartyMatchList"/> — create (roomId=0) or revise a room.</summary>
public sealed class PartyMatchListMsg
{
    public int RoomId { get; set; }
    public int MaxMembers { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int Loot { get; set; }
    public string Title { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.RequestPartyMatchDetail"/> — join a listed room.</summary>
public sealed class PartyMatchDetailRequestMsg
{
    public int RoomId { get; set; }
    public int Unk1 { get; set; }
    public int Unk2 { get; set; }
    public int Unk3 { get; set; }
}

public sealed class PartyMatchDismissMsg
{
    public int RoomId { get; set; }
    public int Unk { get; set; }
}

public sealed class PartyMatchWithdrawMsg
{
    public int RoomId { get; set; }
    public int Unk { get; set; }
}

public sealed class PartyMatchOustMsg
{
    public int ObjectId { get; set; }
}

public sealed class PartyMatchAskJoinMsg
{
    public string Name { get; set; } = "";
}

public sealed class PartyMatchAnswerJoinMsg
{
    public int Answer { get; set; }
}

public sealed class PartyMatchWaitingListRequestMsg
{
    public int Page { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int Mode { get; set; }
}

/// <summary>Zone → client room sheet (Mobius <c>PartyMatchDetail</c>).</summary>
public sealed class PartyMatchDetailMsg
{
    public int RoomId { get; set; }
    public int MaxMembers { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int Loot { get; set; }
    public int Location { get; set; }
    public string Title { get; set; } = "";
}

/// <summary>Zone → client room list (Mobius <c>ListPartyWating</c> / <c>PARTY_MATCH_LIST</c>).</summary>
public sealed class PartyMatchRoomListMsg
{
    public bool HasRooms { get; set; }
    public PartyMatchRoomListEntryMsg[] Rooms { get; set; } = [];
}

public sealed class PartyMatchRoomListEntryMsg
{
    public int RoomId { get; set; }
    public string Title { get; set; } = "";
    public int Location { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int MaxMembers { get; set; }
    public int Members { get; set; }
    public string OwnerName { get; set; } = "";
}

/// <summary>Zone → client member roster (Mobius <c>ExPartyRoomMember</c>).</summary>
public sealed class PartyRoomMemberListMsg
{
    public int Mode { get; set; }
    public PartyRoomMemberEntryMsg[] Members { get; set; } = [];
}

public sealed class PartyRoomMemberEntryMsg
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int Level { get; set; }
    public int Location { get; set; }
    /// <summary>1 owner, 2 same party as owner, 0 otherwise.</summary>
    public int MemberType { get; set; }
}

/// <summary>Zone → client member add/modify/quit (Mobius <c>ExManagePartyRoomMember</c>).</summary>
public sealed class PartyRoomMemberUpdateMsg
{
    public int Mode { get; set; }
    public PartyRoomMemberEntryMsg Member { get; set; } = new();
}

public sealed class PartyRoomAskJoinMsg
{
    public string CharName { get; set; } = "";
}

public sealed class PartyMatchWaitingRoomMsg
{
    public int Page { get; set; }
    public PartyMatchWaitingEntryMsg[] Players { get; set; } = [];
}

public sealed class PartyMatchWaitingEntryMsg
{
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int Level { get; set; }
}
