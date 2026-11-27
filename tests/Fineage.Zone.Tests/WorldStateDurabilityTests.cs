using System.Text.Json;
using Fineage.Game.Persistence;
using Fineage.Zone.Bosses;
using Fineage.Zone.Fishing;
using Fineage.Zone.Lottery;
using Fineage.Zone.Olympiad;
using Fineage.Zone.Persistence;
using Fineage.Zone.Punishment;
using Fineage.Zone.RaidBossPoints;
using Fineage.Zone.SevenSigns;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class WorldStateDurabilityTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);

    static readonly string[] SidecarFiles =
    [
        "boss-schedule.json",
        "seven-signs.json",
        "olympiad.json",
        "lottery.json",
        "raid-boss-points-store.json",
        "punishments.json",
        "monster-race-state.json",
        "fishing-championship.json"
    ];

    static string TempStateDirectory() =>
        Path.Combine(Path.GetTempPath(), "fineage-world-state-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Graceful_shutdown_flushes_every_stateful_sidecar()
    {
        string directory = TempStateDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var festival = new SevenSignsFestivalRuntime(SevenSignsFestivalCatalog.LoadDefault());
            var sevenSigns = SevenSignsEngine.OpenInStateDirectory(directory, Now, festival);
            var bosses = BossSchedule.OpenInStateDirectory(directory, Now);
            var olympiad = OlympiadBoard.OpenInStateDirectory(directory, Now);
            var raidBossPoints = RaidBossPointsManager.OpenInStateDirectory(directory);
            var punishments = PunishmentManager.OpenInStateDirectory(directory);

            var bench = new WorldBench(
                bosses: bosses,
                sevenSigns: sevenSigns,
                festivalRuntime: festival,
                olympiad: olympiad,
                punishments: punishments,
                raidBossPoints: raidBossPoints);
            long nowUnixMs = Now.ToUnixTimeMilliseconds();
            bench.World.UseLottery(LotteryBoard.OpenInStateDirectory(directory, nowUnixMs));
            bench.World.UseFishingChampionship(FishingChampionshipBoard.OpenInStateDirectory(directory, nowUnixMs));
            bench.World.UseMonsterRaceInStateDirectory(directory, nowUnixMs);

            Assert.Equal(WorldStateSystems.All, bench.World.StateFlushSystems);

            bench.World.ShutdownGracefully();

            Assert.Empty(bench.World.LastStateFlushFailures);
            foreach (string file in SidecarFiles)
            {
                string path = Path.Combine(directory, file);
                Assert.True(File.Exists(path), "shutdown did not write " + file);
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Registry_startup_guard_names_an_omitted_stateful_system()
    {
        var registry = new ShutdownFlushRegistry();
        foreach (string system in WorldStateSystems.All.Where(system => system != WorldStateSystems.FishingChampionship))
            registry.Register(system, static () => { });

        var ex = Assert.Throws<InvalidOperationException>(registry.AssertComplete);

        Assert.Contains(WorldStateSystems.FishingChampionship, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupt_sidecar_fails_startup_with_the_path_and_system()
    {
        string directory = TempStateDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "boss-schedule.json");
            File.WriteAllText(path, "{ truncated");

            var ex = Assert.Throws<StateFileException>(() => BossSchedule.OpenInStateDirectory(directory, Now));

            Assert.Equal("boss-schedule", ex.System);
            Assert.Equal(path, ex.FilePath);
            Assert.Contains(path, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
