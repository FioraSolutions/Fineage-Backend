using Fineage.Accounts;
using Fineage.Login;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius AttackableAI idle walk: a monster with no target walks inside MaxDriftRange.</summary>
public sealed class ZoneRandomWalkTests
{
    sealed class AlwaysZero : Random
    {
        public override int Next(int maxValue) => 0;
        public override double NextDouble() => 0.25;
    }

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
            var world = new ZoneWorld(accounts: store, spawnCatalog: spawns, activeMapId: "walk", combatRandom: new AlwaysZero());
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
}
