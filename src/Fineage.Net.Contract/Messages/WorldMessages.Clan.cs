namespace Fineage.Net.Contract
{
    public sealed class ClanCreateRequestNetMsg
    {
        public string Name { get; set; } = "";
    }

    /// <summary>Client → zone: Mobius <c>RequestJoinPledge</c> (the leader's object id).</summary>

    public sealed class ClanInviteRequestNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    /// <summary>Client → zone: Mobius <c>RequestAnswerJoinPledge</c>. Response 0 declines.</summary>

    public sealed class ClanAnswerNetMsg
    {
        public int RequestorObjectId { get; set; }
        public int Response { get; set; }
    }

    /// <summary>Client → zone: Mobius <c>RequestWithdrawalPledge</c>. Empty body.</summary>

    public sealed class ClanLeaveRequestNetMsg
    {
    }

    /// <summary>Client → zone: Mobius <c>RequestOustPledgeMember</c> (the member's object id).</summary>

    public sealed class ClanKickRequestNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    /// <summary>
    /// Zone → client: Mobius <c>PledgeShowMemberListAll</c> / <c>PledgeStatusChanged</c>. Clan id 0 clears the pledge.
    /// </summary>

    public sealed class PledgeStatusNetMsg
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

    /// <summary>Mobius <c>PledgeShowMemberListAll</c> row.</summary>

    public sealed class PledgeMemberNet
    {
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
        public bool Online { get; set; }
        public int PledgeType { get; set; }
    }

    /// <summary>Zone → client: Mobius <c>PledgeShowMemberListAll</c> (the roster after any membership change).</summary>

    public sealed class PledgeMemberListNetMsg
    {
        public int ClanId { get; set; }
        public int LeaderObjectId { get; set; }
        public PledgeMemberNet[] Members { get; set; } = System.Array.Empty<PledgeMemberNet>();
    }

    /// <summary>Zone → client: Mobius <c>AskJoinPledge</c> (an invite arrived).</summary>

    public sealed class ClanAskJoinNetMsg
    {
        public int RequestorObjectId { get; set; }
        public string RequestorName { get; set; } = "";
        public int ClanId { get; set; }
        public string ClanName { get; set; } = "";
        public int PledgeType { get; set; }
    }

    /// <summary>Zone → client: Mobius <c>JoinPledge</c>. Response 0 is a decline.</summary>

    public sealed class ClanJoinResultNetMsg
    {
        public int Response { get; set; }
    }

    /// <summary>
    /// Mobius <c>ChangeWaitType</c>: sit/stand. Interlude also carries the actor position, which the client uses to
    /// settle the pawn before it plays the sitting animation.
    /// </summary>
}
