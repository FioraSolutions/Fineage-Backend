using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Block D alliances over Net (Mobius CT_0 rules): create at clan level 5, invite / accept, the 3-clan limit,
/// war refusal, leave / expel / dissolve penalties, the 8x12 crest, alliance info, relation bits and the $ chat.
/// </summary>
public sealed class NetAllianceTests
{
    static readonly CancellationToken Ct = default;

    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    sealed record Clan(int Id, ZoneSession Leader, ZoneEntity LeaderE, ZoneSession Member, ZoneEntity MemberE);

    static async Task<Clan> ClanAsync(NetTestKit kit, string name, int level = 5)
    {
        var (leader, leaderE) = await kit.EnterAsync(level: 40);
        Assert.Null(await kit.World.TryClanCreateAsync(leader, new ClanCreateRequestMsg { Name = name }, kit.Now, Ct));
        int id = leaderE.ClanId;
        if (level > 0)
            Assert.True(kit.World.SetClanLevelForTest(id, level));
        var (member, memberE) = await kit.EnterAsync(level: 20);
        Assert.Null(await kit.World.TryClanInviteAsync(leader, new ClanInviteRequestMsg { TargetObjectId = memberE.ObjectId }, kit.Now, Ct));
        Assert.Null(await kit.World.TryClanAnswerAsync(member,
            new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderE.ObjectId, Response = 1 }, kit.Now, Ct));
        return new Clan(id, leader, leaderE, member, memberE);
    }

    static Task<string?> Cmd(NetTestKit kit, ZoneSession s, PledgeCommandNetMsg c) =>
        kit.World.TryPledgeCommandAsync(s, NetCodec.DecodePledgeCommand(NetCodec.EncodePledgeCommand(c)), kit.Now, Ct);

    static async Task<string?> JoinAsync(NetTestKit kit, Clan ally, Clan target)
    {
        string? invite = await Cmd(kit, ally.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyInvite, TargetObjectId = target.LeaderE.ObjectId });
        if (invite != null)
            return invite;
        return await Cmd(kit, target.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyAnswer, IntArg1 = 1 });
    }

    static List<int> Lines(NetBridgeRecorder rec, int objectId) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Where(a => ((IReadOnlyList<int>)a[0]!).Contains(objectId))
            .Select(a => ((SystemMessageMsg)a[1]!).Id).ToList();

    [Fact]
    public async Task Create_needs_a_level_5_leader_a_valid_unique_name_and_shows_on_members()
    {
        var (kit, rec) = World();
        var low = await ClanAsync(kit, "Low", level: 4);
        Assert.Equal(PledgeProtocol.Conditions, kit.World.CreateAllyForTest(low.Leader, "Lows"));
        Assert.Contains(549, Lines(rec, low.LeaderE.ObjectId));

        var a = await ClanAsync(kit, "Alpha");
        Assert.Equal(PledgeProtocol.Denied, kit.World.CreateAllyForTest(a.Member, "Wolves"));
        Assert.Equal(ClanProtocol.Name, kit.World.CreateAllyForTest(a.Leader, "Bad name!"));
        Assert.Equal(ClanProtocol.NameLength, kit.World.CreateAllyForTest(a.Leader, "W"));
        Assert.Null(kit.World.CreateAllyForTest(a.Leader, "Wolves"));
        Assert.Equal((a.Id, "Wolves"), (kit.World.AllyOfForTest(a.Id).AllyId, kit.World.AllyOfForTest(a.Id).Name));
        Assert.Equal(a.Id, a.MemberE.AllyId);
        Assert.Equal(PledgeProtocol.Conditions, kit.World.CreateAllyForTest(a.Leader, "Again"));

        var b = await ClanAsync(kit, "Beta");
        Assert.Equal(ClanProtocol.NameTaken, kit.World.CreateAllyForTest(b.Leader, "wolves"));
    }

    [Fact]
    public async Task Invite_accept_limit_war_refusal_info_relation_and_chat()
    {
        var (kit, rec) = World();
        var a = await ClanAsync(kit, "Alpha");
        var b = await ClanAsync(kit, "Beta", level: 0);
        var c = await ClanAsync(kit, "Gamma", level: 0);
        var d = await ClanAsync(kit, "Delta", level: 0);
        Assert.Null(kit.World.CreateAllyForTest(a.Leader, "Wolves"));

        // Only the alliance leader invites, and only clan leaders.
        Assert.Equal(PledgeProtocol.Denied, await Cmd(kit, a.Member, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyInvite, TargetObjectId = b.LeaderE.ObjectId }));
        Assert.Equal(ClanProtocol.Leader, await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyInvite, TargetObjectId = b.MemberE.ObjectId }));

        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyInvite, TargetObjectId = b.LeaderE.ObjectId }));
        var ask = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutAllyAskJoin)));
        Assert.Equal("Wolves", ((AllyAskJoinNetMsg)ask[1]!).AllyName);
        Assert.Null(await Cmd(kit, b.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyAnswer, IntArg1 = 1 }));
        Assert.Equal(a.Id, kit.World.AllyOfForTest(b.Id).AllyId);
        Assert.Equal(a.Id, b.MemberE.AllyId);
        Assert.Contains(517, Lines(rec, b.LeaderE.ObjectId));

        // War with a would-be member refuses the invite (469).
        Assert.True(kit.World.SetMutualWarForTest(a.Id, d.Id));
        Assert.Equal(PledgeProtocol.AlreadyAtWar, await JoinAsync(kit, a, d));
        Assert.Contains(469, Lines(rec, a.LeaderE.ObjectId));

        Assert.Null(await JoinAsync(kit, a, c));
        var e = await ClanAsync(kit, "Epsilon", level: 0);
        Assert.Equal(ClanProtocol.Full, await JoinAsync(kit, a, e));
        Assert.Contains(466, Lines(rec, a.LeaderE.ObjectId));

        // Alliance info lists the three clans.
        rec.Calls.Clear();
        Assert.Null(await Cmd(kit, c.Member, new PledgeCommandNetMsg { Kind = PledgeCommandKind.RequestAllyInfo }));
        var info = (AllyInfoNetMsg)Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutAllyInfo)))[1]!;
        Assert.Equal(("Wolves", a.Id, 3), (info.Name, info.LeaderClanId, info.Clans.Length));
        Assert.Equal(new[] { a.Id, b.Id, c.Id }.Order(), info.Clans.Select(x => x.ClanId).Order());

        // Allied players carry the ally relation bit; outsiders do not.
        Assert.NotEqual(0, kit.World.RelationOfForTest(b.MemberE, c.MemberE) & PledgeProtocol.RelationAlly);
        Assert.Equal(0, kit.World.RelationOfForTest(b.MemberE, e.MemberE) & PledgeProtocol.RelationAlly);

        // $ chat reaches every online member of every allied clan, nobody else.
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(b.Member, "rally", ChatProtocol.Alliance, kit.Now, Ct));
        var heard = rec.Of(nameof(IWorldEventPublisher.FanoutCreatureSay))
            .SelectMany(x => (IReadOnlyList<int>)x[0]!).ToHashSet();
        foreach (var who in new[] { a.LeaderE, a.MemberE, b.LeaderE, b.MemberE, c.LeaderE, c.MemberE })
            Assert.Contains(who.ObjectId, heard);
        Assert.DoesNotContain(e.MemberE.ObjectId, heard);
        Assert.DoesNotContain(d.LeaderE.ObjectId, heard);
    }

    [Fact]
    public async Task Leave_expel_and_dissolve_apply_their_penalties()
    {
        var (kit, rec) = World();
        var a = await ClanAsync(kit, "Alpha");
        var b = await ClanAsync(kit, "Beta", level: 0);
        var c = await ClanAsync(kit, "Gamma", level: 0);
        Assert.Null(kit.World.CreateAllyForTest(a.Leader, "Wolves"));
        Assert.Null(await JoinAsync(kit, a, b));
        Assert.Null(await JoinAsync(kit, a, c));

        // The alliance leader cannot leave; a member's leader can, with a 1-day re-join penalty.
        Assert.Equal(ClanProtocol.LeaderLeave, await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyLeave }));
        Assert.Equal(PledgeProtocol.Denied, await Cmd(kit, b.Member, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyLeave }));
        Assert.Null(await Cmd(kit, b.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyLeave }));
        Assert.Equal((0, PledgeProtocol.AllyPenaltyClanLeaved), (kit.World.AllyOfForTest(b.Id).AllyId, kit.World.AllyOfForTest(b.Id).PenaltyType));
        Assert.Equal(0, b.MemberE.AllyId);
        Assert.Equal(ClanProtocol.JoinPenalty, await JoinAsync(kit, a, b));
        Assert.Contains(761, Lines(rec, a.LeaderE.ObjectId));

        // Expel: the clan waits a day, and the leader may not accept anyone for a day.
        Assert.Equal(PledgeProtocol.NoSuchClan, await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyExpel, Name = "Nobody" }));
        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyExpel, Name = "Gamma" }));
        Assert.Equal((0, PledgeProtocol.AllyPenaltyClanDismissed), (kit.World.AllyOfForTest(c.Id).AllyId, kit.World.AllyOfForTest(c.Id).PenaltyType));
        Assert.Equal(PledgeProtocol.AllyPenaltyDismissClan, kit.World.AllyOfForTest(a.Id).PenaltyType);
        var d = await ClanAsync(kit, "Delta", level: 0);
        Assert.Equal(PledgeProtocol.Conditions, await JoinAsync(kit, a, d));
        Assert.Contains(467, Lines(rec, a.LeaderE.ObjectId));

        // A day later every penalty is gone.
        kit.Now = kit.Now.AddDays(1).AddMinutes(1);
        Assert.Null(await JoinAsync(kit, a, b));
        Assert.Null(await JoinAsync(kit, a, c));

        // Allied clans cannot be dissolved; dissolving the alliance blocks a new one for 10 days.
        Assert.Equal(PledgeProtocol.Conditions, await kit.World.TryClanDissolveAsync(b.Leader, kit.Now, Ct));
        Assert.Contains(554, Lines(rec, b.LeaderE.ObjectId));
        Assert.Equal(PledgeProtocol.Denied, await Cmd(kit, b.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyDissolve }));
        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.AllyDissolve }));
        foreach (var clan in new[] { a, b, c })
            Assert.Equal(0, kit.World.AllyOfForTest(clan.Id).AllyId);
        Assert.Contains(523, Lines(rec, b.MemberE.ObjectId));
        Assert.Equal(PledgeProtocol.Conditions, kit.World.CreateAllyForTest(a.Leader, "Wolves"));
        Assert.Contains(505, Lines(rec, a.LeaderE.ObjectId));
        kit.Now = kit.Now.AddDays(10).AddMinutes(1);
        Assert.Null(kit.World.CreateAllyForTest(a.Leader, "Wolves"));
    }

    [Fact]
    public async Task Ally_crest_is_leader_only_8x12_and_reaches_every_member_clan()
    {
        var (kit, rec) = World();
        var a = await ClanAsync(kit, "Alpha");
        var b = await ClanAsync(kit, "Beta", level: 0);
        Assert.Null(kit.World.CreateAllyForTest(a.Leader, "Wolves"));
        Assert.Null(await JoinAsync(kit, a, b));

        Assert.Equal(PledgeProtocol.CrestSize, await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetAllyCrest, Blob = new byte[193] }));
        Assert.Contains(476, Lines(rec, a.LeaderE.ObjectId));
        Assert.Equal(PledgeProtocol.Denied, await Cmd(kit, b.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetAllyCrest, Blob = new byte[192] }));

        var data = Enumerable.Range(0, 192).Select(i => (byte)i).ToArray();
        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetAllyCrest, Blob = data }));
        int crestId = kit.World.AllyOfForTest(a.Id).CrestId;
        Assert.NotEqual(0, crestId);
        Assert.Equal(crestId, kit.World.AllyOfForTest(b.Id).CrestId);
        Assert.Equal(crestId, b.MemberE.AllyCrestId);
        Assert.Equal(data, kit.World.CrestBytesForTest(crestId));

        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetAllyCrest, Blob = [] }));
        Assert.Equal(0, kit.World.AllyOfForTest(b.Id).CrestId);
        Assert.Null(kit.World.CrestBytesForTest(crestId));
    }

    [Fact]
    public async Task Alliance_survives_restart()
    {
        var (kit, _) = World();
        var a = await ClanAsync(kit, "Alpha");
        var b = await ClanAsync(kit, "Beta", level: 0);
        Assert.Null(kit.World.CreateAllyForTest(a.Leader, "Wolves"));
        Assert.Null(await JoinAsync(kit, a, b));

        var again = new NetTestKit(store: kit.Store);
        Assert.Equal((a.Id, "Wolves"), (again.World.AllyOfForTest(b.Id).AllyId, again.World.AllyOfForTest(b.Id).Name));
    }
}
