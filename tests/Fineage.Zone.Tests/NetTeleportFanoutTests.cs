using Fineage.Game.Services;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Net teleport feedback: a revive or gatekeeper relocation has to reach the moving client and
/// every peer that had it in view, or the client keeps rendering the old spot.
/// </summary>
public sealed class NetTeleportFanoutTests
{
    [Fact]
    public async Task Revive_to_village_fans_one_teleport_with_the_restart_heading()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        Assert.Null(await kit.World.TryAdminKillPlayerAsync(session, CancellationToken.None));
        Assert.Null(await kit.World.TryReviveAsync(
            session, new ReviveRequestMsg { Action = DeathProtocol.ReviveToVillage }, CancellationToken.None));

        var frames = rec.Of(nameof(INetPresenceBridge.FanoutTeleportToLocation));
        var only = Assert.Single(frames);
        var msg = Assert.IsType<TeleportToLocationNetMsg>(only[1]);
        Assert.Equal(player.ObjectId, msg.ObjectId);
        Assert.Equal(TalkingIslandRestart.X, msg.X);
        Assert.Equal(TalkingIslandRestart.Y, msg.Y);
        Assert.Equal(TalkingIslandRestart.Z, msg.Z);
        Assert.Equal(TalkingIslandRestart.Heading, msg.Heading);
        Assert.Contains(player.ObjectId, Assert.IsAssignableFrom<IReadOnlyList<int>>(only[0]));
    }

    [Fact]
    public async Task Gatekeeper_bypass_takes_the_fee_and_fans_the_jump_to_the_owner()
    {
        var (world, session) = await GatekeeperWorldAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        world.NetBridge = bridge;

        int marker = session.ObjectId;
        Assert.Null(await world.TryBypassAsync(session, GateObjectId, "teleport NORMAL 0", CancellationToken.None));

        var frames = rec.Of(nameof(INetPresenceBridge.FanoutTeleportToLocation));
        var only = Assert.Single(frames);
        var msg = Assert.IsType<TeleportToLocationNetMsg>(only[1]);
        Assert.Equal(marker, msg.ObjectId);
        Assert.Equal(100, msg.X);
        Assert.Equal(0, msg.Y);
        Assert.Contains(marker, Assert.IsAssignableFrom<IReadOnlyList<int>>(only[0]));
    }

    const int GateObjectId = 61000;

    static async Task<(ZoneWorld World, ZoneSession Session)> GatekeeperWorldAsync()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("gate", "secret1", "Rook", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);
        Assert.True(store.TryReplaceItems(ticket.Value.Account, ticket.Value.CharacterId, [new StoredItem(57, 50_000, -1)]));
        Assert.True(store.TrySavePresence(ticket.Value.Account, ticket.Value.CharacterId, 0, 0, 0, null));

        var map = new SpawnMapDefinition(
            SpawnCatalog.DefaultMapId,
            true,
            [new SpawnNpcDefinition(GateObjectId, 30006, "Roxxy", 0, 0, 0, 0, false, 100, 80f, 40f, 60, 0, "Gatekeeper", "Teleporter")]);
        var world = new ZoneWorld(
            spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { [SpawnCatalog.DefaultMapId] = map }),
            accounts: store,
            teleports: FreeTeleports(),
            teleportOffset: 0);

        var session = new ZoneSession();
        session.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        return (world, session);
    }

    static TeleportCatalog FreeTeleports() => TeleportCatalog.Parse(
        """
        {
          "gatekeepers": [
            {
              "npcId": 30006,
              "lists": [
                {
                  "name": "NORMAL",
                  "type": "NORMAL",
                  "locations": [
                    { "id": 0, "name": "Near", "x": 100, "y": 0, "z": 0, "feeCount": 0 }
                  ]
                }
              ]
            }
          ]
        }
        """,
        "test");
}
