using Fineage.Zone.Interest;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// AURA / AREA / FRONT_AREA hostile skills gather candidates from the caster's AOI neighbourhood instead of walking
/// <c>_byId.Values</c>. The whole-world walk only ever kept entities <c>_aoi.GetVisible(caster)</c> reported, so the
/// two must agree exactly: in range across a grid cell edge, never beyond the affect range, never across an instance,
/// and always in ascending object-id order.
/// Mobius does the same — the <c>Aura</c> / <c>Area</c> target handlers read <c>World.getVisibleObjectsInRange</c>,
/// the caster's surrounding <c>WorldRegion</c>s.
/// </summary>
public sealed class AreaSkillAoiCoverageTests
{
    /// <summary>Whirlwind: AURA, affectRange 150 (a Mobius radius around the caster).</summary>
    const int Whirlwind = 36;

    [Fact]
    public async Task Aura_candidates_cross_an_aoi_cell_edge_but_not_the_affect_range()
    {
        // Cells are 1000 wide: the caster sits in cell 0, the near target in cell 1, across x = 1000.
        var kit = new NetTestKit(
            Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting", aoi: new GridAoi(cellSize: 1_000));
        var (_, player) = await kit.EnterAsync(classId: 25, x: 960, y: 0, z: 0);

        // dx = 80 < 150, in the neighbouring AOI cell.
        var acrossEdge = Npc(kit, objectId: 900_001, x: 1_040, y: 0, z: 0);
        // dx = 360, outside the 150 affect range, in the caster's own cell.
        var outOfReach = Npc(kit, objectId: 900_002, x: 600, y: 0, z: 0);

        var ids = kit.World.HostileAffectTargetIdsForTest(player, null, Whirlwind);

        Assert.Contains(acrossEdge.ObjectId, ids);
        Assert.DoesNotContain(outOfReach.ObjectId, ids);
    }

    [Fact]
    public async Task Aura_candidates_never_cross_an_instance_even_inside_one_aoi_cell()
    {
        // One cell for the whole zone: every entity is "visible", so only the instance check can reject.
        var kit = new NetTestKit(
            Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting", aoi: new SingleCellAoi());
        var (_, player) = await kit.EnterAsync(classId: 25, x: 0, y: 0, z: 0);

        var sameInstance = Npc(kit, objectId: 900_011, x: 30, y: 0, z: 0);
        var otherInstance = Npc(kit, objectId: 900_012, x: 30, y: 0, z: 0);
        otherInstance.InstanceId = player.InstanceId + 1;

        var ids = kit.World.HostileAffectTargetIdsForTest(player, null, Whirlwind);

        Assert.Contains(sameInstance.ObjectId, ids);
        Assert.DoesNotContain(otherInstance.ObjectId, ids);
    }

    [Fact]
    public async Task Aura_candidates_keep_ascending_object_id_order()
    {
        var kit = new NetTestKit(
            Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting", aoi: new SingleCellAoi());
        var (_, player) = await kit.EnterAsync(classId: 25, x: 0, y: 0, z: 0);

        // Inserted out of order on purpose: Mobius iterates the region's objects by id.
        Npc(kit, objectId: 900_103, x: 10, y: 0, z: 0);
        Npc(kit, objectId: 900_101, x: 5, y: 0, z: 0);
        Npc(kit, objectId: 900_102, x: 15, y: 0, z: 0);

        var ids = kit.World.HostileAffectTargetIdsForTest(player, null, Whirlwind);

        Assert.Equal([900_101, 900_102, 900_103], ids);
    }

    /// <summary>
    /// Puts the NPC in the world and in the AOI the way <c>MaterializeSpawn</c> does; <c>PlaceForTests</c> alone only
    /// registers it in the region directory, and an AURA query reads the AOI.
    /// </summary>
    static ZoneEntity Npc(NetTestKit kit, int objectId, int x, int y, int z)
    {
        var npc = new ZoneEntity
        {
            ObjectId = objectId,
            Kind = ZoneEntityKind.Npc,
            NpcId = 20001,
            NpcType = "Monster",
            Attackable = true,
            Level = 1,
            X = x,
            Y = y,
            Z = z,
            MaxHp = 500,
            CurHp = 500
        };
        kit.World.PlaceForTests(npc);
        kit.World.EnterAoiForTests(npc.ObjectId, x, y);
        return npc;
    }
}
