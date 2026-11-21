using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class EnterSpawnDeterminismTests
{
    [Fact]
    public async Task AppendPeerSpawns_produces_identical_records_on_repeat()
    {
        var kit = new NetTestKit();
        var (session, entity) = await kit.EnterAsync();
        var known = kit.World.SnapshotKnownEntities(session);
        var self = EntityId.FromObjectId(entity.ObjectId, 1);
        var remote = NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(entity, self));

        var once = new List<NetFrame>();
        var again = new List<NetFrame>();
        NetEnterSpawnRecord.AppendPeerSpawns(once, entity, self, remote, known, kit.World);
        NetEnterSpawnRecord.AppendPeerSpawns(again, entity, self, remote, known, kit.World);

        Assert.Equal(NetEnterSpawnRecord.Describe(once), NetEnterSpawnRecord.Describe(again));
    }

    [Fact]
    public async Task Knownlist_plan_diff_order_does_not_depend_on_hashset_insertion_order()
    {
        var kit = new NetTestKit();
        var mob1 = kit.World.SpawnMonsterForTests(20001, NetTestKit.ElfX + 50, NetTestKit.ElfY, NetTestKit.ElfZ);
        var mob2 = kit.World.SpawnMonsterForTests(20001, NetTestKit.ElfX + 55, NetTestKit.ElfY, NetTestKit.ElfZ);
        var (session, self) = await kit.EnterAsync();

        var fanout = new NetKnownlistFanout(kit.World, new EntityIdMap(), new NetPresenceHub(), _ => 300);
        var selfId = EntityId.FromObjectId(self.ObjectId);

        var before = new HashSet<int> { self.ObjectId };
        var afterA = new HashSet<int> { mob2.ObjectId, self.ObjectId, mob1.ObjectId };
        var afterB = new HashSet<int> { mob1.ObjectId, mob2.ObjectId, self.ObjectId };

        var framesA = fanout.PlanDiff(session, selfId, before, afterA);
        var framesB = fanout.PlanDiff(session, selfId, before, afterB);

        Assert.Equal(
            NetEnterSpawnRecord.Describe(framesA),
            NetEnterSpawnRecord.Describe(framesB));
        Assert.True(framesA.Count >= 2);
    }
}