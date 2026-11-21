using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class EntityWatcherIndexTests
{
    static async Task<(WorldBench Bench, BenchPlayer A, BenchPlayer B, BenchPlayer Far)> NearbyPairAsync()
    {
        const int x0 = 46045, y0 = 41251, z0 = -3440;
        var bench = new WorldBench();
        var a = await bench.EnterAsync(x: x0, y: y0, z: z0);
        var b = await bench.EnterAsync(x: x0 + 40, y: y0, z: z0);
        var far = await bench.EnterAsync(x: x0 + 5000, y: y0, z: z0);
        return (bench, a, b, far);
    }

    static HashSet<int> Set(IEnumerable<int> ids) => ids.ToHashSet();

    [Fact]
    public async Task Enter_link_and_forget_keep_the_reverse_index_aligned_with_knownlists()
    {
        var (_, a, b, far) = await NearbyPairAsync();
        a.World.AssertWatcherIndexConsistent();
        Assert.Contains(b.ObjectId, a.Player.Knownlist);
        Assert.Contains(a.ObjectId, b.Player.Knownlist);
        Assert.DoesNotContain(far.ObjectId, a.Player.Knownlist);

        var peers = a.World.CopyNetWatcherIds(a.ObjectId);
        Assert.Contains(b.ObjectId, peers);
        Assert.DoesNotContain(far.ObjectId, peers);
    }

    [Fact]
    public async Task Teleport_rebuilds_watchers_for_everyone_in_the_new_view()
    {
        var (bench, a, b, far) = await NearbyPairAsync();
        bench.World.TeleportPlayerForTests(a.Session, far.Player.X - 40, far.Player.Y, far.Player.Z);
        bench.World.AssertWatcherIndexConsistent();
        Assert.Contains(far.ObjectId, a.Player.Knownlist);
        Assert.Contains(far.ObjectId, bench.World.CopyNetWatcherIds(a.ObjectId));
        Assert.DoesNotContain(b.ObjectId, bench.World.CopyNetWatcherIds(a.ObjectId));
    }

    [Fact]
    public async Task Npc_spawn_and_despawn_update_watchers_with_visibility()
    {
        var (bench, a, _, _) = await NearbyPairAsync();
        int x = a.Player.X + 80;
        var npc = bench.World.SpawnFolkForTests(30001, x, a.Player.Y, a.Player.Z);
        bench.World.FanoutNewNpcForTests(npc);
        bench.World.AssertWatcherIndexConsistent();
        Assert.Contains(a.ObjectId, bench.World.CopyNetWatcherIds(npc.ObjectId));

        bench.World.DespawnNpcForTests(npc.ObjectId);
        bench.World.AssertWatcherIndexConsistent();
        Assert.DoesNotContain(npc.ObjectId, a.Player.Knownlist);
        Assert.Empty(bench.World.CopyNetWatcherIds(npc.ObjectId));
    }

    [Fact]
    public async Task Walking_in_and_out_of_view_ground_items_and_logout_keep_the_index_consistent()
    {
        var (bench, a, b, far) = await NearbyPairAsync();

        bench.World.TeleportPlayerForTests(far.Session, a.Player.X + 60, a.Player.Y, a.Player.Z);
        bench.World.AssertWatcherIndexConsistent();
        Assert.Contains(far.ObjectId, bench.World.CopyNetWatcherIds(a.ObjectId));

        int ground = bench.World.PlaceGroundForTest(a.Player, 57, 10);
        bench.World.AssertWatcherIndexConsistent();
        Assert.Contains(a.ObjectId, bench.World.CopyNetWatcherIds(ground));

        Assert.Null(await bench.World.TryPickupAsync(a.Session, ground, CancellationToken.None));
        bench.World.AssertWatcherIndexConsistent();
        Assert.Empty(bench.World.CopyNetWatcherIds(ground));

        await bench.World.LeaveAsync(b.Session, CancellationToken.None);
        bench.World.AssertWatcherIndexConsistent();
        Assert.DoesNotContain(b.ObjectId, bench.World.CopyNetWatcherIds(a.ObjectId));
        Assert.Empty(bench.World.CopyNetWatcherIds(b.ObjectId));

        bench.World.TeleportPlayerForTests(far.Session, a.Player.X + 20_000, a.Player.Y, a.Player.Z);
        bench.World.AssertWatcherIndexConsistent();
        Assert.Empty(bench.World.CopyNetWatcherIds(a.ObjectId));
    }

    [Fact]
    public async Task Npc_despawn_clears_the_target_of_a_player_that_no_longer_sees_it()
    {
        var (bench, a, _, _) = await NearbyPairAsync();
        var npc = bench.World.SpawnFolkForTests(30001, a.Player.X + 80, a.Player.Y, a.Player.Z);
        bench.World.FanoutNewNpcForTests(npc);
        a.Session.SelectedTargetId = npc.ObjectId;
        bench.World.TeleportPlayerForTests(a.Session, a.Player.X + 20_000, a.Player.Y, a.Player.Z);
        Assert.DoesNotContain(a.ObjectId, bench.World.CopyNetWatcherIds(npc.ObjectId));

        bench.World.DespawnNpcForTests(npc.ObjectId);

        Assert.Equal(0, a.Session.SelectedTargetId);
        bench.World.AssertWatcherIndexConsistent();
    }

    [Fact]
    public async Task Net_vitals_and_combat_audiences_match_the_legacy_recipient_sets()
    {
        var (bench, a, b, _) = await NearbyPairAsync();
        var mob = bench.World.SpawnFolkForTests(20001, a.Player.X + 50, a.Player.Y, a.Player.Z);
        bench.World.FanoutNewNpcForTests(mob);

        Assert.Equal(
            Set(bench.World.LegacyNetWatchersOfForTests(mob)),
            Set(bench.World.NetWatchersOf(mob)));
        Assert.Equal(
            Set(bench.World.LegacyNetWatchersOfForTests(a.Player)),
            Set(bench.World.NetWatchersOf(a.Player)));
        Assert.Equal(
            Set(bench.World.LegacyActorNetWatchersForTests(a.Player, b.Player)),
            Set(bench.World.ActorNetWatchers(a.Player, b.Player) ?? []));
    }
}