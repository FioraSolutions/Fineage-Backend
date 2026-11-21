using Fineage.Zone.Bosses;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class BossSpawnPolicyTests
{
    [Fact]
    public void Whole_world_policy_activates_interlude_rows_inside_the_map_filter()
    {
        var catalog = BossCatalog.LoadDefault();
        var policy = new MapFilteredBossSpawnPolicy(includeEnabledMaps: true);

        var kutus = catalog.Get(25_001);
        Assert.True(kutus.Enabled);
        Assert.True(policy.ShouldSpawn(kutus));

        var antharas = catalog.Get(29_019);
        Assert.True(policy.ShouldSpawn(antharas));

        var anakazel = catalog.Get(25_333);
        Assert.False(anakazel.HasRespawnWindow);
        Assert.False(policy.ShouldSpawn(anakazel));
    }

    [Fact]
    public void Datapack_policy_requires_json_enabled()
    {
        var kutus = BossCatalog.LoadDefault().Get(25_001);
        Assert.True(kutus.Enabled);
        Assert.True(DatapackBossSpawnPolicy.Instance.ShouldSpawn(kutus));

        var anakazel = BossCatalog.LoadDefault().Get(25_333);
        Assert.False(anakazel.Enabled);
        Assert.False(DatapackBossSpawnPolicy.Instance.ShouldSpawn(anakazel));
    }

    [Fact]
    public void Single_map_mode_respects_json_enabled_only()
    {
        var kutus = BossCatalog.LoadDefault().Get(25_001);
        Assert.False(new MapFilteredBossSpawnPolicy(includeEnabledMaps: false).ShouldSpawn(kutus));

        var anakazel = BossCatalog.LoadDefault().Get(25_333);
        Assert.False(new MapFilteredBossSpawnPolicy(includeEnabledMaps: false).ShouldSpawn(anakazel));
    }

    [Fact]
    public void Startup_orders_include_whole_world_bosses()
    {
        var schedule = new BossSchedule(
            BossCatalog.LoadDefault(),
            new MemoryBossStore(),
            1_800_000_000_000,
            spawnPolicy: new MapFilteredBossSpawnPolicy(includeEnabledMaps: true));
        var ids = schedule.StartupOrders().Select(o => o.NpcId).ToHashSet();
        Assert.Contains(25_001, ids);
        Assert.Contains(29_001, ids);
        Assert.DoesNotContain(25_333, ids);
    }
}