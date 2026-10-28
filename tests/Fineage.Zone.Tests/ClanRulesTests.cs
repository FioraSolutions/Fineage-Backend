using Fineage.Protocol;
using Fineage.Zone.Clan;
using Xunit;

namespace Fineage.Zone.Tests;

public class ClanRulesTests
{
    const long Now = 1_700_000_000_000;

    [Fact]
    public void Create_requires_level_10_and_no_existing_clan()
    {
        Assert.Equal(ClanProtocol.Level, ClanRules.CreateBlock(9, 0, 0, Now, "Fine", false));
        Assert.Null(ClanRules.CreateBlock(10, 0, 0, Now, "Fine", false));
        Assert.Equal(ClanProtocol.Member, ClanRules.CreateBlock(10, 4, 0, Now, "Fine", false));
    }

    [Fact]
    public void Create_penalty_uses_ALT_CLAN_CREATE_DAYS_and_expires_on_the_timestamp()
    {
        long expiry = ClanRules.CreatePenaltyExpiry(Now);
        Assert.Equal(Now + ClanProtocol.CreateDays * ClanProtocol.DayMillis, expiry);
        Assert.Equal(10, ClanProtocol.CreateDays);
        Assert.Equal(86_400_000L, ClanProtocol.DayMillis);
        Assert.Equal(ClanProtocol.CreatePenalty, ClanRules.CreateBlock(10, 0, expiry, Now, "Fine", false));
        Assert.Null(ClanRules.CreateBlock(10, 0, expiry, expiry, "Fine", false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    [InlineData(" has space")]
    [InlineData("bad-name")]
    [InlineData("!!!!!!!!!!!!!!!")]
    public void Clan_name_rejects_non_alphanumeric_or_shorter_than_two(string? name)
    {
        Assert.Equal(ClanProtocol.Name, ClanRules.NameError(name));
        Assert.Equal(ClanProtocol.Name, ClanRules.CreateBlock(10, 0, 0, Now, name, false));
    }

    [Fact]
    public void Clan_name_accepts_two_to_sixteen_letters_or_digits_including_unicode()
    {
        Assert.Null(ClanRules.NameError("12"));
        Assert.Null(ClanRules.NameError("аб"));
        Assert.Null(ClanRules.NameError(new string('A', 16)));
        Assert.Equal(ClanProtocol.NameLength, ClanRules.NameError(new string('A', 17)));
        Assert.Equal(ClanProtocol.Name, ClanRules.NameError(new string('!', 17)));
        Assert.Equal(ClanProtocol.NameTaken, ClanRules.CreateBlock(10, 0, 0, Now, "Fine", true));
    }

    [Theory]
    [InlineData(0, 0, 10)]
    [InlineData(1, 0, 15)]
    [InlineData(2, 0, 20)]
    [InlineData(3, 0, 30)]
    [InlineData(4, 0, 40)]
    [InlineData(8, 0, 40)]
    [InlineData(0, -1, 20)]
    [InlineData(11, 100, 30)]
    [InlineData(5, 200, 20)]
    [InlineData(9, 1001, 25)]
    [InlineData(0, 1002, 10)]
    [InlineData(0, 3, 0)]
    public void Member_limit_matches_Mobius_pledge_table(int level, int pledge, int limit)
    {
        Assert.Equal(limit, ClanRules.MemberLimit(level, pledge));
    }

    [Fact]
    public void Level_0_clan_is_full_at_10_members()
    {
        Assert.Equal(10, ClanRules.MemberLimit(0, ClanProtocol.MainPledge));
        Assert.Null(Invite(memberCount: 9));
        Assert.Equal(ClanProtocol.Full, Invite(memberCount: 10));
    }

    [Fact]
    public void Invite_checks_follow_Mobius_order()
    {
        Assert.Equal(ClanProtocol.None, Invite(inClan: false));
        Assert.Equal(ClanProtocol.Leader, Invite(leader: false));
        Assert.Equal(ClanProtocol.Pledge, Invite(pledgeType: -1));
        Assert.Equal(ClanProtocol.Self, Invite(self: true));
        Assert.Equal(ClanProtocol.Target, Invite(targetVisible: false));
        Assert.Equal(ClanProtocol.Busy, Invite(targetBusy: true));
        Assert.Equal(ClanProtocol.Waiting, Invite(selfBusy: true));
        Assert.Equal(ClanProtocol.RecruitPenalty, Invite(clanRecruitPenalty: true));
        Assert.Equal(ClanProtocol.TargetClan, Invite(targetInClan: true));
        Assert.Equal(ClanProtocol.JoinPenalty, Invite(targetJoinPenalty: true));
    }

    [Fact]
    public void Join_penalty_is_one_day_and_inactive_when_the_timestamp_is_reached()
    {
        long expiry = ClanRules.JoinPenaltyExpiry(Now);
        Assert.Equal(Now + ClanProtocol.DayMillis, expiry);
        Assert.Equal(1, ClanProtocol.JoinDays);
        Assert.True(ClanRules.PenaltyActive(expiry, Now));
        Assert.False(ClanRules.PenaltyActive(expiry, expiry));
        Assert.False(ClanRules.PenaltyActive(0, Now));
    }

    [Fact]
    public void Invite_times_out_after_15_seconds()
    {
        long deadline = ClanRules.InviteDeadline(Now);
        Assert.Equal(15, ClanProtocol.InviteTimeoutSeconds);
        Assert.Equal(Now + 15_000, deadline);
        Assert.False(ClanRules.InviteExpired(deadline, deadline - 1));
        Assert.True(ClanRules.InviteExpired(deadline, deadline));
    }

    [Fact]
    public void Leave_kick_and_dissolve_are_leader_gated_and_combat_blocks_membership_changes()
    {
        Assert.Equal(ClanProtocol.None, ClanRules.LeaveBlock(false, false, false));
        Assert.Equal(ClanProtocol.LeaderLeave, ClanRules.LeaveBlock(true, true, false));
        Assert.Equal(ClanProtocol.Combat, ClanRules.LeaveBlock(true, false, true));
        Assert.Null(ClanRules.LeaveBlock(true, false, false));

        Assert.Equal(ClanProtocol.None, ClanRules.KickBlock(false, true, false, true, false));
        Assert.Equal(ClanProtocol.Leader, ClanRules.KickBlock(true, false, false, true, false));
        Assert.Equal(ClanProtocol.DismissSelf, ClanRules.KickBlock(true, true, true, true, false));
        Assert.Equal(ClanProtocol.Target, ClanRules.KickBlock(true, true, false, false, false));
        Assert.Equal(ClanProtocol.Combat, ClanRules.KickBlock(true, true, false, true, true));
        Assert.Null(ClanRules.KickBlock(true, true, false, true, false));

        Assert.Equal(15_000, ClanProtocol.CombatStanceMillis);
        Assert.True(ClanRules.IsInCombat(Now + 15_000, Now));
        Assert.False(ClanRules.IsInCombat(Now, Now));
    }

    [Fact]
    public void Dissolve_waits_ALT_CLAN_DISSOLVE_DAYS_and_can_be_cancelled_until_then()
    {
        long expiry = ClanRules.DissolveExpiry(Now);
        Assert.Equal(7, ClanProtocol.DissolveDays);
        Assert.Equal(Now + 7 * ClanProtocol.DayMillis, expiry);
        Assert.False(ClanRules.DissolveDue(0, Now));
        Assert.False(ClanRules.DissolveDue(expiry, expiry - 1));
        Assert.True(ClanRules.DissolveDue(expiry, expiry));

        Assert.Equal(ClanProtocol.None, ClanRules.DissolveBlock(false, true, false));
        Assert.Equal(ClanProtocol.Leader, ClanRules.DissolveBlock(true, false, false));
        Assert.Equal(ClanProtocol.Dissolving, ClanRules.DissolveBlock(true, true, true));
        Assert.Null(ClanRules.DissolveBlock(true, true, false));

        Assert.Equal(ClanProtocol.NotDissolving, ClanRules.CancelDissolveBlock(true, true, false));
        Assert.Null(ClanRules.CancelDissolveBlock(true, true, true));
    }

    static string? Invite(
        bool inClan = true,
        bool leader = true,
        int pledgeType = 0,
        bool self = false,
        bool targetVisible = true,
        bool targetBusy = false,
        bool selfBusy = false,
        bool clanRecruitPenalty = false,
        bool targetInClan = false,
        bool targetJoinPenalty = false,
        int memberCount = 1) =>
        ClanRules.InviteBlock(
            inClan,
            leader,
            pledgeType,
            self,
            targetVisible,
            targetBusy,
            selfBusy,
            clanRecruitPenalty,
            targetInClan,
            targetJoinPenalty,
            memberCount,
            clanLevel: 0);
}
