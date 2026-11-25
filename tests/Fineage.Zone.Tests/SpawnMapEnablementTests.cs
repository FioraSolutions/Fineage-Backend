using Fineage.Zone.Combat;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class SpawnMapEnablementTests
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "deploy")) &&
                File.Exists(Path.Combine(dir.FullName, "Fineage.Backend.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("could not locate the repository root");
    }

    [Fact]
    public void Datapack_enables_all_spawn_json_maps()
    {
        var catalog = SpawnCatalog.LoadDefault();
        // Load-test maps (loadtest_*) ship disabled and are opted in via FINEAGE_ZONE_EXTRA_MAPS; every world map is on.
        var world = catalog.Maps.Where(kv => !kv.Key.StartsWith("loadtest_", StringComparison.Ordinal)).ToList();
        int enabled = world.Count(kv => kv.Value.Enabled);
        Assert.InRange(enabled, 180, 200);
        Assert.Equal(world.Count, enabled);
        Assert.All(catalog.Maps.Where(kv => kv.Key.StartsWith("loadtest_", StringComparison.Ordinal)), kv => Assert.False(kv.Value.Enabled));
    }

    [Fact]
    public void Production_env_example_keeps_full_world_without_map_whitelist()
    {
        string example = Path.Combine(RepoRoot(), "deploy", "fineage.env.example");
        string text = File.ReadAllText(example);
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
                continue;
            Assert.DoesNotContain("FINEAGE_ZONE_MAPS=", trimmed);
            Assert.DoesNotContain("FINEAGE_ZONE_BOUNDS=", trimmed);
        }
        Assert.Contains("FINEAGE_ZONE_AOI_CELL=3000", text, StringComparison.Ordinal);
        Assert.Contains("spawn-world-coverage.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Include_enabled_maps_boot_is_on_when_env_is_unset()
    {
        string? prior = Environment.GetEnvironmentVariable("FINEAGE_ZONE_INCLUDE_ENABLED_MAPS");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_INCLUDE_ENABLED_MAPS", null);
            Assert.False(ZoneMapFilter.IsExplicitlyOff(Environment.GetEnvironmentVariable("FINEAGE_ZONE_INCLUDE_ENABLED_MAPS")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_INCLUDE_ENABLED_MAPS", prior);
        }
    }

    [Fact]
    public void Hosted_world_day_night_tick_swaps_datapack_period_spawns()
    {
        var catalog = SpawnCatalog.LoadDefault();
        var map = catalog.GetMap("others_21_16");
        Assert.True(map.Enabled);
        var dayRow = map.Npcs.First(n => n.PeriodOfDay == SpawnPeriodOfDay.Day);
        var nightRow = map.Npcs.First(n => n.PeriodOfDay == SpawnPeriodOfDay.Night);

        var refTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long refMs = refTime.ToUnixTimeMilliseconds();
        var gameTime = new ZoneGameTime(refMs);
        long dayMs = refMs + ZoneGameTime.MillisPerIgDay * 10 / 24;
        long nightMs = refMs + ZoneGameTime.MillisPerIgDay * 2 / 24;

        var clock = new SteppedSpawnClock(DateTimeOffset.FromUnixTimeMilliseconds(dayMs));
        var world = new ZoneWorld(
            spawnCatalog: catalog,
            includeEnabledMaps: true,
            clock: () => clock.Now,
            gameTime: gameTime,
            spawnRandom: new Random(1));

        Assert.Contains(dayRow.ObjectId, world.NpcObjectIds);
        Assert.DoesNotContain(nightRow.ObjectId, world.NpcObjectIds);

        clock.Set(DateTimeOffset.FromUnixTimeMilliseconds(nightMs));
        world.AdvanceSpawnsForTests();

        Assert.Contains(nightRow.ObjectId, world.NpcObjectIds);
        Assert.DoesNotContain(dayRow.ObjectId, world.NpcObjectIds);
    }

    sealed class SteppedSpawnClock(DateTimeOffset start)
    {
        public DateTimeOffset Now { get; private set; } = start;
        public void Set(DateTimeOffset at) => Now = at;
    }
}
