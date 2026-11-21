using Fineage.Zone.Geo;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class EnvironmentZoneTests
{
    [Fact]
    public void Swamp_move_bonus_scales_MoveSpeedOf()
    {
        var e = new ZoneEntity { Kind = ZoneEntityKind.Player, ObjectId = 1 };
        e.RunSpeed = 200;
        e.WalkSpeed = 80;
        e.InSwamp = true;
        e.SwampMoveBonus = 0.5;
        Assert.Equal(100f, ZoneWorld.MoveSpeedOf(e));
        e.Running = false;
        Assert.Equal(40f, ZoneWorld.MoveSpeedOf(e));
    }

    [Fact]
    public void Cruma_damage_datapack_point_is_inside_enabled_zone()
    {
        var damage = DamageZones.LoadDefault();
        var zone = damage.At(17800, 108900, -6400);
        Assert.NotNull(zone);
        Assert.True(zone!.Enabled);
        Assert.Equal(18, zone.HpPerTick);
    }

    static SwampZones TinySwamp() => new([
        new SwampZoneDef
        {
            Name = "test",
            MinZ = -100, MaxZ = 100,
            Nodes = [(0, 0), (100, 0), (100, 100), (0, 100)],
            MinX = 0, MaxX = 100, MinY = 0, MaxY = 100,
            MoveBonus = 0.5
        }
    ]);

    [Fact]
    public void Swamp_polygon_hit_matches_peace_raycast()
    {
        Assert.NotNull(TinySwamp().At(50, 50, 0));
        Assert.Null(TinySwamp().At(200, 50, 0));
    }

    [Fact]
    public void Imported_mother_tree_town_polygon_covers_elven_village_center()
    {
        var zones = MotherTreeZones.LoadDefault();
        Assert.True(zones.Count > 0);
        var zone = zones.At(45365, 49596, -3010);
        Assert.NotNull(zone);
        Assert.Equal(2, zone!.HpRegenBonus);
        Assert.Equal(1, zone.MpRegenBonus);
        Assert.Equal(1, zone.AffectedRace);
    }

    [Fact]
    public async Task Elf_inside_mother_tree_gets_flat_regen_bonus()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var (_, elf) = await kit.EnterAsync(classId: 25, race: 1, x: 45365, y: 49596, z: -3010);
        Assert.Equal((2, 1), kit.World.MotherTreeRegenBonusForTest(elf));
        var (_, human) = await kit.EnterAsync(classId: 0, race: 0, x: 45365, y: 49596, z: -3010);
        Assert.Equal((0, 0), kit.World.MotherTreeRegenBonusForTest(human));
    }
}