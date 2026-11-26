using Fineage.Zone.Geo;
using Fineage.Zone.Interest;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius WorldRegion activation at the real 2048-unit microregion scale (World.SHIFT_BY = 11), while Fineage keeps
/// its 32768-unit simulation tiles for ownership and the region-wake FIFO budget.
/// </summary>
public sealed class ZoneMicroRegionActivationTests
{
    const int BaseX = 100_000;
    const int BaseY = 100_000;
    const int Z = -3000;

    static SpawnNpcDefinition Mob(int id, int x, int y) =>
        new(id, 20001, "Gremlin", x, y, Z, 0, true, 80, 80f, 40f, 30, 0,
            NpcType: "Monster", Level: 1, Aggressive: false);

    static NetTestKit Kit(params SpawnNpcDefinition[] npcs) =>
        new(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["micro"] = new("micro", true, npcs)
        }), "micro", geo: GeoEngine.Empty, aoi: new GridAoi(512));

    static void At(NetTestKit kit, long unixMs) => kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

    /// <summary>
    /// Enter away from the test mobs and then move coordinates without rebuilding the knownlist, so the reverse
    /// watcher index does not mark nearby mobs awake independently of region activation.
    /// </summary>
    static async Task<(ZoneSession Session, ZoneEntity Player)> EnterAtBaseAsync(NetTestKit kit)
    {
        var (session, player) = await kit.EnterAsync(x: BaseX + 50_000, y: BaseY, z: Z);
        kit.World.MovePlayerCoordsForTests(session, BaseX, BaseY, Z);
        return (session, player);
    }

    static bool Thinking(NetTestKit kit, int objectId)
    {
        Assert.True(kit.World.TryGetEntityById(objectId, out var npc));
        return (npc.ActiveSets & ActiveSets.Thinking) != 0;
    }

    static void AssertActiveOnly(ZoneWorld world, params RegionId[] expected) =>
        Assert.Equal(expected.OrderBy(id => id).ToArray(), world.PlayerActiveRegions.OrderBy(id => id).ToArray());

    [Fact]
    public void Microregion_coordinates_match_mobius_shift_at_negative_world_edges()
    {
        Assert.Equal(new RegionId(0, 0, 0), ZoneWorld.MicroRegionAt(0, -131_072, -262_144));
        Assert.Equal(new RegionId(0, 63, 127), ZoneWorld.MicroRegionAt(0, -1, -1));
        Assert.Equal(new RegionId(0, 64, 128), ZoneWorld.MicroRegionAt(0, 0, 0));
        Assert.Equal(new RegionId(0, 64, 128), ZoneWorld.MicroRegionAt(0, 2_047, 2_047));
        Assert.Equal(new RegionId(0, 65, 129), ZoneWorld.MicroRegionAt(0, 2_048, 2_048));
    }

    [Fact]
    public async Task Self_microregion_wakes_immediately_and_neighbour_only_after_grace()
    {
        const int selfMob = 9_400_000;
        const int neighbourMob = 9_400_001;
        var kit = Kit(
            Mob(selfMob, BaseX + 100, BaseY + 100),
            Mob(neighbourMob, BaseX + ZoneWorld.MicroRegionSize + 100, BaseY + 100));

        var (_, player) = await EnterAtBaseAsync(kit);
        long start = kit.Now.ToUnixTimeMilliseconds();
        await kit.World.RunCombatTickAsync(CancellationToken.None);

        RegionId self = ZoneWorld.MicroRegionAt(player.InstanceId, BaseX, BaseY);
        AssertActiveOnly(kit.World, self);
        Assert.True(Thinking(kit, selfMob));
        Assert.False(Thinking(kit, neighbourMob));

        At(kit, start + ZoneWorld.NeighborActivationGraceMs - 1);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        AssertActiveOnly(kit.World, self);
        Assert.False(Thinking(kit, neighbourMob));

        At(kit, start + ZoneWorld.NeighborActivationGraceMs);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.Contains(new RegionId(self.InstanceId, self.X + 1, self.Y), kit.World.PlayerActiveRegions);
        Assert.True(Thinking(kit, neighbourMob));
    }

    [Fact]
    public async Task Activation_ring_is_exactly_one_microregion_not_a_map_tile_pad()
    {
        const int selfMob = 9_400_100;
        const int neighbourMob = 9_400_101;
        const int twoRingMob = 9_400_102;
        const int farMob = 9_400_103;
        var kit = Kit(
            Mob(selfMob, BaseX + 100, BaseY + 100),
            Mob(neighbourMob, BaseX + ZoneWorld.MicroRegionSize + 100, BaseY + 100),
            Mob(twoRingMob, BaseX + 2 * ZoneWorld.MicroRegionSize + 100, BaseY + 100),
            Mob(farMob, BaseX + GridRegionPartition.MapTileSize + 100, BaseY + 100));

        var (_, player) = await EnterAtBaseAsync(kit);
        long start = kit.Now.ToUnixTimeMilliseconds();
        await kit.World.RunCombatTickAsync(CancellationToken.None);

        RegionId self = ZoneWorld.MicroRegionAt(player.InstanceId, BaseX, BaseY);
        At(kit, start + ZoneWorld.NeighborActivationGraceMs);
        await kit.World.RunCombatTickAsync(CancellationToken.None);

        var expected = new List<RegionId> { self };
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0)
                    expected.Add(new RegionId(self.InstanceId, self.X + dx, self.Y + dy));
            }
        }

        AssertActiveOnly(kit.World, expected.ToArray());
        Assert.True(Thinking(kit, selfMob));
        Assert.True(Thinking(kit, neighbourMob));
        Assert.False(Thinking(kit, twoRingMob));
        Assert.False(Thinking(kit, farMob));
    }

    [Fact]
    public async Task Leaving_before_grace_cancels_pending_neighbour_activation()
    {
        const int neighbourMob = 9_400_200;
        var kit = Kit(Mob(neighbourMob, BaseX + ZoneWorld.MicroRegionSize + 100, BaseY + 100));
        var (session, player) = await EnterAtBaseAsync(kit);
        long start = kit.Now.ToUnixTimeMilliseconds();
        await kit.World.RunCombatTickAsync(CancellationToken.None);

        RegionId self = ZoneWorld.MicroRegionAt(player.InstanceId, BaseX, BaseY);
        RegionId neighbour = new(self.InstanceId, self.X + 1, self.Y);
        AssertActiveOnly(kit.World, self);

        // Moving out before the grace ends must not leave the old region's neighbours scheduled.
        kit.World.MovePlayerCoordsForTests(session, BaseX + 50_000, BaseY, Z);
        At(kit, start + ZoneWorld.NeighborActivationGraceMs - 1);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.DoesNotContain(self, kit.World.PlayerActiveRegions);
        Assert.DoesNotContain(neighbour, kit.World.PlayerActiveRegions);

        // Leaving the world cancels everything; no stale timer may wake the old neighbourhood later.
        await kit.World.LeaveAsync(session, CancellationToken.None);
        Assert.Empty(kit.World.PlayerActiveRegions);
        At(kit, start + ZoneWorld.NeighborActivationGraceMs + 5_000);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.Empty(kit.World.PlayerActiveRegions);
        Assert.DoesNotContain(neighbour, kit.World.PlayerActiveRegions);
        Assert.False(Thinking(kit, neighbourMob));
    }

    [Fact]
    public async Task Microregions_are_independent_per_instance()
    {
        var kit = Kit();
        var (session0, player0) = await kit.EnterAsync(x: BaseX, y: BaseY, z: Z);
        var (_, player1) = await kit.EnterAsync(x: BaseX, y: BaseY, z: Z);
        player1.InstanceId = 1;

        await kit.World.RunCombatTickAsync(CancellationToken.None);
        RegionId instance0 = ZoneWorld.MicroRegionAt(0, BaseX, BaseY);
        RegionId instance1 = ZoneWorld.MicroRegionAt(1, BaseX, BaseY);
        AssertActiveOnly(kit.World, instance0, instance1);

        kit.World.MovePlayerCoordsForTests(session0, BaseX + 50_000, BaseY, Z);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        RegionId moved0 = ZoneWorld.MicroRegionAt(0, BaseX + 50_000, BaseY);
        AssertActiveOnly(kit.World, moved0, instance1);
    }
}
