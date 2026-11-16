namespace Fineage.Net.Contract
{
    /// <summary>
    /// <see cref="PledgeCommandNetMsg.Kind"/> values. One client opcode multiplexes the Interlude clan / alliance
    /// window requests (Mobius RequestGiveNickName, RequestPledgeSetMemberPowerGrade, RequestPledgePower,
    /// RequestPledgeReorganizeMember, RequestPledgeSetAcademyMaster, RequestStartPledgeWar, RequestStopPledgeWar,
    /// RequestSurrenderPledgeWar, RequestPledgeWarList, RequestSetPledgeCrest, RequestExSetPledgeCrestLarge,
    /// RequestPledgeCrest, RequestPledgeInfo, RequestPledgeMemberList, RequestJoinAlly, RequestAnswerJoinAlly,
    /// RequestWithdrawAlly, RequestOustAlly, RequestDismissAlly, RequestSetAllyCrest, RequestAllyInfo, auction bypasses).
    /// </summary>
    public static class PledgeCommandKind
    {
        /// <summary>TargetObjectId = member, Text = title.</summary>
        public const int SetTitle = 1;
        /// <summary>Name = member name, IntArg1 = grade 1..9.</summary>
        public const int SetMemberPowerGrade = 2;
        /// <summary>IntArg1 = grade, IntArg2 = privilege mask.</summary>
        public const int SetRankPrivileges = 3;
        /// <summary>IntArg1 = grade.</summary>
        public const int RequestRankPrivileges = 4;
        /// <summary>Name = member, IntArg1 = new pledge type, Text = member to swap with (optional).</summary>
        public const int ReorganizeMember = 5;
        /// <summary>Name = academy member, Text = sponsor name, IntArg1 = 1 set / 0 clear.</summary>
        public const int SetSponsor = 6;
        /// <summary>Name = target clan name.</summary>
        public const int DeclareWar = 7;
        public const int StopWar = 8;
        public const int SurrenderWar = 9;
        public const int RequestWarList = 10;
        /// <summary>Blob = crest data (empty deletes).</summary>
        public const int SetCrest = 11;
        public const int SetLargeCrest = 12;
        /// <summary>IntArg1 = crest id, IntArg2 = kind.</summary>
        public const int RequestCrest = 13;
        /// <summary>IntArg1 = clan id.</summary>
        public const int RequestPledgeInfo = 14;
        public const int RequestMemberList = 15;
        /// <summary>TargetObjectId = clan leader to invite.</summary>
        public const int AllyInvite = 20;
        /// <summary>IntArg1 = 1 accept / 0 decline.</summary>
        public const int AllyAnswer = 21;
        public const int AllyLeave = 22;
        /// <summary>Name = clan name.</summary>
        public const int AllyExpel = 23;
        public const int AllyDissolve = 24;
        public const int SetAllyCrest = 25;
        public const int RequestAllyInfo = 26;
        public const int HallList = 30;
        /// <summary>IntArg1 = hall id, Amount = bid.</summary>
        public const int HallBid = 31;
        /// <summary>IntArg1 = hall id.</summary>
        public const int HallCancelBid = 32;
    }

    public sealed class PledgeCommandNetMsg
    {
        public int Kind { get; set; }
        public int TargetObjectId { get; set; }
        public int IntArg1 { get; set; }
        public int IntArg2 { get; set; }
        public long Amount { get; set; }
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
        public byte[] Blob { get; set; } = System.Array.Empty<byte>();
    }

    public sealed class SubPledgeNet
    {
        public int Type { get; set; }
        public string Name { get; set; } = "";
        public int LeaderObjectId { get; set; }
    }

    public sealed class ClanSkillNet
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
    }

    public sealed class PledgeMemberExtraNet
    {
        public int PowerGrade { get; set; }
        public int SponsorObjectId { get; set; }
        public int ApprenticeObjectId { get; set; }
        public int PledgeClass { get; set; }
    }

    /// <summary>Kind 0 pledge, 1 large, 2 ally. Empty data = no such crest.</summary>
    public sealed class CrestDataNetMsg
    {
        public int Kind { get; set; }
        public int CrestId { get; set; }
        public byte[] Data { get; set; } = System.Array.Empty<byte>();
    }

    public sealed class PledgeWarNet
    {
        public int ClanId { get; set; }
        public string Name { get; set; } = "";
        public bool Mutual { get; set; }
    }

    public sealed class PledgeWarListNetMsg
    {
        public PledgeWarNet[] Declared { get; set; } = System.Array.Empty<PledgeWarNet>();
        public PledgeWarNet[] UnderAttack { get; set; } = System.Array.Empty<PledgeWarNet>();
    }

    public sealed class RelationChangedNetMsg
    {
        public int ObjectId { get; set; }
        public int Relation { get; set; }
        public bool AutoAttackable { get; set; }
        public int Karma { get; set; }
        public byte PvpFlag { get; set; }
    }

    public sealed class PledgeInfoNetMsg
    {
        public int ClanId { get; set; }
        public string Name { get; set; } = "";
        public int CrestId { get; set; }
        public int AllyId { get; set; }
        public string AllyName { get; set; } = "";
        public int AllyCrestId { get; set; }
    }

    public sealed class AllyClanNet
    {
        public int ClanId { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public string LeaderName { get; set; } = "";
        public int MemberCount { get; set; }
        public int OnlineCount { get; set; }
        public int CrestId { get; set; }
    }

    public sealed class AllyInfoNetMsg
    {
        public int AllyId { get; set; }
        public string Name { get; set; } = "";
        public int CrestId { get; set; }
        public int LeaderClanId { get; set; }
        public AllyClanNet[] Clans { get; set; } = System.Array.Empty<AllyClanNet>();
    }

    public sealed class AllyAskJoinNetMsg
    {
        public int RequestorObjectId { get; set; }
        public string RequestorName { get; set; } = "";
        public string AllyName { get; set; } = "";
    }

    public sealed class ClanHallNet
    {
        public int HallId { get; set; }
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public int Grade { get; set; }
        public long Lease { get; set; }
        public int OwnerClanId { get; set; }
        public string OwnerClanName { get; set; } = "";
        public long AuctionEndUnixMs { get; set; }
        public long StartingBid { get; set; }
        public long HighestBid { get; set; }
        public int BidCount { get; set; }
        public long OwnBid { get; set; }
    }

    public sealed class ClanHallListNetMsg
    {
        public ClanHallNet[] Halls { get; set; } = System.Array.Empty<ClanHallNet>();
    }
}
