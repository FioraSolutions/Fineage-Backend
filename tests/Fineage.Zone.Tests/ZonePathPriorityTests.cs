using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// <c>_pathRequests</c> is ordered by priority, then arrival, and <see cref="List{T}"/> does not resort itself. A request
/// whose priority rises while it waits must therefore be taken out and inserted again; otherwise it stays physically
/// queued behind work it now outranks, which is exactly what a mob that enters combat after it started walking hits.
/// </summary>
public sealed class ZonePathPriorityTests
{
    const int WantX = 4_000, WantY = 4_000, WantZ = 0;

    /// <summary>An NPC with no aggro, no walking route and no watcher: <see cref="ZoneWorld.PathPriorityOf"/> says P3.</summary>
    static ZoneEntity Npc(int objectId) => new()
    {
        ObjectId = objectId,
        Kind = ZoneEntityKind.Npc,
        NpcId = 20_000 + objectId,
        Name = "Walker" + objectId,
        X = 0,
        Y = 0,
        Z = 0,
        CurHp = 100,
        MaxHp = 100,
        HomeSet = true,
        HomeX = 0,
        HomeY = 0,
        HomeZ = 0
    };

    static int[] Order(ZoneWorld world) =>
        world.PathfindQueueForTest().Select(static r => r.ObjectId).ToArray();

    [Fact]
    public void Promotion_moves_the_request_ahead_of_lower_priorities()
    {
        var world = new ZoneWorld();

        // The mob walks first, so it holds the oldest arrival number of the three.
        var walker = Npc(1);
        world.PlaceForTests(walker);
        world.EnqueueNpcPathfindForTest(walker, ZoneWorld.PathPriority.P3, WantX, WantY, WantZ);

        var watched = Npc(2);
        world.PlaceForTests(watched);
        world.EnqueueNpcPathfindForTest(watched, ZoneWorld.PathPriority.P2, WantX, WantY, WantZ);

        var watchedToo = Npc(3);
        world.PlaceForTests(watchedToo);
        world.EnqueueNpcPathfindForTest(watchedToo, ZoneWorld.PathPriority.P2, WantX, WantY, WantZ);

        // Both P2 requests are served before the P3 walker, even though the walker arrived first.
        Assert.Equal([2, 3, 1], Order(world));

        // Entering combat raises #1 to P1 with the destination unchanged: the branch that used to mutate priority in place.
        walker.AggroTargetId = watched.ObjectId;
        world.RequestNpcGeoPathForTest(walker, WantX, WantY, WantZ);

        Assert.Equal([1, 2, 3], Order(world));
        Assert.Equal(ZoneWorld.PathPriority.P1, world.PathfindQueueForTest()[0].Priority);
        Assert.All(world.PathfindQueueForTest().Skip(1),
            static r => Assert.Equal(ZoneWorld.PathPriority.P2, r.Priority));
    }

    [Fact]
    public void Promotion_joins_the_new_priority_band_behind_work_already_there()
    {
        var world = new ZoneWorld();
        var earlier = Npc(10);
        world.PlaceForTests(earlier);
        earlier.AggroTargetId = 999;
        world.RequestNpcGeoPathForTest(earlier, WantX, WantY, WantZ);

        var promoted = Npc(11);
        world.PlaceForTests(promoted);
        world.EnqueueNpcPathfindForTest(promoted, ZoneWorld.PathPriority.P3, WantX, WantY, WantZ);

        Assert.Equal([10, 11], Order(world));

        promoted.AggroTargetId = 999;
        world.RequestNpcGeoPathForTest(promoted, WantX, WantY, WantZ);

        // Both are P1 now; the promoted one keeps its own arrival order, so it never jumps the request that was already P1.
        Assert.Equal([10, 11], Order(world));
        Assert.Equal([1, 2], world.PathfindQueueForTest().Select(static r => r.Sequence).ToArray());
    }

    [Fact]
    public void Promotion_does_not_duplicate_the_request()
    {
        var world = new ZoneWorld();
        var npc = Npc(7);
        world.PlaceForTests(npc);
        world.EnqueueNpcPathfindForTest(npc, ZoneWorld.PathPriority.P3, WantX, WantY, WantZ);
        npc.AggroTargetId = 999;

        // A chase re-targets the same cell every step; promoting repeatedly must not grow the queue.
        for (int i = 0; i < 5; i++)
            world.RequestNpcGeoPathForTest(npc, WantX, WantY, WantZ);

        Assert.Single(world.PathfindQueueForTest());
        Assert.Equal(7, world.PathfindQueueForTest()[0].ObjectId);
        Assert.Equal(ZoneWorld.PathPriority.P1, world.PathfindQueueForTest()[0].Priority);
    }

    [Fact]
    public void Repeating_the_same_priority_keeps_the_queue_position()
    {
        var world = new ZoneWorld();
        var first = Npc(20);
        world.PlaceForTests(first);
        world.EnqueueNpcPathfindForTest(first, ZoneWorld.PathPriority.P2, WantX, WantY, WantZ);

        var second = Npc(21);
        world.PlaceForTests(second);
        world.EnqueueNpcPathfindForTest(second, ZoneWorld.PathPriority.P2, WantX, WantY, WantZ);

        // Already P2, so re-enqueueing is a no-op and must not reshuffle the FIFO inside the band.
        world.RequestNpcGeoPathForTest(first, WantX, WantY, WantZ);
        world.RequestNpcGeoPathForTest(second, WantX, WantY, WantZ);

        Assert.Equal([20, 21], Order(world));
    }

    [Fact]
    public void Priority_never_drops_and_sequence_survives_a_new_destination()
    {
        var world = new ZoneWorld();
        var npc = Npc(30);
        world.PlaceForTests(npc);
        npc.AggroTargetId = 999;
        world.RequestNpcGeoPathForTest(npc, WantX, WantY, WantZ);
        int sequence = world.PathfindQueueForTest()[0].Sequence;

        // The mob loses its target and keeps walking to a new cell: P1 stays P1, and the arrival order is untouched.
        npc.AggroTargetId = 0;
        world.RequestNpcGeoPathForTest(npc, WantX + 100, WantY, WantZ);

        var request = Assert.Single(world.PathfindQueueForTest());
        Assert.Equal(ZoneWorld.PathPriority.P1, request.Priority);
        Assert.Equal(sequence, request.Sequence);
    }

    [Fact]
    public void Retargeting_a_pending_route_moves_it_into_the_raised_band()
    {
        var world = new ZoneWorld();
        var other = Npc(41);
        world.PlaceForTests(other);
        world.EnqueueNpcPathfindForTest(other, ZoneWorld.PathPriority.P2, WantX, WantY, WantZ);

        var npc = Npc(40);
        world.PlaceForTests(npc);
        world.EnqueueNpcPathfindForTest(npc, ZoneWorld.PathPriority.P3, WantX, WantY, WantZ);
        Assert.Equal([41, 40], Order(world));

        npc.AggroTargetId = 999;
        world.RequestNpcGeoPathForTest(npc, WantX + 50, WantY, WantZ);

        Assert.Equal([40, 41], Order(world));
    }
}
