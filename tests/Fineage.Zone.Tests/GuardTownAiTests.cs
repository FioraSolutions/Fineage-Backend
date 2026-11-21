using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Login;
using Fineage.Zone;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius <c>Guard</c>: karma scan, optional aggressive-monster scan, and 500-range ally hate on damage.</summary>
public sealed class GuardTownAiTests
{
    static SpawnCatalog Map(string id, params SpawnNpcDefinition[] npcs) =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { [id] = new(id, true, npcs) });

    static SpawnNpcDefinition Guard(int objectId, int x, int y, int z) =>
        new(objectId, 30040, "Leon", x, y, z, 0, false, 3290, 160f, 50f, 0, 0, "Guard", "Guard",
            Level: 80, Aggressive: true, AggroRange: 1000);

    static SpawnNpcDefinition Gremlin(int objectId, int x, int y, int z) =>
        new(objectId, 20001, "Gremlin", x, y, z, 0, true, 80, 80f, 40f, 30, 0, NpcType: "Monster", Level: 1,
            Aggressive: true, AggroRange: 400);

    static void PrimeAggro(ZoneWorld world, params int[] ids)
    {
        foreach (int id in ids)
        {
            if (!world.TryGetEntityById(id, out var npc))
                continue;
            npc.ProactiveAggroAtMs = 0;
            npc.GlobalAggro = 0;
        }
    }

    [Fact]
    public async Task Guard_aggroes_karma_player_not_innocent()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("gpk", "pass1234").Ok);
        var created = login.CreateHuman("gpk", "PkBait", HumanClasses.Fighter);
        Assert.True(created.Ok);

        async Task<(ZoneWorld world, ZoneEntity player)> EnterAsync(SpawnCatalog catalog)
        {
            var ticket = login.Play("gpk", "pass1234", created.Value!.Name, HumanClasses.Fighter);
            var world = new ZoneWorld(accounts: store, spawnCatalog: catalog, activeMapId: "gpk",
                ledgerJournal: new MemoryLedgerJournal());
            var session = new ZoneSession();
            session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
            Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
            Assert.True(world.TryGetLivePlayer(session, out var player));
            return (world, player);
        }

        var (_, probe) = await EnterAsync(Map("gpk"));
        int x = probe.X, y = probe.Y, z = probe.Z;
        var (world, player) = await EnterAsync(Map("gpk", Guard(9_100_100, x + 80, y, z)));

        PrimeAggro(world, 9_100_100);
        await world.RunCombatTickAsync(CancellationToken.None);
        Assert.True(world.TryGetEntityById(9_100_100, out var guard));
        Assert.Equal(0, guard.AggroTargetId);

        player.Karma = 100;
        guard.NextAggroScanMs = 0;
        await world.RunCombatTickAsync(CancellationToken.None);
        Assert.Equal(player.ObjectId, guard.AggroTargetId);
    }

    [Fact]
    public async Task Guard_aggroes_aggressive_monster_when_enabled()
    {
        string? prev = Environment.GetEnvironmentVariable(ZoneNpcSettings.GuardAttackAggroMobVariable);
        Environment.SetEnvironmentVariable(ZoneNpcSettings.GuardAttackAggroMobVariable, "true");
        try
        {
            var store = MemoryAccountStore.InMemory();
            var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
            Assert.True(login.Register("gmob", "pass1234").Ok);
            var created = login.CreateHuman("gmob", "MobWatch", HumanClasses.Fighter);
            Assert.True(created.Ok);

            async Task<(ZoneWorld world, ZoneEntity player)> EnterAsync(SpawnCatalog catalog)
            {
                var ticket = login.Play("gmob", "pass1234", created.Value!.Name, HumanClasses.Fighter);
                var world = new ZoneWorld(accounts: store, spawnCatalog: catalog, activeMapId: "gmob",
                    ledgerJournal: new MemoryLedgerJournal());
                var session = new ZoneSession();
                session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
                Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
                Assert.True(world.TryGetLivePlayer(session, out var player));
                return (world, player);
            }

            var (_, probe) = await EnterAsync(Map("gmob"));
            int x = probe.X, y = probe.Y, z = probe.Z;
            var (world, _) = await EnterAsync(Map("gmob",
                Guard(9_100_110, x, y, z),
                Gremlin(9_100_111, x + 80, y, z)));

            PrimeAggro(world, 9_100_110, 9_100_111);
            await world.RunCombatTickAsync(CancellationToken.None);
            Assert.True(world.TryGetEntityById(9_100_110, out var guard));
            Assert.Equal(9_100_111, guard.AggroTargetId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ZoneNpcSettings.GuardAttackAggroMobVariable, prev);
        }
    }

    [Fact]
    public async Task Damaging_one_guard_alerts_nearby_guards()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("ghelp", "pass1234").Ok);
        var created = login.CreateHuman("ghelp", "Rogue", HumanClasses.Fighter);
        Assert.True(created.Ok);

        async Task<(ZoneWorld world, ZoneEntity player)> EnterAsync(SpawnCatalog catalog)
        {
            var ticket = login.Play("ghelp", "pass1234", created.Value!.Name, HumanClasses.Fighter);
            var world = new ZoneWorld(accounts: store, spawnCatalog: catalog, activeMapId: "ghelp",
                ledgerJournal: new MemoryLedgerJournal());
            var session = new ZoneSession();
            session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
            Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
            Assert.True(world.TryGetLivePlayer(session, out var player));
            return (world, player);
        }

        var (_, probe) = await EnterAsync(Map("ghelp"));
        int x = probe.X, y = probe.Y, z = probe.Z;
        var (world, player) = await EnterAsync(Map("ghelp",
            Guard(9_100_120, x, y, z),
            Guard(9_100_121, x + 120, y, z),
            Guard(9_100_122, x + 800, y, z)));

        Assert.True(world.TryGetEntityById(9_100_120, out var primary));
        world.DamageNpcByForTest(player, primary, 1);
        await world.RunCombatTickAsync(CancellationToken.None);

        Assert.True(world.TryGetEntityById(9_100_121, out var ally));
        Assert.True(world.TryGetEntityById(9_100_122, out var far));
        Assert.True(ally.HateList.ContainsKey(player.ObjectId));
        Assert.False(far.HateList.ContainsKey(player.ObjectId));
    }
}