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

        var halisha = catalog.Get(25_339);
        Assert.True(halisha.Enabled);
        Assert.False(halisha.HasWorldSpawnPoint);
        Assert.False(policy.ShouldSpawn(halisha));

        var venom = catalog.Get(29_054);
        Assert.True(venom.Enabled);
        Assert.True(venom.HasWorldSpawnPoint);
        Assert.False(venom.HasRespawnWindow);
        Assert.True(policy.ShouldSpawn(venom));

        var frintezza = catalog.Get(29_045);
        Assert.True(frintezza.Enabled);
        Assert.False(frintezza.HasWorldSpawnPoint);
        Assert.False(policy.ShouldSpawn(frintezza));
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

    /// <summary>
    /// #926: Gigantic Chaos Golem is spawned by Mobius <c>DrChaos</c> at 96320,-110912,-3328 —
    /// not by the raid schedule. Keep the row disabled at the origin so it never materializes at 0,0,0.
    /// </summary>
    [Fact]
    public void Dr_chaos_golem_stays_script_owned_without_schedule_spawn()
    {
        var golem = BossCatalog.LoadDefault().Get(25_512);
        Assert.False(golem.Enabled);
        Assert.False(golem.HasWorldSpawnPoint);
        Assert.False(DatapackBossSpawnPolicy.Instance.ShouldSpawn(golem));
        Assert.False(new MapFilteredBossSpawnPolicy(includeEnabledMaps: true).ShouldSpawn(golem));

        var schedule = new BossSchedule(
            BossCatalog.LoadDefault(),
            new MemoryBossStore(),
            1_800_000_000_000,
            spawnPolicy: new MapFilteredBossSpawnPolicy(includeEnabledMaps: true));
        Assert.DoesNotContain(25_512, schedule.StartupOrders().Select(o => o.NpcId));
    }
}