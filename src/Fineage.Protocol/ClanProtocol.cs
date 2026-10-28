namespace Fineage.Protocol;

/// <summary>
/// Clan lifecycle wire and the Interlude defaults this server uses.
/// Layout: <c>docs/clan-v2-protocol.md</c>.
/// </summary>
/// <remarks>
/// Numbers and checks follow L2J Mobius (com.l2jserver lineage), not its class layout:
/// <c>ClanTable.createClan</c> (level 10, one clan, alphanumeric name length 2–16, unique ignoring case),
/// <c>L2Clan.getMaxNrOfMembers</c> (level 0 main pledge holds 10),
/// <c>L2Clan.checkClanJoinCondition</c>, <c>RequestJoinPledge</c>, <c>RequestAnswerJoinPledge</c>,
/// <c>RequestWithdrawalPledge</c>, <c>RequestOustPledgeMember</c>, <c>L2Clan.removeClanMember</c>,
/// <c>L2Request.REQUEST_TIMEOUT</c> (15 seconds), and <c>Character.properties</c>
/// <c>DaysBeforeJoinAClan</c> / <c>DaysBeforeCreateAClan</c> / <c>DaysToPassToDissolveAClan</c>
/// loaded as <c>Config.ALT_CLAN_JOIN_DAYS</c> = 1, <c>ALT_CLAN_CREATE_DAYS</c> = 10,
/// <c>ALT_CLAN_DISSOLVE_DAYS</c> = 7. A day is 86_400_000 ms.
/// Dissolution waits those 7 days (<c>L2Clan.dissolvingExpiryTime</c>, <c>ClanTable.scheduleRemoveClan</c>
/// / <c>destroyClan</c>). War, castle, and clan-hall blocks are not applied; this server has none.
/// Invite range is interest visibility (<c>YOU_HAVE_INVITED_THE_WRONG_TARGET</c> when the player is not visible).
/// <c>RequestJoinPledge</c> has no extra radius. Crests are not implemented; crest id is 0.
/// Server replies use <see cref="BandFirst"/>–<see cref="BandLast"/>, clear of chat 1040–1049,
/// party 1027–1029, and lobby 1020–1026 / 1030–1033. Client requests are 32–38, clear of equip 15–17,
/// party 18–19 / 26–27, admin spoil/sweep 30–31, and login 20–25.
/// </remarks>
public static class ClanProtocol
{
    public const ushort BandFirst = 1050;
    public const ushort BandLast = 1059;

    public const int MinCreateLevel = 10;
    public const int MinNameLength = 2;
    public const int MaxNameLength = 16;

    /// <summary><c>ALT_CLAN_JOIN_DAYS</c>. Leave and dismiss penalties, and the clan's post-dismiss recruit lock.</summary>
    public const int JoinDays = 1;

    /// <summary><c>ALT_CLAN_CREATE_DAYS</c>. Leader cannot create another clan for this long after the old one is destroyed.</summary>
    public const int CreateDays = 10;

    /// <summary><c>ALT_CLAN_DISSOLVE_DAYS</c>. Dissolution waits this long. Cancel is allowed until it elapses.</summary>
    public const int DissolveDays = 7;

    public const long DayMillis = 86_400_000L;

    /// <summary><c>L2Request.REQUEST_TIMEOUT</c> in seconds.</summary>
    public const int InviteTimeoutSeconds = 15;

    /// <summary>Attack stance that feeds leave/dismiss combat checks. 15 seconds, refreshed on a landed attack.</summary>
    public const int CombatStanceMillis = 15_000;

    public const int MainPledge = 0;
    public const int CrestId = 0;

    public const string Level = "clan_level";
    public const string Member = "clan_member";
    public const string CreatePenalty = "clan_create_penalty";
    public const string Name = "clan_name";
    public const string NameLength = "clan_name_length";
    public const string NameTaken = "clan_name_taken";
    public const string None = "clan_none";
    public const string Leader = "clan_leader";
    public const string LeaderLeave = "clan_leader_leave";
    public const string Pledge = "clan_pledge";
    public const string Self = "clan_self";
    public const string Target = "clan_target";
    public const string TargetClan = "clan_target_clan";
    public const string Busy = "clan_busy";
    public const string Waiting = "clan_waiting";
    public const string RecruitPenalty = "clan_recruit_penalty";
    public const string JoinPenalty = "clan_join_penalty";
    public const string Full = "clan_full";
    public const string Invite = "clan_invite";
    public const string Combat = "clan_combat";
    public const string DismissSelf = "clan_dismiss_self";
    public const string Dissolving = "clan_dissolving";
    public const string NotDissolving = "clan_not_dissolving";
    public const string Store = "clan_store";

    /// <summary>System message ids on <see cref="MsgType.SystemMessage"/>. Chat uses 1–3. 0 is plain text.</summary>
    public const int Created = 100;
    public const int Entered = 101;
    public const int MemberJoined = 102;
    public const int InviteDeclinedSelf = 103;
    public const int InviteDeclinedRequestor = 104;
    public const int Withdrawn = 105;
    public const int MemberWithdrawn = 106;
    public const int JoinWait = 107;
    public const int Expelled = 108;
    public const int ExpelledLeader = 109;
    public const int RecruitWait = 110;
    public const int Dismissed = 111;
    public const int DissolveRequested = 112;
    public const int DissolveCancelled = 113;
    public const int Dispersed = 114;
}
