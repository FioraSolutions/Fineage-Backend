using System.Reflection;
using Fineage.Accounts;
using Fineage.Login;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius aggressive scan (aggroRange), AggroList hate, clan help and Net attack stance.</summary>
public sealed class ZoneAggroTests
{
    public class StanceRecorder : DispatchProxy
    {
        public readonly List<(int ObjectId, bool InStance)> Stances = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(IWorldEventPublisher.FanoutAttackStance))
                Stances.Add(((int)args![1]!, (bool)args[2]!));
            return method?.ReturnType == typeof(bool) ? false : null;
        }
    }

    [Fact]
    public void Damage_hate_follows_mobius_addDamageHate()
    {
        Assert.Equal(100 * 100 / 17, ZoneWorld.DamageHate(100, 10));
        Assert.Equal(1, ZoneWorld.DamageHate(0, 80));
    }

    [Fact]
    public async Task Aggressive_mob_attacks_in_range_calls_clan_and_sends_stance()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("aggro", "pass1234").Ok);
        var created = login.CreateHuman("aggro", "Bait", HumanClasses.Fighter);
        Assert.True(created.Ok);

        async Task<(ZoneWorld, ZoneEntity)> Enter(SpawnCatalog spawns)
        {
            var ticket = login.Play("aggro", "pass1234", created.Value!.Name, HumanClasses.Fighter);
            var world = new ZoneWorld(accounts: store, spawnCatalog: spawns, activeMapId: "aggro");
            var session = new ZoneSession();
            session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
            Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
            Assert.True(world.TryGetLivePlayer(session, out var player));
            return (world, player);
        }

        static SpawnCatalog Map(params SpawnNpcDefinition[] npcs) =>
            SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { ["aggro"] = new("aggro", true, npcs) });

        var (_, probe) = await Enter(Map());
        int x = probe.X, y = probe.Y, z = probe.Z;
        SpawnNpcDefinition Mob(int id, int dx, bool aggressive, string clan) =>
            new(id, 90000 + id, "Mob" + id, x + dx, y, z, 0, true, 500, 80f, 40f, 60, 0, "", "Monster", Level: 5,
                AttackRange: 40, AttackSpeed: 253, Aggressive: aggressive, AggroRange: 400, ClanHelpRange: 300, Clans: [clan]);

        var (world, player) = await Enter(Map(
            Mob(9_100_001, 200, aggressive: true, "WOLF"),
            Mob(9_100_002, 450, aggressive: false, "WOLF"),   // 250 from the caller: clan help
            Mob(9_100_003, 480, aggressive: false, "ORC"),    // other clan: stays idle
            Mob(9_100_004, 2000, aggressive: true, "WOLF")));  // outside aggroRange
        var bridge = DispatchProxy.Create<IWorldEventPublisher, StanceRecorder>();
        world.NetBridge = bridge;

        await world.RunCombatTickAsync(CancellationToken.None);

        int Target(int id) => world.TryGetEntityById(id, out var e) ? e.AggroTargetId : -1;
        Assert.Equal(player.ObjectId, Target(9_100_001));
        Assert.Equal(player.ObjectId, Target(9_100_002));
        Assert.Equal(0, Target(9_100_003));
        Assert.Equal(0, Target(9_100_004));
        Assert.True(world.TryGetEntityById(9_100_001, out var caller));
        Assert.True(caller.HateList[player.ObjectId] >= 1);
        var stances = ((StanceRecorder)(object)bridge).Stances;
        Assert.Contains((9_100_001, true), stances);
    }
}
