namespace Fineage.Net.Contract
{
    public sealed class PartyMatchConfigNetMsg
    {
        public int Auto { get; set; }
        public int Location { get; set; }
        public int Level { get; set; }
    }

    public sealed class PartyMatchListNetMsg
    {
        public int RoomId { get; set; }
        public int MaxMembers { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int Loot { get; set; }
        public string Title { get; set; } = "";
    }

    public sealed class PartyMatchDetailRequestNetMsg
    {
        public int RoomId { get; set; }
        public int Unk1 { get; set; }
        public int Unk2 { get; set; }
        public int Unk3 { get; set; }
    }

    public sealed class PartyMatchDismissNetMsg
    {
        public int RoomId { get; set; }
        public int Unk { get; set; }
    }

    public sealed class PartyMatchWithdrawNetMsg
    {
        public int RoomId { get; set; }
        public int Unk { get; set; }
    }

    public sealed class PartyMatchOustNetMsg
    {
        public int ObjectId { get; set; }
    }

    public sealed class PartyMatchAskJoinNetMsg
    {
        public string Name { get; set; } = "";
    }

    public sealed class PartyMatchAnswerJoinNetMsg
    {
        public int Answer { get; set; }
    }

    public sealed class PartyMatchWaitingListRequestNetMsg
    {
        public int Page { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int Mode { get; set; }
    }

    public sealed class PartyMatchDetailNetMsg
    {
        public int RoomId { get; set; }
        public int MaxMembers { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int Loot { get; set; }
        public int Location { get; set; }
        public string Title { get; set; } = "";
    }

    public sealed class PartyMatchRoomListNetMsg
    {
        public bool HasRooms { get; set; }
        public PartyMatchRoomListEntryNet[] Rooms { get; set; } = System.Array.Empty<PartyMatchRoomListEntryNet>();
    }

    public sealed class PartyMatchRoomListEntryNet
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

    public sealed class PartyRoomMemberListNetMsg
    {
        public int Mode { get; set; }
        public PartyRoomMemberEntryNet[] Members { get; set; } = System.Array.Empty<PartyRoomMemberEntryNet>();
    }

    public sealed class PartyRoomMemberEntryNet
    {
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
        public int Location { get; set; }
        public int MemberType { get; set; }
    }

    public sealed class PartyRoomMemberUpdateNetMsg
    {
        public int Mode { get; set; }
        public PartyRoomMemberEntryNet Member { get; set; } = new();
    }

    public sealed class PartyRoomAskJoinNetMsg
    {
        public string CharName { get; set; } = "";
    }

    public sealed class PartyMatchWaitingRoomNetMsg
    {
        public int Page { get; set; }
        public PartyMatchWaitingEntryNet[] Players { get; set; } = System.Array.Empty<PartyMatchWaitingEntryNet>();
    }

    public sealed class PartyMatchWaitingEntryNet
    {
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
    }
}
