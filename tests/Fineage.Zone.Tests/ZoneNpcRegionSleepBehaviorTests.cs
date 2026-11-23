using Fineage.Accounts;
using Fineage.Login;
using Fineage.Zone.Geo;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Game.Economy;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius WorldRegion sleep/wake: inactive tiles freeze idle AI and movement; waking preserves spawn posture and resumes behavior.
/// </summary>
public sealed class ZoneNpcRegionSleepBehaviorTests
{
    static async Task<(ZoneWorld World, ZoneEntity Player, ZoneSession Session)> EnterAsync(
        int playerX, int playerY, int playerZ, params SpawnNpcDefinition[] npcs)
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("sleep", "pass1234").Ok);
        var created = login.CreateHuman("sleep", "Scout", HumanClasses.Fighter);
        Assert.True(created.Ok);
        var ticket = login.Play("sleep", "pass1234", created.Value!.Name, HumanClasses.Fighter);
        var map = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["sleep"] = new("sleep", true, npcs)
        });
        var world = new ZoneWorld(
            new GridAoi(GridAoi.DefaultCellSize),
            accounts: store,
            spawnCatalog: map,
            activeMapId: "sleep",
            geo: GeoEngine.Empty,
            ledgerJournal: new MemoryLedgerJournal());
        var session = new ZoneSession();
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(session, out var player));
        player.X = playerX;
        player.Y = playerY;
        player.Z = playerZ;
        session.X = playerX;
        session.Y = playerY;
        session.Z = playerZ;
        world.TeleportPlayerForTests(session, playerX, playerY, playerZ);
        return (world, player, session);
    }

    [Fact]
    public async Task Teleport_into_sleeping_region_keeps_mobs_at_spawn_then_aggroes()
    {
        const int mobId = 9_200_100;
        int spawnX = 80_000, spawnY = 40_000, spawnZ = -3000;
        var mobDef = new SpawnNpcDefinition(
            mobId, 20001, "Gremlin", spawnX, spawnY, spawnZ, 0,
            true, 80, 80f, 40f, 30, 0, NpcType: "Monster", Level: 5, Aggressive: true, AggroRange: 450);

        var (world, _, session) = await EnterAsync(10_000, 10_000, spawnZ, mobDef);
        Assert.True(world.TryGetEntityById(mobId, out var sleeping));
        Assert.Equal(0, world.ActiveSetCounts.Thinking);
        Assert.False(sleeping.MoveActive);
        Assert.Equal(spawnX, sleeping.X);
        Assert.Equal(spawnY, sleeping.Y);

        world.TeleportPlayerForTests(session, spawnX + 200, spawnY, spawnZ);
        await world.RunCombatTickAsync(CancellationToken.None);
        Assert.True(world.PlayerActiveRegions.Count > 0);
        Assert.True(world.TryGetLivePlayer(session, out var nearPlayer));
        Assert.Contains(mobId, nearPlayer.Knownlist);

        sleeping.ProactiveAggroAtMs = 0;
        sleeping.GlobalAggro = 0;
        sleeping.NextAggroScanMs = 0;
        world.DamageNpcByForTest(nearPlayer, sleeping, 1);
        for (int i = 0; i < 20 && sleeping.AggroTargetId <= 0; i++)
            await world.RunCombatTickAsync(CancellationToken.None);
        Assert.Equal(nearPlayer.ObjectId, sleeping.AggroTargetId);
    }

    [Fact]
    public async Task Chase_across_inactive_tile_keeps_moving_while_aggroed()
    {
        const int mobId = 9_200_101;
        int y0 = 50_000, z = -3000;
        var mobDef = new SpawnNpcDefinition(
            mobId, 20001, "Gremlin", 60_000, y0, z, 0,
            true, 80, 120f, 40f, 30, 0, NpcType: "Monster", Level: 5, Aggressive: true, AggroRange: 450);

        var (world, player, _) = await EnterAsync(60_200, y0, z, mobDef);
        Assert.True(world.TryGetEntityById(mobId, out var mob));
        world.SetAggroTarget(mob, player.ObjectId);
        mob.AttackTimeoutAtMs = long.MaxValue;
        world.NoteNpcThinkJob(mob);

        world.TeleportPlayerForTests(player.Session!, mob.X - 2_500, y0, z);
        int xBefore = mob.X;
        bool moved = false;
        for (int i = 0; i < 40; i++)
        {
            await world.RunCombatTickAsync(CancellationToken.None);
            moved |= mob.MoveActive || mob.X != xBefore;
            if (mob.AggroTargetId <= 0)
                break;
        }

        Assert.Equal(player.ObjectId, mob.AggroTargetId);
        Assert.True(moved, "chasing mob keeps simulating movement after the player crosses an inactive map tile");
    }

    [Fact]
    public async Task Drifted_mob_return_home_starts_walking()
    {
        const int mobId = 9_200_102;
        int homeX = 70_000, homeY = 60_000, z = -3000;
        var mobDef = new SpawnNpcDefinition(
            mobId, 20001, "Gremlin", homeX, homeY, z, 0,
            true, 80, 80f, 40f, 30, 0, NpcType: "Monster", Level: 1);

        var (world, _, _) = await EnterAsync(homeX + 100, homeY, z, mobDef);
        Assert.True(world.TryGetEntityById(mobId, out var mob));
        int limit = ZoneNpcSettings.MaxDriftRange;
        mob.X = homeX + limit + 50;
        mob.Y = homeY;
        mob.HomeSet = true;
        mob.HomeX = homeX;
        mob.HomeY = homeY;
        mob.HomeZ = z;
        world.BumpNpcThinkForTests(mob);

        await world.RunCombatTickAsync(CancellationToken.None);
        Assert.True(mob.ReturningHome || mob.MoveActive);
    }
}