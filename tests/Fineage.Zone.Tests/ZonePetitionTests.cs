using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius petitions: open / cancel / chat via one GM queue (#930).</summary>
public sealed class ZonePetitionTests
{
    static async Task<(ZoneWorld World, ZoneSession Session, ZoneEntity Player)> Enter(
        MemoryAccountStore store, int characterId, string account, string name)
    {
        var world = new ZoneWorld(accounts: store, ledgerJournal: new MemoryLedgerJournal());
        world.PetitionRequireOnlineGm = false;
        var session = new ZoneSession();
        session.Bind(characterId, account, name, HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(session, out var player));
        return (world, session, player);
    }

    static (MemoryAccountStore Store, LoginService Login) NewStore()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("petition", "pass1234").Ok);
        return (store, login);
    }

    [Fact]
    public async Task Open_cancel_and_chat_reach_one_queue()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("petition", "Petitioner", HumanClasses.Fighter).Value!;
        var b = login.CreateHuman("petition", "GmHelper", HumanClasses.Fighter).Value!;
        ZoneAdmin.SetNamesForTest(["GmHelper"]);
        try
        {
            Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");
            var (world, s1, p1) = await Enter(store, a.Id, "petition", "Petitioner");
            var s2 = new ZoneSession();
            s2.Bind(b.Id, "petition", "GmHelper", HumanClasses.Fighter);
            Assert.True(await world.TryEnterAsync(s2, CancellationToken.None));
            Assert.True(world.TryGetLivePlayer(s2, out var gm));

            Assert.Null(await world.TryPetitionAsync(s1, new PetitionRequestMsg
            {
                Content = "Help stuck",
                Type = 1
            }, CancellationToken.None));
            Assert.True(world.Petitions.IsPlayerPending(p1.ObjectId));
            Assert.Equal(1, world.Petitions.PendingCount);
            int id = world.Petitions.TryGet(1, out var entry) ? entry.Id : 0;
            Assert.True(id > 0);

            Assert.Null(await world.TryAcceptPetitionAsync(s2, id, CancellationToken.None));
            Assert.True(world.Petitions.IsPlayerInConsultation(p1.ObjectId));
            Assert.True(world.Petitions.IsPlayerInConsultation(gm.ObjectId));

            Assert.Null(await world.TrySayAsync(s1, "stuck near gate", ChatProtocol.PetitionPlayer,
                DateTimeOffset.UtcNow, CancellationToken.None));
            Assert.True(world.Petitions.TryGet(id, out var live));
            Assert.Contains(live.Log, row => row.Text == "stuck near gate");

            Assert.Null(await world.TryPetitionCancelAsync(s2, CancellationToken.None));
            Assert.False(world.Petitions.IsPlayerInConsultation(p1.ObjectId));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, null);
            ZoneAdmin.SetNamesForTest(null);
        }
    }

    [Fact]
    public async Task Missing_petition_id_on_accept_is_logged_and_ignored()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("petition", "GhostGm", HumanClasses.Fighter).Value!;
        ZoneAdmin.SetNamesForTest(["GhostGm"]);
        try
        {
            Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");
            var (world, session, _) = await Enter(store, a.Id, "petition", "GhostGm");
            Assert.Null(await world.TryAcceptPetitionAsync(session, petitionId: 9999, CancellationToken.None));
            Assert.Equal(0, world.Petitions.PendingCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, null);
            ZoneAdmin.SetNamesForTest(null);
        }
    }

    [Fact]
    public async Task Petition_say_without_consultation_rejected()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("petition", "Lonely", HumanClasses.Fighter).Value!;
        var (world, session, player) = await Enter(store, a.Id, "petition", "Lonely");
        // Mobius: returns null error path but fans NotInPetitionChat (745) to the speaker.
        Assert.Null(await world.TrySayAsync(session, "hello?", ChatProtocol.PetitionPlayer,
            DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.False(world.Petitions.IsPlayerInConsultation(player.ObjectId));
    }

    [Fact]
    public async Task Cancel_pending_without_consultation()
    {
        var (store, login) = NewStore();
        var a = login.CreateHuman("petition", "CancelMe", HumanClasses.Fighter).Value!;
        var (world, session, player) = await Enter(store, a.Id, "petition", "CancelMe");
        Assert.Null(await world.TryPetitionAsync(session, new PetitionRequestMsg
        {
            Content = "nvm",
            Type = 2
        }, CancellationToken.None));
        Assert.True(world.Petitions.IsPlayerPending(player.ObjectId));
        Assert.Null(await world.TryPetitionCancelAsync(session, CancellationToken.None));
        Assert.False(world.Petitions.IsPlayerPending(player.ObjectId));
    }
}
