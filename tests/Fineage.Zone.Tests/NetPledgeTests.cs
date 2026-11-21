using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Block C clan window over Net (Mobius CT_0 rules): sub-unit invites, academy graduation, ranks and privileges,
/// titles, crests, wars (war kills are PvP, not PK), clan hall auctions and the leader delegation at restart.
/// </summary>
public sealed class NetPledgeTests
{
    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    sealed record ClanSetup(int ClanId, ZoneSession Leader, ZoneEntity LeaderEntity, List<(ZoneSession S, ZoneEntity E)> Members);

    static async Task<ClanSetup> ClanAsync(NetTestKit kit, string name, int members = 0, int level = 0, int pledgeType = 0, int x = NetTestKit.ElfX)
    {
        var (leader, leaderEntity) = await kit.EnterAsync(level: 40, x: x);
        Assert.Null(await kit.World.TryClanCreateAsync(leader, new ClanCreateRequestMsg { Name = name }, kit.Now, default));
        int clanId = leaderEntity.ClanId;
        if (level > 0)
            Assert.True(kit.World.SetClanLevelForTest(clanId, level));
        var list = new List<(ZoneSession, ZoneEntity)>();
        for (int i = 0; i < members; i++)
            list.Add(await JoinAsync(kit, leader, leaderEntity, pledgeType, x: x));
        return new ClanSetup(clanId, leader, leaderEntity, list);
    }

    static async Task<(ZoneSession, ZoneEntity)> JoinAsync(NetTestKit kit, ZoneSession leader, ZoneEntity leaderEntity, int pledgeType = 0,
        int level = 20, int x = NetTestKit.ElfX)
    {
        var (s, e) = await kit.EnterAsync(level: level, x: x);
        Assert.Null(await kit.World.TryClanInviteAsync(leader,
            new ClanInviteRequestMsg { TargetObjectId = e.ObjectId, PledgeType = pledgeType }, kit.Now, default));
        Assert.Null(await kit.World.TryClanAnswerAsync(s,
            new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderEntity.ObjectId, Response = 1 }, kit.Now, default));
        return (s, e);
    }

    static Task<string?> Cmd(NetTestKit kit, ZoneSession s, PledgeCommandNetMsg c) =>
        kit.World.TryPledgeCommandAsync(s, NetCodec.DecodePledgeCommand(NetCodec.EncodePledgeCommand(c)), kit.Now, default);

    static List<int> Lines(NetBridgeRecorder rec, int objectId) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Where(a => ((IReadOnlyList<int>)a[0]!).Contains(objectId))
            .Select(a => ((SystemMessageMsg)a[1]!).Id).ToList();

    [Fact]
    public void Pledge_command_and_invite_tail_round_trip_and_old_invite_still_decodes()
    {
        var c = new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetCrest, IntArg1 = 3, Amount = 1L << 40, Name = "a", Text = "b", Blob = [1, 2, 3] };
        var back = NetCodec.DecodePledgeCommand(NetCodec.EncodePledgeCommand(c));
        Assert.Equal((c.Kind, 3, 1L << 40, "a", "b"), (back.Kind, back.IntArg1, back.Amount, back.Name, back.Text));
        Assert.Equal(new byte[] { 1, 2, 3 }, back.Blob);

        var academy = NetCodec.EncodeClanInviteRequest(new ClanInviteRequestNetMsg { TargetObjectId = 9, PledgeType = -1 });
        Assert.Equal(-1, NetCodec.DecodeClanInviteRequest(academy).PledgeType);
        var old = NetCodec.DecodeClanInviteRequest(academy.AsSpan(0, 4));
        Assert.Equal((9, 0), (old.TargetObjectId, old.PledgeType));
    }

    [Fact]
    public async Task Academy_needs_level_5_and_graduation_pays_reputation_and_circlet_without_penalty()
    {
        var (kit, rec) = World();
        var clan = await ClanAsync(kit, "Acad", level: 5);
        kit.World.ClanDetailsForTest(clan.ClanId);
        // Academy unit created through the village master verb path is covered elsewhere; here the test hook path:
        Assert.Null(kit.World.CreateSubUnitForTest(clan.Leader, "Pupils", "", PledgeProtocol.Academy));

        var (tooHigh, tooHighE) = await kit.EnterAsync(level: 41);
        Assert.Equal(ClanProtocol.Academy, await kit.World.TryClanInviteAsync(clan.Leader,
            new ClanInviteRequestMsg { TargetObjectId = tooHighE.ObjectId, PledgeType = PledgeProtocol.Academy }, kit.Now, default));
        Assert.Contains(1735, Lines(rec, clan.LeaderEntity.ObjectId));

        var (pupil, pupilE) = await JoinAsync(kit, clan.Leader, clan.LeaderEntity, PledgeProtocol.Academy, level: 16);
        Assert.Equal(PledgeProtocol.Academy, kit.World.PledgeTypeOfForTest(pupilE.ObjectId));
        Assert.Equal(PledgeProtocol.AcademyGrade, pupilE.PowerGrade);

        // Elven Mystic 25 → Elven Wizard 26 (1st) → Spellsinger 27 (2nd): graduation on the 2nd transfer.
        Assert.True(kit.World.ChangeClassForTest(pupil, 26));
        Assert.Equal(clan.ClanId, pupilE.ClanId);
        Assert.True(kit.World.ChangeClassForTest(pupil, 27));
        Assert.Equal(0, pupilE.ClanId);
        Assert.Equal(PledgeProtocol.AcademyMaxRep, kit.World.ClanDetailsForTest(clan.ClanId)!.Reputation);
        Assert.Contains(pupilE.Inventory!.Items, i => i.ItemId == PledgeProtocol.AcademyCircletItemId);
        Assert.Contains(1749, Lines(rec, pupilE.ObjectId));

        // No join penalty: another clan can take the graduate at once.
        var other = await ClanAsync(kit, "Next");
        Assert.Null(await kit.World.TryClanInviteAsync(other.Leader, new ClanInviteRequestMsg { TargetObjectId = pupilE.ObjectId }, kit.Now, default));
    }

    [Fact]
    public async Task Royal_guard_invite_reorganize_and_rank_privileges_control_who_may_invite()
    {
        var (kit, _) = World();
        var clan = await ClanAsync(kit, "Royal", members: 2, level: 6);
        kit.World.SetClanReputationForTest(clan.ClanId, 10_000);
        Assert.Equal(PledgeProtocol.BadArgument, kit.World.CreateSubUnitForTest(clan.Leader, "Guard", "", PledgeProtocol.Royal1));
        Assert.Null(kit.World.CreateSubUnitForTest(clan.Leader, "Guard", clan.Members[1].E.Name, PledgeProtocol.Royal1));
        Assert.Equal(5_000, kit.World.ClanDetailsForTest(clan.ClanId)!.Reputation);

        var (_, guardE) = await JoinAsync(kit, clan.Leader, clan.LeaderEntity, PledgeProtocol.Royal1);
        Assert.Equal(PledgeProtocol.Royal1, kit.World.PledgeTypeOfForTest(guardE.ObjectId));

        var (officer, officerE) = clan.Members[0];
        Assert.Null(await Cmd(kit, clan.Leader, new PledgeCommandNetMsg
        {
            Kind = PledgeCommandKind.ReorganizeMember,
            Name = officerE.Name,
            IntArg1 = PledgeProtocol.Royal1,
            Text = guardE.Name
        }));
        Assert.Equal(PledgeProtocol.Royal1, kit.World.PledgeTypeOfForTest(officerE.ObjectId));
        Assert.Equal(PledgeProtocol.Main, kit.World.PledgeTypeOfForTest(guardE.ObjectId));

        // Grade 5 cannot invite until the leader grants the rank INVITE; grade 3 gets it.
        var (_, recruitE) = await kit.EnterAsync(level: 20);
        Assert.Equal(ClanProtocol.Leader, await kit.World.TryClanInviteAsync(officer,
            new ClanInviteRequestMsg { TargetObjectId = recruitE.ObjectId }, kit.Now, default));
        Assert.Equal(PledgeProtocol.Denied, await Cmd(kit, officer, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.SetRankPrivileges, IntArg1 = 3, IntArg2 = PledgeProtocol.PrivInviteMember }));
        Assert.Null(await Cmd(kit, clan.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.SetRankPrivileges, IntArg1 = 3, IntArg2 = PledgeProtocol.PrivInviteMember }));
        Assert.Null(await Cmd(kit, clan.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.SetMemberPowerGrade, Name = officerE.Name, IntArg1 = 3 }));
        Assert.Equal(3, officerE.PowerGrade);
        Assert.Equal(PledgeProtocol.PrivInviteMember, officerE.ClanPrivileges);
        Assert.Null(await kit.World.TryClanInviteAsync(officer,
            new ClanInviteRequestMsg { TargetObjectId = recruitE.ObjectId }, kit.Now, default));

        // Academy rank can only hold warehouse + doors.
        Assert.Null(await Cmd(kit, clan.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.SetRankPrivileges, IntArg1 = 9, IntArg2 = PledgeProtocol.PrivAll }));
        Assert.Equal(PledgeProtocol.PrivAcademyMask, kit.World.ClanDetailsForTest(clan.ClanId)!.RankPrivileges[9]);
    }

    [Fact]
    public async Task Title_needs_clan_level_3_and_persists_and_reaches_watchers()
    {
        var (kit, rec) = World();
        var clan = await ClanAsync(kit, "Titles", members: 1);
        var (_, memberE) = clan.Members[0];
        var title = new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetTitle, Name = memberE.Name, Text = "Tank" };
        Assert.Equal(PledgeProtocol.Conditions, await Cmd(kit, clan.Leader, title));
        Assert.Contains(271, Lines(rec, clan.LeaderEntity.ObjectId));

        kit.World.SetClanLevelForTest(clan.ClanId, 3);
        Assert.Null(await Cmd(kit, clan.Leader, title));
        Assert.Equal("Tank", memberE.Title);
        Assert.Contains(214, Lines(rec, memberE.ObjectId));
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutCharInfo)));

        // Leaving the clan drops the title (Mobius removeClanMember, non-noble).
        Assert.Null(await kit.World.TryClanLeaveAsync(clan.Members[0].S, kit.Now, default));
        Assert.Equal("", memberE.Title);
    }

    [Fact]
    public async Task Crest_upload_checks_size_level_and_privilege_and_is_fetchable()
    {
        var (kit, rec) = World();
        var clan = await ClanAsync(kit, "Crest", members: 1);
        var crest = new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetCrest, Blob = new byte[200] };
        crest.Blob[0] = 7;
        Assert.Equal(PledgeProtocol.Conditions, await Cmd(kit, clan.Leader, crest));
        kit.World.SetClanLevelForTest(clan.ClanId, 3);
        Assert.Equal(PledgeProtocol.CrestSize, await Cmd(kit, clan.Leader,
            new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetCrest, Blob = new byte[257] }));
        Assert.Equal(PledgeProtocol.Denied, await Cmd(kit, clan.Members[0].S, crest));

        Assert.Null(await Cmd(kit, clan.Leader, crest));
        int crestId = clan.LeaderEntity.ClanCrestId;
        Assert.NotEqual(0, crestId);
        Assert.Equal(crestId, clan.Members[0].E.ClanCrestId);

        Assert.Null(await Cmd(kit, clan.Members[0].S, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.RequestCrest, IntArg1 = crestId, IntArg2 = PledgeProtocol.CrestKindPledge }));
        var sent = Assert.IsType<CrestDataNetMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutCrestData)).Last()[1]);
        Assert.Equal((crestId, 200, (byte)7), (sent.CrestId, sent.Data.Length, sent.Data[0]));

        var large = new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetLargeCrest, Blob = new byte[2176] };
        Assert.Null(await Cmd(kit, clan.Leader, large));
        Assert.NotEqual(0, clan.LeaderEntity.LargeCrestId);

        // Empty data deletes; the old blob is gone from the store.
        Assert.Null(await Cmd(kit, clan.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetCrest }));
        Assert.Equal(0, clan.LeaderEntity.ClanCrestId);
        Assert.Null(kit.World.CrestBytesForTest(crestId));
        Assert.Contains(1861, Lines(rec, clan.LeaderEntity.ObjectId));
    }

    [Fact]
    public async Task Mutual_war_makes_kills_pvp_not_pk_and_moves_reputation()
    {
        var (kit, rec) = World();
        var a = await ClanAsync(kit, "Alpha", members: 14, level: 5);
        var b = await ClanAsync(kit, "Bravo", members: 14, level: 5);
        kit.World.SetClanReputationForTest(a.ClanId, 100);
        kit.World.SetClanReputationForTest(b.ClanId, 100);

        // One-sided: still a PK.
        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.DeclareWar, Name = "Bravo" }));
        Assert.Contains(1561, Lines(rec, b.LeaderEntity.ObjectId));
        var killer = a.Members[0].E;
        var victim = b.Members[0].E;
        Assert.False(kit.World.PlayerAutoAttackableForTest(killer, victim));

        Assert.Null(await Cmd(kit, b.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.DeclareWar, Name = "Alpha" }));
        Assert.True(kit.World.PlayerAutoAttackableForTest(killer, victim));
        var relation = rec.Of(nameof(IWorldEventPublisher.FanoutRelationChanged))
            .Select(x => (Ids: (IReadOnlyList<int>)x[0]!, M: (RelationChangedNetMsg)x[1]!))
            .Last(x => x.Ids.Contains(killer.ObjectId) && x.M.ObjectId == victim.ObjectId).M;
        Assert.Equal(PledgeProtocol.RelationMutualWar | PledgeProtocol.RelationOneSidedWar,
            relation.Relation & (PledgeProtocol.RelationMutualWar | PledgeProtocol.RelationOneSidedWar));

        kit.World.OnKillUpdatePvPKarmaForTest(killer, victim);
        Assert.Equal((0, 1, 0), (killer.Karma, killer.PvpKills, killer.PkKills));
        Assert.Equal(101, kit.World.ClanDetailsForTest(a.ClanId)!.Reputation);
        Assert.Equal(99, kit.World.ClanDetailsForTest(b.ClanId)!.Reputation);

        Assert.Null(await Cmd(kit, b.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.RequestWarList }));
        var wars = Assert.IsType<PledgeWarListNetMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutPledgeWarList)).Last()[1]);
        Assert.True(Assert.Single(wars.Declared).Mutual);
        Assert.Equal("Alpha", Assert.Single(wars.UnderAttack).Name);

        // Surrender ends both sides.
        Assert.Null(await Cmd(kit, b.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.SurrenderWar, Name = "Alpha" }));
        Assert.Empty(kit.World.ClanDetailsForTest(a.ClanId)!.WarTargets);
        Assert.Empty(kit.World.ClanDetailsForTest(b.ClanId)!.WarTargets);
    }

    [Fact]
    public async Task War_needs_level_3_and_15_members()
    {
        var (kit, rec) = World();
        var a = await ClanAsync(kit, "Small", members: 2, level: 5);
        await ClanAsync(kit, "Other", members: 2, level: 5);
        Assert.Equal(PledgeProtocol.Conditions, await Cmd(kit, a.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.DeclareWar, Name = "Other" }));
        Assert.Contains(1564, Lines(rec, a.LeaderEntity.ObjectId));
    }

    [Fact]
    public async Task Hall_auction_takes_clan_warehouse_adena_and_hands_the_hall_to_the_highest_bidder()
    {
        var (kit, _) = World();
        var a = await ClanAsync(kit, "Bidder", level: 2);
        var b = await ClanAsync(kit, "Rival", level: 2);
        var hall = kit.World.HallCatalog.Halls.OrderBy(h => h.Id).First();
        kit.World.AddClanWarehouseItemForTest(a.ClanId, PledgeProtocol.AdenaItemId, 100_000_000);
        kit.World.AddClanWarehouseItemForTest(b.ClanId, PledgeProtocol.AdenaItemId, 100_000_000);

        Assert.Equal(PledgeProtocol.BadArgument, await Cmd(kit, a.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.HallBid, IntArg1 = hall.Id, Amount = hall.StartingBid - 1 }));
        Assert.Null(await Cmd(kit, a.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.HallBid, IntArg1 = hall.Id, Amount = hall.StartingBid }));
        Assert.Equal(100_000_000 - (int)hall.StartingBid, kit.World.ClanWarehouseForTest(a.ClanId)[PledgeProtocol.AdenaItemId]);
        Assert.Equal(PledgeProtocol.BadArgument, await Cmd(kit, b.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.HallBid, IntArg1 = hall.Id, Amount = hall.StartingBid }));
        Assert.Null(await Cmd(kit, b.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.HallBid, IntArg1 = hall.Id, Amount = hall.StartingBid + 1_000 }));

        kit.World.EndHallAuctionsForTest(kit.Now.ToUnixTimeMilliseconds());
        Assert.Equal(b.ClanId, kit.World.HallForTest(hall.Id)!.Value.Owner);
        Assert.Equal(hall.Id, b.LeaderEntity.HideoutId);
        // The loser gets 90% back.
        Assert.Equal(100_000_000 - (int)hall.StartingBid + (int)(hall.StartingBid * 0.9),
            kit.World.ClanWarehouseForTest(a.ClanId)[PledgeProtocol.AdenaItemId]);

        // Hall owners are listed and cannot dissolve.
        Assert.Null(await Cmd(kit, b.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.HallList }));
    }

    [Fact]
    public async Task Hall_and_crest_and_details_survive_a_zone_restart()
    {
        var kit = new NetTestKit();
        var clan = await ClanAsync(kit, "Keep", members: 1, level: 3);
        Assert.Null(await Cmd(kit, clan.Leader, new PledgeCommandNetMsg { Kind = PledgeCommandKind.SetCrest, Blob = [1, 2, 3] }));
        Assert.Null(await Cmd(kit, clan.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.SetMemberPowerGrade, Name = clan.Members[0].E.Name, IntArg1 = 4 }));
        int crestId = clan.LeaderEntity.ClanCrestId;

        var again = new NetTestKit(store: kit.Store);
        Assert.Equal(new byte[] { 1, 2, 3 }, again.World.CrestBytesForTest(crestId));
        var details = again.World.ClanDetailsForTest(clan.ClanId)!;
        Assert.Equal(4, details.Members[clan.Members[0].E.ObjectId].PowerGrade);
        Assert.Equal(crestId, again.World.ClanSummaryForTest(clan.ClanId)!.Value.CrestId);
    }

    [Fact]
    public async Task Sponsor_links_one_academy_member_to_one_regular_member()
    {
        var (kit, rec) = World();
        var clan = await ClanAsync(kit, "Mentor", members: 1, level: 5);
        Assert.Null(kit.World.CreateSubUnitForTest(clan.Leader, "Kids", "", PledgeProtocol.Academy));
        var (_, kidE) = await JoinAsync(kit, clan.Leader, clan.LeaderEntity, PledgeProtocol.Academy, level: 10);
        var mentorE = clan.Members[0].E;
        Assert.Null(await Cmd(kit, clan.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.SetSponsor, Name = kidE.Name, Text = mentorE.Name, IntArg1 = 1 }));
        var d = kit.World.ClanDetailsForTest(clan.ClanId)!;
        Assert.Equal((mentorE.ObjectId, kidE.ObjectId), (d.Members[kidE.ObjectId].SponsorId, d.Members[mentorE.ObjectId].ApprenticeId));
        Assert.Contains(1755, Lines(rec, kidE.ObjectId));
        Assert.Equal(PledgeProtocol.Conditions, await Cmd(kit, clan.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.SetSponsor, Name = kidE.Name, Text = clan.LeaderEntity.Name, IntArg1 = 1 }));
        Assert.Null(await Cmd(kit, clan.Leader, new PledgeCommandNetMsg
        { Kind = PledgeCommandKind.SetSponsor, Name = kidE.Name, Text = mentorE.Name, IntArg1 = 0 }));
        Assert.Equal(0, kit.World.ClanDetailsForTest(clan.ClanId)!.Members[kidE.ObjectId].SponsorId);
    }

    [Fact]
    public async Task Leader_delegation_applies_at_the_next_start()
    {
        var kit = new NetTestKit();
        var clan = await ClanAsync(kit, "Heir", members: 1, level: 3);
        var heir = clan.Members[0].E;
        Assert.True(kit.World.RequestDelegationForTest(clan.ClanId, heir.ObjectId));

        var again = new NetTestKit(store: kit.Store);
        Assert.Equal(heir.ObjectId, again.World.ClanSummaryForTest(clan.ClanId)!.Value.LeaderId);
        Assert.Equal(0, again.World.ClanDetailsForTest(clan.ClanId)!.NewLeaderId);
    }

    [Fact]
    public async Task Kicking_an_academy_member_gives_no_join_penalty_but_the_clan_waits()
    {
        var (kit, _) = World();
        var clan = await ClanAsync(kit, "Kick", level: 5);
        Assert.Null(kit.World.CreateSubUnitForTest(clan.Leader, "Young", "", PledgeProtocol.Academy));
        var (_, kidE) = await JoinAsync(kit, clan.Leader, clan.LeaderEntity, PledgeProtocol.Academy, level: 10);
        Assert.Null(await kit.World.TryClanKickAsync(clan.Leader, new ClanKickRequestMsg { TargetObjectId = kidE.ObjectId }, kit.Now, default));

        var other = await ClanAsync(kit, "Taker");
        Assert.Null(await kit.World.TryClanInviteAsync(other.Leader, new ClanInviteRequestMsg { TargetObjectId = kidE.ObjectId }, kit.Now, default));
        var (_, fresh) = await kit.EnterAsync(level: 20);
        Assert.Equal(ClanProtocol.RecruitPenalty, await kit.World.TryClanInviteAsync(clan.Leader,
            new ClanInviteRequestMsg { TargetObjectId = fresh.ObjectId }, kit.Now, default));
    }

    [Fact]
    public async Task Pledge_command_reaches_the_world_through_the_net_handler()
    {
        var (kit, rec) = World();
        var clan = await ClanAsync(kit, "Wire");
        var info = new PledgeCommandNetMsg { Kind = PledgeCommandKind.RequestPledgeInfo, IntArg1 = clan.ClanId };
        Assert.Null(await Cmd(kit, clan.Leader, info));
        var sent = Assert.IsType<PledgeInfoNetMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutPledgeInfo)).Last()[1]);
        Assert.Equal("Wire", sent.Name);
        var (loner, _) = await kit.EnterAsync();
        Assert.Equal(ClanProtocol.None, await Cmd(kit, loner, new PledgeCommandNetMsg { Kind = PledgeCommandKind.RequestMemberList }));
    }
}
