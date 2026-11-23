using Fineage.Accounts;
using Fineage.Login;
using Fineage.Game.Economy;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneRegionWakeBudgetTests
{
    [Fact]
    public async Task Region_activation_joins_think_set_incrementally()
    {
        const string varName = "FINEAGE_ZONE_REGION_WAKE_BUDGET";
        string? prior = Environment.GetEnvironmentVariable(varName);
        Environment.SetEnvironmentVariable(varName, "12");
        try
        {
            const int mobBase = 9_300_000;
            int spawnX = 100_000, spawnY = 100_000, spawnZ = -3000;
            var npcs = new SpawnNpcDefinition[40];
            for (int i = 0; i < npcs.Length; i++)
            {
                npcs[i] = new SpawnNpcDefinition(
                    mobBase + i, 20001, "Gremlin", spawnX + (i % 8) * 80, spawnY + (i / 8) * 80, spawnZ, 0,
                    true, 80, 80f, 40f, 30, 0, NpcType: "Monster", Level: 1, Aggressive: false);
            }

            var store = MemoryAccountStore.InMemory();
            var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
            Assert.True(login.Register("wake", "pass1234").Ok);
            var created = login.CreateHuman("wake", "Scout", HumanClasses.Fighter);
            Assert.True(created.Ok);
            var ticket = login.Play("wake", "pass1234", created.Value!.Name, HumanClasses.Fighter);
            var map = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
            {
                ["wake"] = new("wake", true, npcs)
            });
            var world = new ZoneWorld(
                new GridAoi(GridAoi.DefaultCellSize),
                accounts: store,
                spawnCatalog: map,
                activeMapId: "wake",
                ledgerJournal: new MemoryLedgerJournal());
            var session = new ZoneSession();
            session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
            Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
            world.MovePlayerCoordsForTests(session, 10_000, 10_000, spawnZ);
            await world.RunCombatTickAsync(CancellationToken.None);
            Assert.Equal(0, world.ActiveSetCounts.Thinking);

            world.MovePlayerCoordsForTests(session, spawnX + 200, spawnY, spawnZ);
            await world.RunCombatTickAsync(CancellationToken.None);

            int thinking = world.ActiveSetCounts.Thinking;
            Assert.InRange(thinking, 1, 12);
            Assert.True(world.PendingRegionWakeCount >= npcs.Length - thinking);
        }
        finally
        {
            if (prior == null)
                Environment.SetEnvironmentVariable(varName, null);
            else
                Environment.SetEnvironmentVariable(varName, prior);
        }
    }
}