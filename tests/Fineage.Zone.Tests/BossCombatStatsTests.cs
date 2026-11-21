using Fineage.Zone.Bosses;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Raid bosses materialize through the same npc template pipeline as fixed spawns.</summary>
public sealed class BossCombatStatsTests
{
    const int GreyclawNpcId = 25_001;
    const int ObjectId = 80_001;

    [Fact]
    public void Materialized_raid_boss_carries_template_combat_stats()
    {
        var (min, max) = BossCatalog.RaidWindow(1200, 600);
        var schedule = new BossSchedule(
            BossCatalog.From([new BossDefinition(
                GreyclawNpcId,
                BossKind.Raid,
                true,
                ObjectId,
                "Greyclaw Kutus",
                "Raid Boss",
                "RaidBoss",
                -54_416,
                146_480,
                -2887,
                0,
                true,
                100,
                40,
                170,
                50,
                min,
                max,
                BossLifeStatus.Alive)]),
            new MemoryBossStore(),
            1_800_000_000_000);
        var bench = new WorldBench(
            spawns: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
            {
                ["empty"] = new("empty", true, [])
            }),
            mapId: "empty",
            bosses: schedule);
        var world = bench.World;

        Assert.True(world.TryGetEntityById(ObjectId, out var boss));
        Assert.Equal("RaidBoss", boss.NpcType);

        var template = NpcCatalog.LoadDefault().Get(GreyclawNpcId);
        Assert.Equal(template.PDef, boss.PDef, 3);
        Assert.Equal(template.PAtk, boss.PAtk, 3);
        Assert.Equal(template.Level, boss.Level);
        Assert.True(boss.PDef > 0);

        _ = world.NpcPDefForTest(boss);

        var player = new ZoneEntity
        {
            ObjectId = 1,
            Kind = ZoneEntityKind.Player,
            ClassId = 0,
            Level = 20,
            X = boss.X,
            Y = boss.Y,
            Z = boss.Z,
            MaxHp = 500,
            CurHp = 500,
            MaxMp = 200,
            CurMp = 200
        };
        world.PlaceForTests(player);
        var (damage, _) = world.RollPlayerMeleeForTest(player, boss);
        Assert.True(damage > 0);
    }

    [Fact]
    public void Boss_spawn_row_matches_shared_FromTemplate_pipeline()
    {
        var template = NpcCatalog.LoadDefault().Get(GreyclawNpcId);
        var row = SpawnCatalog.FromTemplate(ObjectId, template, 1, 2, 3, 0);
        Assert.Equal(template.PDef, row.PDef, 3);
        Assert.Equal(template.PAtk, row.PAtk, 3);
        Assert.NotEqual(ZoneCombat.GremlinPDef, row.PDef);
    }
}