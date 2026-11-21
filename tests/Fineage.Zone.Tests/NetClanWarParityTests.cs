using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius CT_0 clan wars: cooldown, death XP, mutual-war PvP/reputation, relation bits, surrender.</summary>
public sealed class NetClanWarParityTests
{
    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static async Task<(int ClanId, ZoneSession Leader, ZoneEntity LeaderE, ZoneEntity Member)> ClanAsync(NetTestKit kit, string name)
    {
        var (leader, leaderE) = await kit.EnterAsync(level: 40);
        Assert.Null(await kit.World.TryClanCreateAsync(leader, new ClanCreateRequestMsg { Name = name }, kit.Now, default));
        int clanId = leaderE.ClanId;
        Assert.True(kit.World.SetClanLevelForTest(clanId, 5));
        for (int i = 0; i < 14; i++)
        {
            var (m, _) = await kit.EnterAsync(level: 20);
            Assert.Null(await kit.World.TryClanInviteAsync(leader, new ClanInviteRequestMsg { TargetObjectId = m.ObjectId }, kit.Now, default));
            Assert.Null(await kit.World.TryClanAnswerAsync(m,
                new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderE.ObjectId, Response = 1 }, kit.Now, default));
        }
        var (member, memberE) = await kit.EnterAsync(level: 30);
        Assert.Null(await kit.World.TryClanInviteAsync(leader, new ClanInviteRequestMsg { TargetObjectId = memberE.ObjectId }, kit.Now, default));
        Assert.Null(await kit.World.TryClanAnswerAsync(member,
            new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderE.ObjectId, Response = 1 }, kit.Now, default));
        return (clanId, leader, leaderE, memberE);
    }

    static Task<string?> Cmd(NetTestKit kit, ZoneSession s, PledgeCommandNetMsg c) =>
        kit.World.TryPledgeCommandAsync(s, NetCodec.DecodePledgeCommand(NetCodec.EncodePledgeCommand(c)), kit.Now, default);

    static List<int> Lines(NetBridgeRecorder rec, int objectId) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Where(a => ((IReadOnlyList<int>)a[0]!).Contains(objectId))
            .Select(a => ((SystemMessageMsg)a[1]!).Id).ToList();

    [Fact]
    public async Task Stopping_a_war_blocks_redeclaration_for_five_days()
    {
        var (kit, rec) = World();
        var a = await ClanAsync(kit, "Alpha");
        var b = await ClanAsync(kit, "Beta");
        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.DeclareWar, Name = "Beta" }));
        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.StopWar, Name = "Beta" }));
        Assert.Equal(PledgeProtocol.AlreadyAtWar,
            await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.DeclareWar, Name = "Beta" }));
        Assert.Contains(628, Lines(rec, a.LeaderE.ObjectId));

        kit.AdvanceToTickCovering(kit.Now.ToUnixTimeMilliseconds() + PledgeProtocol.WarCooldownMillis + 1);
        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.DeclareWar, Name = "Beta" }));
    }

    [Fact]
    public async Task Declaring_clan_members_take_quarter_death_exp_from_enemy_clan_killers()
    {
        var (kit, _) = World();
        var a = await ClanAsync(kit, "Alpha");
        var b = await ClanAsync(kit, "Beta");
        var c = await ClanAsync(kit, "Gamma");
        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.DeclareWar, Name = "Beta" }));

        Assert.Equal(PledgeProtocol.WarDeathExpShare, kit.World.ClanWarDeathExpShareForTest(a.Member, b.Member));
        Assert.Equal(1d, kit.World.ClanWarDeathExpShareForTest(a.Member, c.Member));
    }

    [Fact]
    public async Task Mutual_war_kill_counts_as_pvp_without_pk_or_karma_and_shifts_reputation()
    {
        var (kit, _) = World();
        var a = await ClanAsync(kit, "Alpha");
        var b = await ClanAsync(kit, "Beta");
        Assert.True(kit.World.SetMutualWarForTest(a.ClanId, b.ClanId));
        Assert.True(kit.World.SetClanReputationForTest(a.ClanId, 100));
        Assert.True(kit.World.SetClanReputationForTest(b.ClanId, 20));

        int pvpBefore = b.LeaderE.PvpKills;
        int karmaBefore = b.LeaderE.Karma;
        kit.World.KillPlayerByNpcForTest(a.Member, b.LeaderE);

        Assert.Equal(karmaBefore, b.LeaderE.Karma);
        Assert.Equal(pvpBefore + 1, b.LeaderE.PvpKills);
        Assert.Equal(99, kit.Store.FindClan(a.ClanId)!.Details!.Reputation);
        Assert.Equal(21, kit.Store.FindClan(b.ClanId)!.Details!.Reputation);
    }

    [Fact]
    public async Task War_relation_bits_show_one_sided_then_mutual()
    {
        var (kit, _) = World();
        var a = await ClanAsync(kit, "Alpha");
        var b = await ClanAsync(kit, "Beta");
        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.DeclareWar, Name = "Beta" }));

        int oneSided = kit.World.RelationOfForTest(b.Member, a.Member);
        Assert.NotEqual(0, oneSided & PledgeProtocol.RelationOneSidedWar);
        Assert.Equal(0, oneSided & PledgeProtocol.RelationMutualWar);

        Assert.Null(await Cmd(kit, b.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.DeclareWar, Name = "Alpha" }));
        int mutual = kit.World.RelationOfForTest(a.Member, b.Member);
        Assert.NotEqual(0, mutual & PledgeProtocol.RelationMutualWar);
    }

    [Fact]
    public async Task Surrender_ends_the_war_on_both_sides()
    {
        var (kit, _) = World();
        var a = await ClanAsync(kit, "Alpha");
        var b = await ClanAsync(kit, "Beta");
        Assert.True(kit.World.SetMutualWarForTest(a.ClanId, b.ClanId));

        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.SurrenderWar, Name = "Beta" }));
        Assert.Equal(PledgeProtocol.NotAtWar,
            await Cmd(kit, b.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.StopWar, Name = "Alpha" }));
        Assert.Equal(0, kit.World.RelationOfForTest(a.Member, b.Member) & PledgeProtocol.RelationMutualWar);
    }
}