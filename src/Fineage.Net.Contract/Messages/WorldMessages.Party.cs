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

    /// <summary>Client → zone <see cref="NetOpcode.PartyChangeLeaderRequest"/>: new leader's objectId.</summary>
    public sealed class PartyChangeLeaderRequestNetMsg
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
        /// <summary>Optional wire tail (after all rows); absent from older servers ⇒ 0.</summary>
        public int CurCp { get; set; }
        /// <summary>Optional wire tail (after all rows); absent from older servers ⇒ 0.</summary>
        public int MaxCp { get; set; }
    }

    /// <summary>Zone → client <see cref="NetOpcode.PartyMemberUpdate"/>.</summary>
    public sealed class PartyMemberUpdateNetMsg
    {
        public int ObjectId { get; set; }
        public int Level { get; set; }
        public int ClassId { get; set; }
        public int CurHp { get; set; }
        public int MaxHp { get; set; }
        public int CurMp { get; set; }
        public int MaxMp { get; set; }
        public int CurCp { get; set; }
        public int MaxCp { get; set; }
    }

    public sealed class PartyMemberPositionEntryNet
    {
        public int ObjectId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    /// <summary>Zone → client <see cref="NetOpcode.PartyMemberPosition"/>.</summary>
    public sealed class PartyMemberPositionNetMsg
    {
        public PartyMemberPositionEntryNet[] Members { get; set; } = System.Array.Empty<PartyMemberPositionEntryNet>();
    }

    public sealed class PartyMemberListNetMsg
    {
        public int LeaderObjectId { get; set; }
        public int PartyId { get; set; }
        public PartyMemberEntryNet[] Members { get; set; } = System.Array.Empty<PartyMemberEntryNet>();
    }
}
