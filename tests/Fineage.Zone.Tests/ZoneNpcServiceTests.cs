using System.Net;
using System.Net.Sockets;
using Fineage.Game.Services;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneNpcServiceTests
{
    const int GateObjectId = 61000;
    const int KeeperObjectId = 61001;

    [Fact]
    public void Loaded_zone_keeps_talking_island_spots_and_rejects_gludin()
    {
        var village = SpawnCatalog.LoadDefault().GetMap(SpawnCatalog.TalkingIslandVillageMapId);
        var anchors = village.Npcs.Select(npc => (npc.X, npc.Y)).ToArray();
        Assert.True(NpcServiceProtocol.InLoadedZone(-99586, 237637, anchors));
        Assert.True(NpcServiceProtocol.InLoadedZone(-112367, 234703, anchors));
        Assert.True(NpcServiceProtocol.InLoadedZone(-111728, 244330, anchors));
        Assert.False(NpcServiceProtocol.InLoadedZone(-80749, 149834, anchors));
    }

    [Fact]
    public async Task Free_teleport_moves_inside_the_loaded_zone_and_refuses_the_rest()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("gate", "secret1", "Rook", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);
        Assert.True(store.TrySavePresence(ticket.Value.Account, ticket.Value.CharacterId, 0, 0, 0, null));

        var world = World(store, teleports: SampleTeleports(), offset: 0);
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        long mark = stream.Length;
        Assert.Null(await world.TryBypassAsync(session, GateObjectId, "showTeleports", CancellationToken.None));
        stream.Position = mark;
        var list = await ReadAsync<TeleportListMsg>(stream, MsgType.TeleportList);
        Assert.Equal("NORMAL", list.ListName);
        Assert.Equal(0, list.Locations[0].FeeCount);
        Assert.Equal(100, list.Locations[0].X);
        var page = await ReadAsync<NpcHtmlMsg>(stream, MsgType.NpcHtml);
        Assert.Contains("Near", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("1000 Adena", page.Html, StringComparison.Ordinal);

        mark = stream.Length;
        Assert.Null(await world.TryBypassAsync(session, GateObjectId, "teleport NORMAL 0", CancellationToken.None));
        stream.Position = mark;
        var moved = await ReadAsync<MoveToMsg>(stream, MsgType.MoveToAck);
        Assert.Equal(100, moved.DestX);
        Assert.Equal(0, moved.DestY);
        var done = await ReadAsync<SystemMessageMsg>(stream, MsgType.SystemMessage);
        Assert.Equal(NpcServiceProtocol.Teleported, done.Id);
        Assert.Equal(100, session.X);

        mark = stream.Length;
        Assert.Null(await world.TryBypassAsync(session, GateObjectId, "goto 1", CancellationToken.None));
        stream.Position = mark;
        var blocked = await ReadAsync<SystemMessageMsg>(stream, MsgType.SystemMessage);
        Assert.Equal(NpcServiceProtocol.DestinationUnloaded, blocked.Id);
        Assert.Equal(100, session.X);
        _ = await ReadAsync<NpcHtmlMsg>(stream, MsgType.NpcHtml);

        mark = stream.Length;
        Assert.Null(await world.TryBypassAsync(session, GateObjectId, "showTeleports NOBLES_ADENA", CancellationToken.None));
        stream.Position = mark;
        Assert.Equal(NpcServiceProtocol.NoblesDeferred, (await ReadAsync<SystemMessageMsg>(stream, MsgType.SystemMessage)).Id);
    }

    [Fact]
    public async Task Paid_teleport_takes_adena_and_the_monday_discount()
    {
        var when = new DateTimeOffset(2026, 10, 26, 21, 0, 0, TimeSpan.Zero);
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("fare", "secret1", "Fare", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);
        Assert.True(store.TrySaveProgress(ticket.Value.Account, ticket.Value.CharacterId, 41, 0, 0, 0, 0, 0, 0));
        Assert.True(store.TryReplaceItems(ticket.Value.Account, ticket.Value.CharacterId, [new StoredItem(57, 5000, -1)]));
        Assert.True(store.TrySavePresence(ticket.Value.Account, ticket.Value.CharacterId, 0, 0, 0, null));

        var world = World(store, teleports: SampleTeleports(), offset: 0, clock: () => when);
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value.CharacterId, ticket.Value.Account, "Fare", HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        long mark = stream.Length;
        Assert.Null(await world.TryTeleportSelectAsync(session, new TeleportSelectRequestMsg
        {
            NpcObjectId = GateObjectId,
            ListName = "NORMAL",
            LocationId = 0
        }, CancellationToken.None));
        stream.Position = mark;
        var update = await ReadAsync<InventoryUpdateMsg>(stream, MsgType.InventoryUpdate);
        var adena = Assert.Single(update.Changes);
        Assert.Equal(57, adena.ItemId);
        Assert.Equal(4500, adena.Count);
        var moved = await ReadAsync<MoveToMsg>(stream, MsgType.MoveToAck);
        Assert.Equal(100, moved.DestX);
    }

    [Fact]
    public async Task Newbie_helper_buffs_fighters_and_mages_and_skips_a_second_character()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var fighter = login.Play("buffs", "secret1", "Fighter", HumanClasses.Fighter);
        var mage = login.Play("buffs", "secret1", "Mage", HumanClasses.Mystic);
        Assert.True(fighter.Ok && fighter.Value != null && mage.Ok && mage.Value != null);
        Assert.True(store.FindById(fighter.Value.Account, fighter.Value.CharacterId)!.Newbie);
        Assert.False(store.FindById(mage.Value.Account, mage.Value.CharacterId)!.Newbie);
        Assert.True(store.TrySaveProgress(fighter.Value.Account, fighter.Value.CharacterId, 16, 0, 0, 0, 0, 0, 0));
        Assert.True(store.TrySaveProgress(mage.Value.Account, mage.Value.CharacterId, 16, 0, 0, 0, 0, 0, 0));
        Assert.True(store.TrySavePresence(fighter.Value.Account, fighter.Value.CharacterId, 0, 0, 0, null));
        Assert.True(store.TrySavePresence(mage.Value.Account, mage.Value.CharacterId, 0, 0, 0, null));

        var world = World(store, teleports: SampleTeleports(), offset: 0);
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(fighter.Value.CharacterId, fighter.Value.Account, "Fighter", HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        long mark = stream.Length;
        Assert.Null(await world.TryBypassAsync(session, GateObjectId, "SupportMagic", CancellationToken.None));
        stream.Position = mark;
        var casts = new List<int>();
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            Assert.NotNull(frame);
            if (frame!.Value.Type == (ushort)MsgType.MagicSkillUse)
                casts.Add(MessagePackSerializer.Deserialize<MagicSkillUseMsg>(frame.Value.Body).SkillId);
            else if (frame.Value.Type == (ushort)MsgType.AbnormalStatusUpdate)
            {
                var abnormal = MessagePackSerializer.Deserialize<AbnormalStatusUpdateMsg>(frame.Value.Body);
                Assert.Contains(abnormal.Effects, effect => effect.Id == 4322 && effect.Value == 3 && effect.DurationSeconds == 3600);
                Assert.Contains(abnormal.Effects, effect => effect.Id == 4327);
                Assert.DoesNotContain(abnormal.Effects, effect => effect.Id == 4331);
                break;
            }
        }

        Assert.Contains(4322, casts);
        Assert.Contains(4327, casts);
        var result = await ReadAsync<NewbieBuffResultMsg>(stream, MsgType.NewbieBuffResult);
        Assert.Equal("", result.Refusal);
        Assert.Equal(7, result.SkillIds.Length);

        await world.LeaveAsync(session, CancellationToken.None);
        stream.SetLength(0);
        session.Bind(mage.Value.CharacterId, mage.Value.Account, "Mage", HumanClasses.Mystic);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        mark = stream.Length;
        Assert.Null(await world.TrySupportMagicAsync(session, GateObjectId, CancellationToken.None));
        stream.Position = mark;
        var refused = await ReadAsync<SystemMessageMsg>(stream, MsgType.SystemMessage);
        Assert.Equal(NpcServiceProtocol.BuffNotNewbie, refused.Id);
        Assert.Equal("newbie", (await ReadAsync<NewbieBuffResultMsg>(stream, MsgType.NewbieBuffResult)).Refusal);

        mark = stream.Position;
        Assert.Null(await world.TryGiveBlessingAsync(session, GateObjectId, CancellationToken.None));
        stream.Position = mark;
        _ = await ReadAsync<MagicSkillUseMsg>(stream, MsgType.MagicSkillUse);
        _ = await ReadAsync<AbnormalStatusUpdateMsg>(stream, MsgType.AbnormalStatusUpdate);
        var blessing = await ReadAsync<BlessingResultMsg>(stream, MsgType.BlessingResult);
        Assert.True(blessing.Accepted);
        Assert.Equal(5182, blessing.SkillId);
    }

    [Fact]
    public async Task Private_warehouse_charges_the_fee_persists_and_checks_weight()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("keep", "secret1", "Keep", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);
        Assert.True(store.TryReplaceItems(ticket.Value.Account, ticket.Value.CharacterId,
            [new StoredItem(57, 200, -1), new StoredItem(736, 10, -1)]));
        Assert.True(store.TrySavePresence(ticket.Value.Account, ticket.Value.CharacterId, 0, 0, 0, null));

        var world = World(store, teleports: SampleTeleports(), offset: 0, weights: new ItemWeights(new Dictionary<int, int> { [736] = 1_000_000 }));
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value.CharacterId, ticket.Value.Account, "Keep", HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        long mark = stream.Length;
        Assert.Null(await world.TryBypassAsync(session, KeeperObjectId, "DepositP", CancellationToken.None));
        stream.Position = mark;
        var window = await ReadAsync<WarehouseDepositListMsg>(stream, MsgType.WarehouseDepositList);
        Assert.Equal(30, window.FeePerLine);
        Assert.Equal(100, window.SlotsMax);
        Assert.Equal(200, window.Adena);
        var scrolls = Assert.Single(window.Items, item => item.ItemId == 736);
        var adena = Assert.Single(window.Items, item => item.ItemId == 57);
        _ = await ReadAsync<NpcHtmlMsg>(stream, MsgType.NpcHtml);

        mark = stream.Length;
        Assert.Null(await world.TryWarehouseDepositAsync(session, new WarehouseDepositRequestMsg
        {
            NpcObjectId = KeeperObjectId,
            Lines = [new WarehouseCountMsg { ObjectId = scrolls.ObjectId, Count = 10 }]
        }, CancellationToken.None));
        stream.Position = mark;
        var paid = await ReadAsync<InventoryUpdateMsg>(stream, MsgType.InventoryUpdate);
        Assert.Contains(paid.Changes, row => row.ItemId == 57 && row.Count == 170);
        Assert.Contains(paid.Changes, row => row.ItemId == 736 && row.Count == 0);
        Assert.Equal(NpcServiceProtocol.TransactionComplete, (await ReadAsync<SystemMessageMsg>(stream, MsgType.SystemMessage)).Id);
        stream.Position = stream.Length;

        mark = stream.Length;
        Assert.Null(await world.TryWarehouseDepositAsync(session, new WarehouseDepositRequestMsg
        {
            NpcObjectId = KeeperObjectId,
            Lines = [new WarehouseCountMsg { ObjectId = adena.ObjectId, Count = 50 }]
        }, CancellationToken.None));
        stream.Position = mark;
        var second = await ReadAsync<InventoryUpdateMsg>(stream, MsgType.InventoryUpdate);
        Assert.Contains(second.Changes, row => row.ItemId == 57 && row.Count == 90);
        _ = await ReadAsync<SystemMessageMsg>(stream, MsgType.SystemMessage);

        await world.LeaveAsync(session, CancellationToken.None);
        var saved = store.FindById(ticket.Value.Account, ticket.Value.CharacterId);
        Assert.Contains(saved!.Warehouse!, row => row.ItemId == 736 && row.Count == 10);
        Assert.Contains(saved.Warehouse!, row => row.ItemId == 57 && row.Count == 50);

        stream.SetLength(0);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        mark = stream.Length;
        Assert.Null(await world.TryBypassAsync(session, KeeperObjectId, "WithdrawP", CancellationToken.None));
        stream.Position = mark;
        var back = await ReadAsync<WarehouseWithdrawListMsg>(stream, MsgType.WarehouseWithdrawList);
        Assert.Equal(2, back.SlotsUsed);
        Assert.True(back.MaxWeight > 69000);
        var storedAdena = Assert.Single(back.Items, item => item.ItemId == 57);
        var storedScrolls = Assert.Single(back.Items, item => item.ItemId == 736);
        _ = await ReadAsync<NpcHtmlMsg>(stream, MsgType.NpcHtml);

        mark = stream.Length;
        Assert.Null(await world.TryWarehouseWithdrawAsync(session, new WarehouseWithdrawRequestMsg
        {
            NpcObjectId = KeeperObjectId,
            Lines = [new WarehouseCountMsg { ObjectId = storedScrolls.ObjectId, Count = 1 }]
        }, CancellationToken.None));
        stream.Position = mark;
        Assert.Equal(NpcServiceProtocol.WeightLimit, (await ReadAsync<SystemMessageMsg>(stream, MsgType.SystemMessage)).Id);
        stream.Position = stream.Length;

        mark = stream.Length;
        Assert.Null(await world.TryWarehouseWithdrawAsync(session, new WarehouseWithdrawRequestMsg
        {
            NpcObjectId = KeeperObjectId,
            Lines = [new WarehouseCountMsg { ObjectId = storedAdena.ObjectId, Count = 50 }]
        }, CancellationToken.None));
        stream.Position = mark;
        var returned = await ReadAsync<InventoryUpdateMsg>(stream, MsgType.InventoryUpdate);
        Assert.Contains(returned.Changes, row => row.ItemId == 57 && row.Count == 140);
    }

    [Fact]
    public async Task Zone_server_dispatches_the_service_opcodes()
    {
        var store = JsonAccountStore.InMemory();
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(store, tokens);
        var ticket = login.Play("wire", "secret1", "Wire", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);
        Assert.True(store.TrySavePresence(ticket.Value.Account, ticket.Value.CharacterId, 0, 0, 0, null));
        var world = World(store, teleports: SampleTeleports(), offset: 0);
        var server = new ZoneServer(tokens, world);
        await using var link = await ServiceLink.Open(server, world.NpcObjectIds.Count);
        await link.EnterAsync(ticket.Value!);
        await link.SendAsync(MsgType.TeleportSelectRequest, new TeleportSelectRequestMsg
        {
            NpcObjectId = GateObjectId,
            ListName = "NORMAL",
            LocationId = 1
        });
        var refused = await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(NpcServiceProtocol.DestinationUnloaded, refused.Id);
        await link.CloseAsync();
    }

    static TeleportCatalog SampleTeleports() => TeleportCatalog.Parse(
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
                    { "id": 0, "name": "Near", "x": 100, "y": 0, "z": 0, "feeCount": 1000 },
                    { "id": 1, "name": "Far", "x": 200000, "y": 200000, "z": 0, "feeCount": 18000 }
                  ]
                },
                {
                  "name": "NOBLES_ADENA",
                  "type": "NOBLES_ADENA",
                  "locations": [
                    { "id": 0, "name": "Arena", "x": 100, "y": 0, "z": 0, "feeCount": 1000 }
                  ]
                }
              ]
            }
          ]
        }
        """,
        "test");

    static ZoneWorld World(JsonAccountStore store, TeleportCatalog teleports, int offset, ItemWeights? weights = null, Func<DateTimeOffset>? clock = null)
    {
        var map = new SpawnMapDefinition(
            SpawnCatalog.DefaultMapId,
            true,
            [
                new SpawnNpcDefinition(GateObjectId, 30006, "Roxxy", 0, 0, 0, 0, false, 100, 80f, 40f, 60, 0, "Gatekeeper", "Teleporter"),
                new SpawnNpcDefinition(KeeperObjectId, 30005, "Wilford", 0, 0, 0, 0, false, 100, 80f, 40f, 60, 0, "Warehouse Keeper", "Warehouse")
            ]);
        return new ZoneWorld(
            spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { [SpawnCatalog.DefaultMapId] = map }),
            accounts: store,
            teleports: teleports,
            teleportOffset: offset,
            weights: weights,
            clock: clock);
    }

    static async Task<T> ReadAsync<T>(Stream stream, MsgType type)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)type, frame!.Value.Type);
        return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
    }

    sealed class ServiceLink : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        readonly CancellationTokenSource _cts;
        readonly Task _server;
        readonly int _npcs;

        ServiceLink(TcpClient client, NetworkStream stream, CancellationTokenSource cts, Task server, int npcs)
        {
            _client = client;
            _stream = stream;
            _cts = cts;
            _server = server;
            _npcs = npcs;
        }

        public static async Task<ServiceLink> Open(ZoneServer server, int npcs)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var cts = new CancellationTokenSource();
            var accept = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
                listener.Stop();
                await using var stream = accepted.GetStream();
                await server.HandleAsync(stream, cts.Token);
            });
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new ServiceLink(client, client.GetStream(), cts, accept, npcs);
        }

        public async Task EnterAsync(HandoffTicket ticket)
        {
            await SendAsync(MsgType.Hello, new HelloMsg());
            _ = await ReadAsync<HelloAckMsg>(MsgType.HelloAck);
            await SendAsync(MsgType.Auth, ticket.ToZoneAuth());
            _ = await ReadAsync<AuthOkMsg>(MsgType.AuthOk);
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg());
            _ = await ReadAsync<UserInfoMsg>(MsgType.UserInfo);
            _ = await ReadAsync<ItemListMsg>(MsgType.ItemList);
            _ = await ReadAsync<SkillListMsg>(MsgType.SkillList);
            _ = await ReadAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned);
            for (int i = 0; i < _npcs; i++)
                _ = await ReadAsync<NpcInfoMsg>(MsgType.NpcInfo);
            Assert.Equal(ChatProtocol.Welcome, (await ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        }

        public async Task SendAsync<T>(MsgType type, T message) =>
            await FrameIO.SendAsync(_stream, (ushort)type, MessagePackSerializer.Serialize(message));

        public async Task<T> ReadAsync<T>(MsgType type)
        {
            var frame = await FrameIO.ReadAsync(_stream);
            Assert.NotNull(frame);
            Assert.Equal((ushort)type, frame!.Value.Type);
            return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
        }

        public async Task CloseAsync()
        {
            _client.Close();
            await _server.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _cts.CancelAsync();
            try
            {
                await _server.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }
}
