using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Clan;
using Fineage.Zone.Friends;
using Fineage.Zone.Party;
using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Step 10 of <c>docs/architecture/zone-simulation.md</c>: party, clan, chat and friends are social actors on the
/// scheduler thread. Region bodies tell them at the barrier, and their audiences do not depend on regions.
/// </summary>
public sealed class SocialActorTests
{
    const int BerserkerSpirit = 1062;

    sealed class NoMembers : IPartyMembers
    {
        public bool IsOnline(int objectId) => false;

        public PartyMemberUpdateMsg? Bars(int objectId) => null;

        public (int X, int Y, int Z)? Position(int objectId) => null;
    }

    sealed class FailureLog : ISimulationObserver
    {
        public List<StageFailure> Failures { get; } = [];

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed)
        {
        }

        public void StageFailed(StageFailure failure)
        {
            lock (Failures)
                Failures.Add(failure);
        }

        public void TicksSkipped(TickContext tick, long skipped)
        {
        }

        public void EntitiesHandedOff(TickContext tick, IReadOnlyList<RegionTransfer> transfers)
        {
        }

        public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget)
        {
        }
    }

    [Fact]
    public void Region_bodies_cannot_write_a_social_actor()
    {
        var party = new PartyActor(new NoMembers());
        var clan = new ClanActor();
        var chat = new ChatActor();
        var friends = new FriendsActor();
        friends.Load(1, "Ally", null);
        Action[] requests =
        [
            () => party.Step(new TickContext(1, 0, 0)),
            () => party.Create(9, 0),
            () => party.Changed(1),
            () => clan.Link(1, 7),
            () => chat.Find("Scout"),
            () => friends.TryGet(1, out _),
            () => friends.Blocks(1, 2),
            () => friends.MutualOnline(1)
        ];
        var observer = new FailureLog();
        var partition = new GridRegionPartition(1000);
        var scheduler = new RegionTickScheduler(new SimulationClock(100, () => 0), new ParallelRegionExecutor(4), observer);
        scheduler.AddRegionStage(new RegionStage("bodies",
            (_, work) =>
            {
                for (int i = 0; i < requests.Length; i++)
                    work.Add(partition.RegionOf(0, i * 5_000, 0), i);
            },
            (_, i) => requests[i](),
            parallel: true));

        scheduler.Step(100);

        Assert.Equal(requests.Length, observer.Failures.Count);
        Assert.All(observer.Failures, f => Assert.Contains("reached from the body of region", f.Error.Message));
        foreach (var request in requests)
            request();
    }

    /// <summary>
    /// The roster only changes on the scheduler thread and at the barrier, none of which run while a body is in flight,
    /// so a body reads a stable party and a kill inside <c>auto-attacks</c> can settle its exp share there.
    /// </summary>
    [Fact]
    public void Region_bodies_read_the_party_roster()
    {
        var party = new PartyActor(new NoMembers());
        var partyOfOne = party.Create(1, 0);
        party.Join(partyOfOne, 2);
        List<int> seen = [];
        var observer = new FailureLog();
        var partition = new GridRegionPartition(1000);
        var scheduler = new RegionTickScheduler(new SimulationClock(100, () => 0), new ParallelRegionExecutor(4), observer);
        scheduler.AddRegionStage(new RegionStage("bodies",
            (_, work) =>
            {
                work.Add(partition.RegionOf(0, 0, 0), 1);
                work.Add(partition.RegionOf(0, 5_000, 0), 2);
            },
            (_, _) =>
            {
                seen.Add(party.InParty(1) ? 1 : 0);
                seen.Add(party.SameParty(1, 2) ? 1 : 0);
                if (party.Of(1) is { } p)
                    seen.Add(p.PartyId);
            },
            parallel: true));

        scheduler.Step(100);

        Assert.Empty(observer.Failures);
        Assert.Equal([1, 1, partyOfOne.PartyId, 1, 1, partyOfOne.PartyId], seen);
    }

    /// <summary>
    /// The clan roster changes only on the scheduler thread and at the barrier, so a combat body deciding whether two
    /// players are at mutual war (<c>auto-attacks</c> → <c>PlayerAutoAttackable</c> → <c>AtMutualWar</c>) reads it
    /// without deferring the whole hit. Writes from a body still throw.
    /// </summary>
    [Fact]
    public void Region_bodies_read_the_clan_roster()
    {
        var clan = new ClanActor();
        clan.Add(new ZoneClan { Id = 7, Name = "Wardens" });
        clan.Link(1, 7);
        clan.Link(2, 7);
        clan.SetJoinExpiry(2, 1_234);
        clan.Invite(3, new PendingClanInvite(1, 7, 9_999));
        List<int> seen = [];
        var observer = new FailureLog();
        var partition = new GridRegionPartition(1000);
        var scheduler = new RegionTickScheduler(new SimulationClock(100, () => 0), new ParallelRegionExecutor(4), observer);
        scheduler.AddRegionStage(new RegionStage("bodies",
            (_, work) =>
            {
                work.Add(partition.RegionOf(0, 0, 0), 1);
                work.Add(partition.RegionOf(0, 5_000, 0), 2);
            },
            (_, _) =>
            {
                // Each body reads its own seven values on a worker thread and leaves them in one piece, so the
                // recorded list keeps each body's reads contiguous whichever order the executor runs the regions in.
                List<int> reads = [clan.ClanIdOf(1), clan.ClanIdOf(2), clan.Contains(7) ? 1 : 0];
                if (clan.TryGet(7, out var roster))
                    reads.Add(roster.Members.Count);
                reads.Add(clan.TryGetJoinExpiry(2, out long expiry) ? (int)expiry : 0);
                reads.Add(clan.TryGetInvite(3, out _) ? 1 : 0);
                reads.Add(clan.InInvite(3) ? 1 : 0);
                lock (seen)
                    seen.AddRange(reads);
            },
            parallel: true));

        scheduler.Step(100);

        Assert.Empty(observer.Failures);
        Assert.Equal([7, 7, 1, 0, 1_234, 1, 1, 7, 7, 1, 0, 1_234, 1, 1], seen);
    }

    [Fact]
    public void Clan_dissolutions_follow_the_wall_sample_of_the_step()
    {
        var clans = new ClanActor();
        clans.Add(new ZoneClan { Id = 7, Name = "Late", DissolvingExpiryUnixMs = 10_000 });
        clans.Add(new ZoneClan { Id = 3, Name = "Due", DissolvingExpiryUnixMs = 5_000 });
        clans.Add(new ZoneClan { Id = 5, Name = "Kept" });

        Assert.Empty(clans.Step(new TickContext(1, 60_000, 4_999)));
        Assert.Equal([new ClanDissolutionDue(3)], clans.Step(new TickContext(2, 0, 5_000)));
        Assert.Equal([new ClanDissolutionDue(3), new ClanDissolutionDue(7)], clans.Step(new TickContext(3, 0, 10_000)));
    }

    [Fact]
    public async Task A_friend_leaving_the_world_shows_offline_to_its_online_friends()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var a = kit.Create();
        var b = kit.Create();
        var (sessionA, playerA) = await kit.EnterAsync(a);
        var (sessionB, playerB) = await kit.EnterAsync(b);
        Assert.Null(await kit.World.TryFriendInviteAsync(sessionA, new FriendInviteMsg { Name = b.Name }, default));
        Assert.Null(await kit.World.TryFriendAnswerAsync(sessionB, new FriendAnswerMsg { Response = 1 }, default));
        lock (rec.Calls)
            rec.Calls.Clear();

        await kit.World.LeaveAsync(sessionB, default);

        var update = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutFriendUpdate)));
        Assert.Equal([playerA.ObjectId], Assert.IsAssignableFrom<IReadOnlyList<int>>(update[0]));
        var row = Assert.IsType<FriendUpdateMsg>(update[1]);
        Assert.Equal((1, playerB.ObjectId, b.Name, 0), (row.Added, row.ObjectId, row.Name, row.Online));

        var (sessionBack, _) = await kit.EnterAsync(b);
        var back = rec.Of(nameof(IWorldEventPublisher.FanoutFriendUpdate)).Skip(1)
            .Select(c => (To: Assert.IsAssignableFrom<IReadOnlyList<int>>(c[0]).Single(), Row: Assert.IsType<FriendUpdateMsg>(c[1])))
            .ToList();
        Assert.Contains(back, x => x.To == playerA.ObjectId && x.Row.ObjectId == playerB.ObjectId && x.Row.Online == 1);
        Assert.Contains(back, x => x.To == sessionBack.ObjectId && x.Row.ObjectId == playerA.ObjectId && x.Row.Online == 1);
    }

    [Fact]
    public async Task Whispers_and_name_lookups_follow_the_roster_of_players_in_the_world()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (speaker, _) = await kit.EnterAsync();
        var target = kit.Create();
        var (targetSession, targetEntity) = await kit.EnterAsync(target);
        var at = DateTimeOffset.FromUnixTimeMilliseconds(1_900_000_000_000);

        Assert.Null(await kit.World.TrySayAsync(speaker, "psst", ChatProtocol.Tell, at, default, target.Name.ToUpperInvariant()));
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutCreatureSay)),
            c => Assert.IsAssignableFrom<IReadOnlyList<int>>(c[0]).SequenceEqual([targetEntity.ObjectId]));

        await kit.World.LeaveAsync(targetSession, default);
        lock (rec.Calls)
            rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(speaker, "psst", ChatProtocol.Tell, at.AddSeconds(5), default, target.Name));
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutCreatureSay)));
        var refusal = Assert.IsType<SystemMessageMsg>(Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)))[1]);
        Assert.Equal(ChatProtocol.PlayerNotOnline, refusal.Id);
    }

    [Fact]
    public async Task Party_effects_expiring_in_region_bodies_reach_mates_in_other_regions_the_same_in_both_executors()
    {
        var sequential = await ExpireAcrossRegionsAsync(SequentialRegionExecutor.Instance);
        var parallel = await ExpireAcrossRegionsAsync(new ParallelRegionExecutor(4));

        Assert.Equal(2, sequential.Count);
        Assert.Equal(sequential, parallel);
    }

    /// <summary>Two members two map tiles apart; each one's buff expires in its own region body.</summary>
    static async Task<List<string>> ExpireAcrossRegionsAsync(IRegionExecutor executor)
    {
        var kit = new NetTestKit(regions: GridRegionPartition.MapTiles, regionExecutor: executor, simulationSeed: 9);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (leader, leaderEntity) = await kit.EnterAsync();
        var (member, memberEntity) = await kit.EnterAsync(x: NetTestKit.ElfX + 2 * GridRegionPartition.MapTileSize);
        Assert.NotEqual(GridRegionPartition.MapTiles.RegionOf(0, leaderEntity.X, leaderEntity.Y),
            GridRegionPartition.MapTiles.RegionOf(0, memberEntity.X, memberEntity.Y));
        Assert.Null(await kit.World.TryPartyInviteAsync(leader, memberEntity.ObjectId, 0, default));
        Assert.Null(await kit.World.TryPartyAnswerAsync(member, leader.ObjectId, 1, default));
        var template = kit.Game.Skills.Find(BerserkerSpirit)!;
        Assert.True(template.TryLevelData(1, out var level));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(template, level, leaderEntity));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(template, level, memberEntity));
        lock (rec.Calls)
            rec.Calls.Clear();

        kit.Now = kit.Now.AddSeconds(template.AbnormalTimeSeconds + 5);
        kit.World.StepSimulation();

        return rec.Of(nameof(IWorldEventPublisher.FanoutPartySpelled))
            .Select(c =>
            {
                var to = Assert.IsAssignableFrom<IReadOnlyList<int>>(c[0]);
                var msg = Assert.IsType<AbnormalStatusUpdateMsg>(c[1]);
                Assert.DoesNotContain(msg.Effects, e => e.Id == BerserkerSpirit);
                return $"{string.Join(",", to)} <- {msg.ObjectId}";
            })
            .ToList();
    }
}
