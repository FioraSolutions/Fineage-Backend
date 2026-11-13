using Fineage.Accounts;
using Fineage.Login;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius AttackableAI idle walk: a monster with no target walks inside MaxDriftRange.</summary>
public sealed class ZoneRandomWalkTests
{
    /// <summary>
    /// Mobius roll that lands inside drift: Rnd.get(30)==0, Rnd.get(600)==300,
    /// Rnd.get(300, 600) inclusive == 300. Offset is then (0, -300).
    /// </summary>
    sealed class MobiusWalkRoll : Random
    {
        public override int Next(int maxValue) => maxValue == RandomWalkRateExposed ? 0 : 300;
        public override double NextDouble() => 0;
    }

    const int RandomWalkRateExposed = 30;

    [Fact]
    public async Task Idle_monster_walks_away_from_its_spawn()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("walk", "pass1234").Ok);
        var created = login.CreateHuman("walk", "Walker", HumanClasses.Fighter);
        Assert.True(created.Ok);

        async Task<(ZoneWorld World, ZoneEntity Player)> Enter(SpawnCatalog spawns)
        {
            var ticket = login.Play("walk", "pass1234", created.Value!.Name, HumanClasses.Fighter);
            var world = new ZoneWorld(accounts: store, spawnCatalog: spawns, activeMapId: "walk", combatRandom: new MobiusWalkRoll());
            var session = new ZoneSession();
            session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
            Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
            Assert.True(world.TryGetLivePlayer(session, out var player));
            return (world, player);
        }

        static SpawnCatalog Map(params SpawnNpcDefinition[] npcs) =>
            SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { ["walk"] = new("walk", true, npcs) });

        var (_, probe) = await Enter(Map());
        int id = 9_200_001;
        var mob = new SpawnNpcDefinition(
            id, 20001, "Gremlin", probe.X + 8000, probe.Y, probe.Z, 0,
            true, 80, 80f, 40f, 30, 0, NpcType: "Monster", Level: 1, Aggressive: false);

        var (world, _) = await Enter(Map(mob));
        Assert.True(world.TryGetEntityById(id, out var before));
        Assert.False(before.MoveActive);
        int homeX = before.HomeX;
        int homeY = before.HomeY;

        await world.RunCombatTickAsync(CancellationToken.None);

        Assert.True(world.TryGetEntityById(id, out var after));
        Assert.True(after.MoveActive);
        Assert.False(after.Running);
        long dx = after.MoveDestX - (long)homeX;
        long dy = after.MoveDestY - (long)homeY;
        Assert.True(dx * dx + dy * dy <= (long)ZoneNpcSettings.MaxDriftRange * ZoneNpcSettings.MaxDriftRange);
    }

    [Fact]
    public async Task Monster_outside_the_player_view_does_not_walk()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("far", "pass1234").Ok);
        var created = login.CreateHuman("far", "Farmer", HumanClasses.Fighter);
        Assert.True(created.Ok);
        var ticket = login.Play("far", "pass1234", created.Value!.Name, HumanClasses.Fighter);
        var probe = new ZoneWorld(accounts: store, spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["far"] = new("far", true, [])
        }), activeMapId: "far", aoi: new Fineage.Zone.Interest.GridAoi());
        var session = new ZoneSession();
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await probe.TryEnterAsync(session, CancellationToken.None));
        Assert.True(probe.TryGetLivePlayer(session, out var player));

        int id = 9_200_010;
        var mob = new SpawnNpcDefinition(
            id, 20001, "Gremlin", player.X + 50_000, player.Y, player.Z, 0,
            true, 80, 80f, 40f, 30, 0, NpcType: "Monster", Level: 1);
        var world = new ZoneWorld(accounts: store, spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["far"] = new("far", true, [mob])
        }), activeMapId: "far", aoi: new Fineage.Zone.Interest.GridAoi(), combatRandom: new MobiusWalkRoll());
        var session2 = new ZoneSession();
        session2.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session2, CancellationToken.None));

        await world.RunCombatTickAsync(CancellationToken.None);
        Assert.True(world.TryGetEntityById(id, out var after));
        Assert.False(after.MoveActive);
    }

    [Fact]
    public async Task Minion_steps_toward_a_leader_that_ran_off()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("min", "pass1234").Ok);
        var created = login.CreateHuman("min", "Miner", HumanClasses.Fighter);
        Assert.True(created.Ok);
        var ticket = login.Play("min", "pass1234", created.Value!.Name, HumanClasses.Fighter);
        var probe = new ZoneWorld(accounts: store, spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["min"] = new("min", true, [])
        }), activeMapId: "min");
        var session = new ZoneSession();
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await probe.TryEnterAsync(session, CancellationToken.None));
        Assert.True(probe.TryGetLivePlayer(session, out var player));

        var leader = new SpawnNpcDefinition(9_200_020, 20001, "Leader", player.X + 100, player.Y, player.Z, 0,
            true, 80, 80f, 40f, 30, 0, NpcType: "Monster", Level: 1);
        var minion = new SpawnNpcDefinition(9_200_021, 20001, "Minion", player.X + 120, player.Y, player.Z, 0,
            true, 80, 80f, 40f, 30, 0, NpcType: "Monster", Level: 1);
        var world = new ZoneWorld(accounts: store, spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["min"] = new("min", true, [leader, minion])
        }), activeMapId: "min");
        var session2 = new ZoneSession();
        session2.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session2, CancellationToken.None));
        Assert.True(world.TryGetEntityById(9_200_020, out var lead));
        Assert.True(world.TryGetEntityById(9_200_021, out var pet));
        pet.LeaderObjectId = lead.ObjectId;
        // Inside MaxDriftRange so think does not send the leader home, and far enough that the minion must follow.
        lead.X += 250;
        lead.Running = true;

        await world.RunCombatTickAsync(CancellationToken.None);
        Assert.True(pet.MoveActive);
        Assert.True(pet.Running);
    }

    [Fact]
    public async Task Attack_timeout_sends_the_monster_home()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("tout", "pass1234").Ok);
        var created = login.CreateHuman("tout", "Timer", HumanClasses.Fighter);
        Assert.True(created.Ok);
        var ticket = login.Play("tout", "pass1234", created.Value!.Name, HumanClasses.Fighter);
        var probe = new ZoneWorld(accounts: store, spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["tout"] = new("tout", true, [])
        }), activeMapId: "tout");
        var session = new ZoneSession();
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await probe.TryEnterAsync(session, CancellationToken.None));
        Assert.True(probe.TryGetLivePlayer(session, out var player));

        int id = 9_200_030;
        var mob = new SpawnNpcDefinition(id, 20001, "Gremlin", player.X + 80, player.Y, player.Z, 0,
            true, 80, 80f, 40f, 30, 0, NpcType: "Monster", Level: 1, Aggressive: true, AggroRange: 400);
        var world = new ZoneWorld(accounts: store, spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["tout"] = new("tout", true, [mob])
        }), activeMapId: "tout");
        var session2 = new ZoneSession();
        session2.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session2, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(session2, out player));
        Assert.True(world.TryGetEntityById(id, out var npc));
        int homeX = npc.HomeX;
        npc.X = homeX + 800;
        world.SetAggroTarget(npc, player.ObjectId);
        npc.AttackTimeoutAtMs = 1;

        await world.RunCombatTickAsync(CancellationToken.None);
        Assert.Equal(0, npc.AggroTargetId);
        Assert.Equal(homeX, npc.X);
    }
}
