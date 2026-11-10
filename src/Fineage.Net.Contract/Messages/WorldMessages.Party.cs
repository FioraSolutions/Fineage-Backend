namespace Fineage.Net.Contract
{
    public sealed class PartyInviteRequestNetMsg
    {
        public int TargetObjectId { get; set; }
        public int ItemDistribution { get; set; }
    }

    public sealed class PartyAnswerJoinRequestNetMsg
    {
        public int RequestorObjectId { get; set; }
        public int Response { get; set; }
    }

    public sealed class PartyKickRequestNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    public sealed class PartyAskJoinNetMsg
    {
        public int RequestorObjectId { get; set; }
        public string RequestorName { get; set; } = "";
        public int ItemDistribution { get; set; }
    }

    public sealed class PartyJoinResultNetMsg
    {
        public int Response { get; set; }
    }

    public sealed class PartyMemberEntryNet
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

    public sealed class PartyMemberListNetMsg
    {
        public int LeaderObjectId { get; set; }
        public int PartyId { get; set; }
        public PartyMemberEntryNet[] Members { get; set; } = System.Array.Empty<PartyMemberEntryNet>();
    }
}
