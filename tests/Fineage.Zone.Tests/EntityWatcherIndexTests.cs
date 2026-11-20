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
        a.World.AssertWatcherIndexConsistent(a.ObjectId);
        a.World.AssertWatcherIndexConsistent(b.ObjectId);
        Assert.True(a.Player.Knownlist.Contains(b.ObjectId));
        Assert.True(b.Player.Knownlist.Contains(a.ObjectId));
        Assert.False(a.Player.Knownlist.Contains(far.ObjectId));

        var peers = a.World.CopyNetWatcherIds(a.ObjectId);
        Assert.Contains(b.ObjectId, peers);
        Assert.DoesNotContain(far.ObjectId, peers);
    }

    [Fact]
    public async Task Teleport_rebuilds_watchers_for_everyone_in_the_new_view()
    {
        var (bench, a, b, far) = await NearbyPairAsync();
        bench.World.TeleportPlayerForTests(a.Session, far.Player.X - 40, far.Player.Y, far.Player.Z);
        bench.World.AssertWatcherIndexConsistent(a.ObjectId);
        bench.World.AssertWatcherIndexConsistent(far.ObjectId);
        Assert.True(a.Player.Knownlist.Contains(far.ObjectId));
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
        bench.World.AssertWatcherIndexConsistent(npc.ObjectId);
        Assert.Contains(a.ObjectId, bench.World.CopyNetWatcherIds(npc.ObjectId));

        bench.World.DespawnNpcForTests(npc.ObjectId);
        Assert.False(a.Player.Knownlist.Contains(npc.ObjectId));
        Assert.Empty(bench.World.CopyNetWatcherIds(npc.ObjectId));
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