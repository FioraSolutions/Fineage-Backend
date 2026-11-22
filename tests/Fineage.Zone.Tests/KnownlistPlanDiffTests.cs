using System.Diagnostics;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;
using Xunit.Abstractions;

namespace Fineage.Zone.Tests;

/// <summary>
/// The sorted-merge knownlist diff must emit the same frames as the previous OrderBy implementation.
/// </summary>
[Collection(nameof(KnownlistPlanDiffCollection))]
public sealed class KnownlistPlanDiffTests(ITestOutputHelper output)
{
    const int Adena = 57;
    const int Trials = 40;

    [Fact]
    public async Task PlanDiff_matches_orderby_oracle_on_randomized_watcher_sets()
    {
        var (kit, session, self, ids) = await BuildWorldAsync();
        var map = new EntityIdMap();
        var fanout = new NetKnownlistFanout(kit.World, map, new NetPresenceHub(), _ => 300);
        var selfId = EntityId.FromObjectId(self.ObjectId);
        var random = new Random(867);

        AssertSame(Oracle(kit.World, map, session, selfId, [], []), fanout.PlanDiff(session, selfId, [], []));

        var all = new HashSet<int>(ids);
        var reversed = new HashSet<int>();
        for (int i = ids.Count - 1; i >= 0; i--)
            reversed.Add(ids[i]);
        AssertSame(Oracle(kit.World, map, session, selfId, [], all), fanout.PlanDiff(session, selfId, [], reversed));
        AssertSame(Oracle(kit.World, map, session, selfId, all, []), fanout.PlanDiff(session, selfId, reversed, []));
        AssertSame(Oracle(kit.World, map, session, selfId, all, all), fanout.PlanDiff(session, selfId, reversed, all));

        for (int trial = 0; trial < Trials; trial++)
        {
            var before = Subset(random, ids);
            var after = Subset(random, ids);
            var expected = Oracle(kit.World, map, session, selfId, before, after);
            var actual = fanout.PlanDiff(session, selfId, before, after);
            AssertSame(expected, actual);
        }
    }

    [Fact]
    public async Task PlanDiff_reuses_buffers_and_allocates_less_than_orderby()
    {
        var (kit, session, self, ids) = await BuildWorldAsync();
        var live = ids.Where(id => kit.World.TryGetEntityById(id, out _)).ToList();
        var map = new EntityIdMap();
        var fanout = new NetKnownlistFanout(kit.World, map, new NetPresenceHub(), _ => 300);
        var selfId = EntityId.FromObjectId(self.ObjectId);
        var same = new HashSet<int>(live);
        var before = new HashSet<int>(live.Take(live.Count * 3 / 4));
        var after = new HashSet<int>(live.Skip(live.Count / 4));

        const int iterations = 2_000;
        Warm(iterations / 10, () => fanout.PlanDiff(session, selfId, same, same));
        Warm(iterations / 10, () => Oracle(kit.World, map, session, selfId, same, same));
        Warm(iterations / 10, () => fanout.PlanDiff(session, selfId, before, after));
        Warm(iterations / 10, () => Oracle(kit.World, map, session, selfId, before, after));

        var steadyNew = Measure(iterations, () => fanout.PlanDiff(session, selfId, same, same));
        var steadyOld = Measure(iterations, () => Oracle(kit.World, map, session, selfId, same, same));
        var churnNew = Measure(iterations, () => fanout.PlanDiff(session, selfId, before, after));
        var churnOld = Measure(iterations, () => Oracle(kit.World, map, session, selfId, before, after));

        output.WriteLine(
            $"plandiff n={live.Count} iterations={iterations} " +
            $"steady-new bytes={steadyNew.Bytes} ({Per(steadyNew.Bytes, iterations):F1}/call) ms={steadyNew.Ms:F2} ({Per(steadyNew.Ms, iterations) * 1000:F3} us/call); " +
            $"steady-old bytes={steadyOld.Bytes} ({Per(steadyOld.Bytes, iterations):F1}/call) ms={steadyOld.Ms:F2} ({Per(steadyOld.Ms, iterations) * 1000:F3} us/call); " +
            $"churn-new bytes={churnNew.Bytes} ({Per(churnNew.Bytes, iterations):F1}/call) ms={churnNew.Ms:F2} ({Per(churnNew.Ms, iterations) * 1000:F3} us/call); " +
            $"churn-old bytes={churnOld.Bytes} ({Per(churnOld.Bytes, iterations):F1}/call) ms={churnOld.Ms:F2} ({Per(churnOld.Ms, iterations) * 1000:F3} us/call)");

        Assert.Equal(0, steadyNew.Bytes);
        Assert.True(steadyOld.Bytes > steadyNew.Bytes, $"order-by steady bytes {steadyOld.Bytes} should exceed the pooled diff");
        Assert.True(churnNew.Bytes < churnOld.Bytes, $"churn bytes new={churnNew.Bytes} old={churnOld.Bytes}");
        Assert.Same(fanout.PlanDiff(session, selfId, same, same), fanout.PlanDiff(session, selfId, [], []));
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Self, List<int> Ids)> BuildWorldAsync()
    {
        var kit = new NetTestKit();
        var (session, self) = await kit.EnterAsync();
        self.Sitting = true;
        var (otherSession, other) = await kit.EnterAsync(x: NetTestKit.ElfX + 80);
        other.Sitting = true;
        _ = otherSession;

        var ids = new List<int> { self.ObjectId, other.ObjectId };
        for (int i = 0; i < 24; i++)
        {
            var mob = kit.World.SpawnMonsterForTests(
                20001,
                NetTestKit.ElfX + 40 + i * 3,
                NetTestKit.ElfY + (i % 5),
                NetTestKit.ElfZ);
            if (i == 0)
            {
                mob.MoveActive = true;
                mob.MoveDestX = mob.X + 150;
                mob.MoveDestY = mob.Y;
                mob.MoveDestZ = mob.Z;
                mob.Running = true;
            }

            ids.Add(mob.ObjectId);
        }

        self.Inventory!.Add(Adena, 100);
        var stack = self.Inventory.FindByItemId(Adena);
        Assert.NotNull(stack);
        Assert.Null(await kit.World.TryDropItemAsync(
            session, stack!.ObjectId, 10, self.X + 30, self.Y, self.Z, CancellationToken.None));
        int groundId = 0;
        foreach (int id in self.Knownlist)
        {
            if (kit.World.TryGetEntityById(id, out var entity) && entity.Kind == ZoneEntityKind.GroundItem)
                groundId = id;
        }

        Assert.NotEqual(0, groundId);
        ids.Add(groundId);
        ids.Add(9_876_543);
        ids.Add(9_876_544);
        return (kit, session, self, ids);
    }

    static HashSet<int> Subset(Random random, List<int> ids)
    {
        var set = new HashSet<int>();
        foreach (int id in ids)
        {
            if (random.Next(2) == 0)
                set.Add(id);
        }

        return set;
    }

    /// <summary>Previous PlanDiff: OrderBy each set and probe membership with the other set.</summary>
    static List<NetFrame> Oracle(
        ZoneWorld world,
        EntityIdMap ids,
        ZoneSession zoneSession,
        EntityId self,
        HashSet<int> before,
        HashSet<int> after)
    {
        var frames = new List<NetFrame>();
        foreach (int id in after.OrderBy(static x => x))
        {
            if (before.Contains(id))
                continue;
            if (!world.TryGetEntityById(id, out var newlySeen))
                continue;

            var otherId = ids.AllocateOrGet(id);
            if (newlySeen.Kind == ZoneEntityKind.GroundItem)
            {
                frames.Add(NetFrame.ToSelf(
                    NetOpcode.DropItem,
                    NetCodec.EncodeDropItem(new DropItemNetMsg
                    {
                        DropperObjectId = 0,
                        EntityValue = otherId.Value,
                        EntityGeneration = otherId.Generation,
                        ItemId = newlySeen.GroundItemId,
                        X = newlySeen.X,
                        Y = newlySeen.Y,
                        Z = newlySeen.Z,
                        Stackable = newlySeen.GroundStackable,
                        Count = newlySeen.GroundCount
                    })));
                continue;
            }

            var spawn = NetCodec.EncodeEntitySpawn(
                NetEnterWorldProjector.BuildPresenceSpawn(newlySeen, otherId, 300));
            frames.Add(NetFrame.ToSelf(NetOpcode.EntitySpawn, spawn));
            foreach (var (opcode, payload) in NetEnterWorldProjector.BuildPresenceFollowUps(newlySeen, otherId, world))
                frames.Add(NetFrame.ToSelf(opcode, payload));
            if (newlySeen.Kind == ZoneEntityKind.Player &&
                world.TryGetLivePlayer(zoneSession, out var selfEntity))
            {
                var toPeer = NetCodec.EncodeEntitySpawn(
                    NetEnterWorldProjector.BuildRemoteSpawn(selfEntity, self, 300));
                frames.Add(new NetFrame(id, NetOpcode.EntitySpawn, toPeer));
                foreach (var (opcode, payload) in NetEnterWorldProjector.BuildPresenceFollowUps(selfEntity, self, world))
                    frames.Add(new NetFrame(id, opcode, payload));
            }
        }

        var despawnSelf = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = self.Value,
            EntityGeneration = self.Generation
        });
        foreach (int id in before.OrderBy(static x => x))
        {
            if (after.Contains(id))
                continue;
            var forgottenId = ids.AllocateOrGet(id);
            frames.Add(NetFrame.ToSelf(
                NetOpcode.EntityDespawn,
                NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
                {
                    EntityValue = forgottenId.Value,
                    EntityGeneration = forgottenId.Generation
                })));
            if (world.TryGetLivePlayerById(id, out _))
                frames.Add(new NetFrame(id, NetOpcode.EntityDespawn, despawnSelf));
        }

        return frames;
    }

    static void AssertSame(List<NetFrame> expected, List<NetFrame> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].PeerObjectId, actual[i].PeerObjectId);
            Assert.Equal(expected[i].Opcode, actual[i].Opcode);
            Assert.Equal(expected[i].Payload, actual[i].Payload);
        }
    }

    static void Warm(int iterations, Action action)
    {
        for (int i = 0; i < iterations; i++)
            action();
    }

    static (long Bytes, double Ms) Measure(int iterations, Action action)
    {
        var sw = new Stopwatch();
        long start = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        for (int i = 0; i < iterations; i++)
            action();
        sw.Stop();
        return (GC.GetAllocatedBytesForCurrentThread() - start, sw.Elapsed.TotalMilliseconds);
    }

    static double Per(double total, int iterations) => total / iterations;
}
