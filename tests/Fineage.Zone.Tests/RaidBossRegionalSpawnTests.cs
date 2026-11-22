using Fineage.Zone.Bosses;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Issue #795: retail raid/grand rows spawn at imported coords with schedule windows.</summary>
public sealed class RaidBossRegionalSpawnTests
{
    const int WorldScheduledCount = 211;

    [Fact]
    public void Catalog_marks_retail_rows_with_coords_and_respawn_window()
    {
        var catalog = BossCatalog.LoadDefault();
        Assert.Equal(223, catalog.Bosses.Count);
        Assert.Equal(WorldScheduledCount, catalog.Bosses.Count(b => b.Enabled && b.HasWorldSpawnPoint));
        Assert.Equal(216, catalog.Bosses.Count(b => b.Enabled));
    }

    [Fact]
    public void Whole_world_host_materializes_every_scheduled_boss()
    {
        var catalog = BossCatalog.LoadDefault();
        var policy = new MapFilteredBossSpawnPolicy(includeEnabledMaps: true);
        var schedule = new BossSchedule(
            catalog,
            new MemoryBossStore(),
            1_800_000_000_000,
            spawnPolicy: policy);
        var startup = schedule.StartupOrders().Select(o => o.NpcId).ToHashSet();
        Assert.Equal(WorldScheduledCount, startup.Count);

        var world = new ZoneWorld(includeEnabledMaps: true, bosses: schedule);
        var spawnable = catalog.Bosses.Where(policy.ShouldSpawn).Select(b => b.ObjectId).ToHashSet();
        foreach (int objectId in spawnable)
            Assert.Contains(objectId, world.NpcObjectIds);
        Assert.Equal(WorldScheduledCount, spawnable.Count);
    }

    [Fact]
    public void Regional_samples_spawn_at_imported_locations()
    {
        var world = new ZoneWorld(includeEnabledMaps: true);
        AssertBoss(world, 25_001, -54_416, 146_480, -2_887);
        AssertBoss(world, 29_019, 185_708, 114_298, -8_221);
        AssertBoss(world, 25_054, 113_432, 16_403, 3_960);
    }

    [Fact]
    public void Schedule_views_match_catalog_ids()
    {
        var catalog = BossCatalog.LoadDefault();
        var schedule = new BossSchedule(
            catalog,
            new MemoryBossStore(),
            1_800_000_000_000,
            spawnPolicy: new MapFilteredBossSpawnPolicy(includeEnabledMaps: true));
        var views = schedule.Views();
        Assert.Equal(catalog.Bosses.Count, views.Count);
        foreach (var definition in catalog.Bosses)
        {
            var view = views.Single(v => v.NpcId == definition.NpcId);
            Assert.Equal(definition.ObjectId, view.ObjectId);
            Assert.Equal(definition.Kind, view.Kind);
            Assert.Equal(definition.Enabled, view.Enabled);
        }
    }

    [Fact]
    public void Bounds_filter_keeps_enabled_rows_inside_the_box()
    {
        var catalog = BossCatalog.LoadDefault();
        var kutus = catalog.Get(25_001);
        Assert.Equal(-54_416, kutus.X);
        Assert.Equal(146_480, kutus.Y);
        var disabledRetail = kutus with { Enabled = false };
        var antharas = catalog.Get(29_019);
        var policy = new MapFilteredBossSpawnPolicy(includeEnabledMaps: true);

        try
        {
            // The old box started at Y 200000, which is north of Kutus (Y 146480), so the
            // disabled-row assert was false for a point the box never contained.
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_BOUNDS", "-80000,100000,-40000,180000");
            Assert.True(policy.ShouldSpawn(kutus));
            Assert.False(policy.ShouldSpawn(disabledRetail));
            Assert.False(policy.ShouldSpawn(antharas));
            Assert.False(policy.ShouldSpawn(catalog.Get(25_333)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_BOUNDS", null);
        }
    }

    static void AssertBoss(ZoneWorld world, int npcId, int x, int y, int z)
    {
        var definition = BossCatalog.LoadDefault().Get(npcId);
        Assert.True(world.TryGetEntityById(definition.ObjectId, out var entity), $"boss {npcId}");
        Assert.Equal(x, entity.X);
        Assert.Equal(y, entity.Y);
        Assert.Equal(z, entity.Z);
    }
}
