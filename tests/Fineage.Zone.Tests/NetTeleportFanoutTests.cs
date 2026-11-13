using Fineage.Game.Services;
using Fineage.Accounts;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Interest;
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
    public async Task Revive_to_village_fans_one_teleport_to_the_nearest_town()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        int deathX = player.X, deathY = player.Y;
        Assert.Null(await kit.World.TryAdminKillPlayerAsync(session, CancellationToken.None));
        Assert.Null(await kit.World.TryReviveAsync(session, DeathProtocol.ReviveToVillage, CancellationToken.None));

        var frames = rec.Of(nameof(IWorldEventPublisher.FanoutTeleportToLocation));
        var only = Assert.Single(frames);
        Assert.Equal(player.ObjectId, (int)only[1]!);
        int x = (int)only[2]!, y = (int)only[3]!, z = (int)only[4]!;
        Assert.Equal(0, (int)only[5]!);
        Assert.Contains(player.ObjectId, Assert.IsAssignableFrom<IReadOnlyList<int>>(only[0]));

        // Mobius teleToLocation(TOWN): map-region of the death cell (elf_town for NetTestKit spawn), not TI hardcode.
        var regions = MapRegionCatalog.LoadDefault();
        var region = regions.At(deathX, deathY);
        Assert.NotNull(region);
        Assert.Equal("elf_town", region!.Name);
        Assert.Contains(region.RespawnPoints, pt =>
            !pt.IsChaotic && pt.Kind == "normal" && pt.X == x && pt.Y == y && pt.Z == z);
        Assert.NotEqual(TalkingIslandRestart.X, x);
    }

    [Fact]
    public async Task Gatekeeper_bypass_takes_the_fee_and_fans_the_jump_to_the_owner()
    {
        var (world, session) = await GatekeeperWorldAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        world.NetBridge = bridge;

        int marker = session.ObjectId;
        Assert.Null(await world.TryBypassAsync(session, GateObjectId, "teleport NORMAL 0", CancellationToken.None));

        var frames = rec.Of(nameof(IWorldEventPublisher.FanoutTeleportToLocation));
        var only = Assert.Single(frames);
        Assert.Equal(marker, (int)only[1]!);
        Assert.Equal(100, (int)only[2]!);
        Assert.Equal(0, (int)only[3]!);
        Assert.Contains(marker, Assert.IsAssignableFrom<IReadOnlyList<int>>(only[0]));
    }

    [Fact]
    public async Task Gatekeeper_jump_tells_the_client_who_left_and_who_appeared()
    {
        var (world, session) = await GatekeeperWorldAsync(destinationX: 7_000);
        // Enter does not always link the gate. A step while the bridge is still off fills the knownlist;
        // the jump must then tell the client that the gate left view.
        Assert.Null(await world.TryMoveAsync(session, 50, 0, 0, CancellationToken.None));
        var (bridge, rec) = NetBridgeRecorder.Create();
        world.NetBridge = bridge;

        Assert.Null(await world.TryBypassAsync(session, GateObjectId, "teleport NORMAL 0", CancellationToken.None));

        var frames = rec.Of(nameof(IWorldEventPublisher.FanoutKnownlistDiff));
        var diff = Assert.Single(frames);
        Assert.Equal(session.ObjectId, (int)diff[0]!);
        Assert.Contains(GateObjectId, Assert.IsAssignableFrom<IReadOnlyList<int>>(diff[2]));
    }

    const int GateObjectId = 61000;

    static Task<(ZoneWorld World, ZoneSession Session)> GatekeeperWorldAsync() =>
        GatekeeperWorldAsync(destinationX: 100);

    static async Task<(ZoneWorld World, ZoneSession Session)> GatekeeperWorldAsync(int destinationX)
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
            aoi: new GridAoi(),
            spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { [SpawnCatalog.DefaultMapId] = map }),
            accounts: store,
            teleports: FreeTeleports(destinationX),
            teleportOffset: 0);

        var session = new ZoneSession();
        session.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        return (world, session);
    }

    static TeleportCatalog FreeTeleports(int destinationX) => TeleportCatalog.Parse(
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
                    { "id": 0, "name": "Near", "x": DEST_X, "y": 0, "z": 0, "feeCount": 0 }
                  ]
                }
              ]
            }
          ]
        }
        """.Replace("DEST_X", destinationX.ToString()),
        "test");
}
