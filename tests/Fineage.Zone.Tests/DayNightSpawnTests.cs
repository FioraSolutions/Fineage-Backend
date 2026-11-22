using Fineage.Zone.Combat;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class DayNightSpawnTests
{
    const int NightMobId = 9_000_777;
    const int DayMobId = 9_000_778;

    [Fact]
    public void FromLocalMidnight_uses_anchor_offset_not_process_timezone()
    {
        var utcMidnight = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long refMs = utcMidnight.ToUnixTimeMilliseconds();
        var gameTime = ZoneGameTime.FromLocalMidnight(utcMidnight);
        long dayMs = refMs + ZoneGameTime.MillisPerIgDay * 10 / 24;
        Assert.False(gameTime.IsNight(dayMs));
        Assert.True(gameTime.IsNight(refMs + ZoneGameTime.MillisPerIgDay * 2 / 24));
    }

    [Fact]
    public void Night_only_spawn_appears_after_ig_night_begins()
    {
        var refTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long refMs = refTime.ToUnixTimeMilliseconds();
        var gameTime = new ZoneGameTime(refMs);
        // IG hour 2 = night; IG hour 10 = day (Mobius: night when hour &lt; 6).
        long nightMs = refMs + ZoneGameTime.MillisPerIgDay * 2 / 24;
        long dayMs = refMs + ZoneGameTime.MillisPerIgDay * 10 / 24;

        var catalog = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            [SpawnCatalog.DefaultMapId] = new(
                SpawnCatalog.DefaultMapId,
                Enabled: true,
                [
                    new SpawnNpcDefinition(
                        NightMobId, 20001, "Night Gremlin", 100, 200, -3000, 0,
                        true, 100, 80f, 40f, 60, 0, PeriodOfDay: SpawnPeriodOfDay.Night),
                    new SpawnNpcDefinition(
                        DayMobId, 20001, "Day Gremlin", 120, 200, -3000, 0,
                        true, 100, 80f, 40f, 60, 0, PeriodOfDay: SpawnPeriodOfDay.Day)
                ])
        });

        var clock = new SteppedClock(ManualSimulationDriver.FixedStartUtc.AddMilliseconds(dayMs - refMs));
        var world = new ZoneWorld(
            spawnCatalog: catalog,
            clock: () => clock.Now,
            gameTime: gameTime,
            spawnRandom: new Random(1),
            allowAmbientBoats: false);

        Assert.Contains(DayMobId, world.NpcObjectIds);
        Assert.DoesNotContain(NightMobId, world.NpcObjectIds);

        clock.Set(ManualSimulationDriver.FixedStartUtc.AddMilliseconds(nightMs - refMs));
        world.AdvanceSpawnsForTests();

        Assert.Contains(NightMobId, world.NpcObjectIds);
        Assert.DoesNotContain(DayMobId, world.NpcObjectIds);
    }

    sealed class SteppedClock(DateTimeOffset start)
    {
        public DateTimeOffset Now { get; private set; } = start;
        public void Set(DateTimeOffset at) => Now = at;
    }
}