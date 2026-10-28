using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Zone TCP opcodes for UseItem, UnequipItem, and the admin GiveItem gate.</summary>
public sealed class ZoneEquipWireTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("1", true)]
    [InlineData(" true ", true)]
    [InlineData("YES", true)]
    [InlineData("True", true)]
    public void Admin_flag_is_off_unless_explicitly_enabled(string? text, bool enabled)
    {
        Assert.Equal(enabled, ZoneAdmin.IsEnabled(text));
    }

    [Fact]
    public async Task UseItem_and_UnequipItem_round_trip_on_the_zone_port()
    {
        await using var host = await ZoneGearHost.StartAsync();
        var ticket = host.Login.Play("wire", "pass1234", "Alden", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null, ticket.Message);

        await using var actor = await ZoneGearPeer.ConnectAsync(host.Port);
        await actor.AuthAsync(ticket.Value);
        await actor.SendAsync(MsgType.UseItem, new UseItemMsg { ObjectId = 1 });
        var early = await actor.ExpectErrorAsync();
        Assert.Equal("world", early.Code);

        var entered = await actor.EnterAsync();
        int sword = entered.Items.Items.Single(item => item.ItemId == 2369).ObjectId;
        int dagger = entered.Items.Items.Single(item => item.ItemId == 10).ObjectId;
        int guide = entered.Items.Items.Single(item => item.ItemId == 5588).ObjectId;
        Assert.Equal(7, entered.Items.Items.Single(item => item.ItemId == 2369).EquippedSlot);

        await actor.SendRawAsync((ushort)MsgType.UseItem, []);
        Assert.Equal("bad_request", (await actor.ExpectErrorAsync()).Code);

        await actor.SendAsync(MsgType.UseItem, new UseItemMsg { ObjectId = 42 });
        Assert.Equal(InventoryProtocol.NotOwned, (await actor.ExpectErrorAsync()).Code);

        await actor.SendAsync(MsgType.UseItem, new UseItemMsg { ObjectId = guide });
        Assert.Equal(InventoryProtocol.NotEquippable, (await actor.ExpectErrorAsync()).Code);

        await actor.SendAsync(MsgType.UnequipItem, new UnequipItemMsg { PaperdollSlot = 17 });
        Assert.Equal(InventoryProtocol.BadSlot, (await actor.ExpectErrorAsync()).Code);

        await actor.SendAsync(MsgType.UnequipItem, new UnequipItemMsg { PaperdollSlot = 8 });
        Assert.Equal(InventoryProtocol.Empty, (await actor.ExpectErrorAsync()).Code);

        await actor.SendAsync(MsgType.UseItem, new UseItemMsg { ObjectId = dagger });
        var update = await actor.ExpectAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate);
        var user = await actor.ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
        Assert.Contains(update.Changes, row => row.Change == InventoryChange.Modify && row.ObjectId == dagger && row.EquippedSlot == 7 && row.ItemId == 10);
        Assert.Contains(update.Changes, row => row.Change == InventoryChange.Modify && row.ObjectId == sword && row.EquippedSlot == -1 && row.ItemId == 2369);
        Assert.Equal(10, user.Paperdoll[7].ItemId);
        Assert.Equal(dagger, user.Paperdoll[7].ObjectId);
        Assert.Equal(1146, user.Paperdoll[10].ItemId);

        var saved = host.Store.FindById(ticket.Value.Account, ticket.Value.CharacterId);
        Assert.Contains(saved!.Items!, item => item.ItemId == 10 && item.EquippedSlot == 7);
        Assert.Contains(saved.Items!, item => item.ItemId == 2369 && item.EquippedSlot == -1);

        await actor.SendAsync(MsgType.UseItem, new UseItemMsg { ObjectId = dagger });
        update = await actor.ExpectAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate);
        user = await actor.ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
        Assert.Contains(update.Changes, row => row.ObjectId == dagger && row.EquippedSlot == -1);
        Assert.Equal(0, user.Paperdoll[7].ItemId);

        Assert.True(host.World.TrySetPlayerDead(ticket.Value.CharacterId, true));
        await actor.SendAsync(MsgType.UnequipItem, new UnequipItemMsg { PaperdollSlot = 10 });
        Assert.Equal(InventoryProtocol.Dead, (await actor.ExpectErrorAsync()).Code);
        Assert.Contains(host.Store.FindById(ticket.Value.Account, ticket.Value.CharacterId)!.Items!, item => item.ItemId == 1146 && item.EquippedSlot == 10);
    }

    [Fact]
    public async Task Equip_broadcasts_CharInfo_key_6_to_the_other_player()
    {
        await using var host = await ZoneGearHost.StartAsync();
        var fighter = host.Login.Play("wire", "pass1234", "Alden", HumanClasses.Fighter);
        var mystic = host.Login.Play("wireb", "pass1234", "Bram", HumanClasses.Mystic);
        Assert.True(fighter.Ok && fighter.Value != null);
        Assert.True(mystic.Ok && mystic.Value != null);

        await using var left = await ZoneGearPeer.ConnectAsync(host.Port);
        await using var right = await ZoneGearPeer.ConnectAsync(host.Port);
        await left.AuthAsync(fighter.Value);
        await right.AuthAsync(mystic.Value);
        var leftEnter = await left.EnterAsync();
        var rightEnter = await right.EnterAsync(expectOthers: 1);
        var arrived = await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        Assert.Equal(mystic.Value.CharacterId, arrived.ObjectId);

        int dagger = leftEnter.Items.Items.Single(item => item.ItemId == 10).ObjectId;
        await left.SendAsync(MsgType.UseItem, new UseItemMsg { ObjectId = dagger });
        await left.ExpectAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate);
        var user = await left.ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
        var seen = await right.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);

        Assert.Equal(fighter.Value.CharacterId, seen.ObjectId);
        Assert.Equal(17, seen.Paperdoll.Length);
        Assert.Equal(10, seen.Paperdoll[7]);
        Assert.Equal(1146, seen.Paperdoll[10]);
        Assert.Equal(1147, seen.Paperdoll[11]);
        Assert.Equal(10, user.Paperdoll[7].ItemId);
        Assert.Equal(6, rightEnter.User.Paperdoll[7].ItemId);
        Assert.Equal(6, arrived.Paperdoll[7]);
    }

    [Fact]
    public async Task GiveItem_is_forbidden_until_admin_commands_are_enabled()
    {
        await using var locked = await ZoneGearHost.StartAsync(new ZoneServerOptions { AdminCommands = false });
        var ticket = locked.Login.Play("wire", "pass1234", "Alden", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);
        await using var actor = await ZoneGearPeer.ConnectAsync(locked.Port);
        await actor.AuthAsync(ticket.Value);
        await actor.EnterAsync();
        await actor.SendAsync(MsgType.GiveItem, new GiveItemMsg { ItemId = 57, Count = 25 });
        var denied = await actor.ExpectErrorAsync();
        Assert.Equal(InventoryProtocol.Forbidden, denied.Code);
        Assert.DoesNotContain(locked.Store.FindById(ticket.Value.Account, ticket.Value.CharacterId)!.Items!, item => item.ItemId == 57);

        await using var open = await ZoneGearHost.StartAsync(new ZoneServerOptions { AdminCommands = true });
        var adminTicket = open.Login.Play("admin", "pass1234", "Cira", HumanClasses.Fighter);
        Assert.True(adminTicket.Ok && adminTicket.Value != null);
        await using var admin = await ZoneGearPeer.ConnectAsync(open.Port);
        await admin.AuthAsync(adminTicket.Value);
        await admin.SendAsync(MsgType.GiveItem, new GiveItemMsg { ItemId = 57, Count = 4 });
        Assert.Equal("world", (await admin.ExpectErrorAsync()).Code);

        await admin.EnterAsync();
        await admin.SendAsync(MsgType.GiveItem, new GiveItemMsg { ItemId = 57, Count = 4 });
        var update = await admin.ExpectAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate);
        var row = Assert.Single(update.Changes);
        Assert.Equal(InventoryChange.Add, row.Change);
        Assert.Equal(57, row.ItemId);
        Assert.Equal(4, row.Count);
        Assert.Equal(-1, row.EquippedSlot);
        Assert.Contains(open.Store.FindById(adminTicket.Value.Account, adminTicket.Value.CharacterId)!.Items!, item => item.ItemId == 57 && item.Count == 4);

        await admin.SendAsync(MsgType.GiveItem, new GiveItemMsg { ItemId = 999999, Count = 1 });
        Assert.Equal("bad_request", (await admin.ExpectErrorAsync()).Code);

        await admin.SendRawAsync((ushort)MsgType.GiveItem, []);
        Assert.Equal("bad_request", (await admin.ExpectErrorAsync()).Code);
    }

    sealed class ZoneGearHost : IAsyncDisposable
    {
        readonly TcpListener _listener;
        readonly CancellationTokenSource _cts;
        readonly Task _accept;

        ZoneGearHost(TcpListener listener, CancellationTokenSource cts, Task accept, LoginService login, JsonAccountStore store, ZoneWorld world)
        {
            _listener = listener;
            _cts = cts;
            _accept = accept;
            Login = login;
            Store = store;
            World = world;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public int Port { get; }
        public LoginService Login { get; }
        public JsonAccountStore Store { get; }
        public ZoneWorld World { get; }

        public static async Task<ZoneGearHost> StartAsync(ZoneServerOptions? options = null)
        {
            var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
            var store = JsonAccountStore.InMemory();
            var world = new ZoneWorld(accounts: store);
            var server = new ZoneServer(tokens, world, clock: null, options);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var cts = new CancellationTokenSource();
            var accept = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    TcpClient accepted;
                    try
                    {
                        accepted = await listener.AcceptTcpClientAsync(cts.Token);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                    {
                        break;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await using var stream = accepted.GetStream();
                            await server.HandleAsync(stream, cts.Token);
                        }
                        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                        {
                        }
                        finally
                        {
                            accepted.Dispose();
                        }
                    });
                }
            });

            await Task.Yield();
            return new ZoneGearHost(listener, cts, accept, new LoginService(store, tokens), store, world);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            try
            {
                await _accept.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
            }
        }
    }

    sealed class ZoneGearPeer : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;

        ZoneGearPeer(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public static async Task<ZoneGearPeer> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new ZoneGearPeer(client, client.GetStream());
        }

        public async Task AuthAsync(HandoffTicket ticket)
        {
            await SendAsync(MsgType.Hello, new HelloMsg());
            await ExpectAsync<HelloAckMsg>(MsgType.HelloAck);
            await SendAsync(MsgType.Auth, ticket.ToZoneAuth());
            await ExpectAsync<AuthOkMsg>(MsgType.AuthOk);
        }

        public async Task<Arrival> EnterAsync(int expectOthers = 0)
        {
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg());
            var user = await ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
            var items = await ExpectAsync<ItemListMsg>(MsgType.ItemList);
            await ExpectAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned);
            for (int i = 0; i < TalkingIsland.Gremlins.Length; i++)
                await ExpectAsync<NpcInfoMsg>(MsgType.NpcInfo);
            for (int i = 0; i < expectOthers; i++)
                await ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
            return new Arrival(user, items);
        }

        public async Task SendAsync<T>(MsgType type, T message)
        {
            byte[] body = MessagePackSerializer.Serialize(message);
            await SendRawAsync((ushort)type, body);
        }

        public async Task SendRawAsync(ushort type, byte[] body)
        {
            await FrameIO.SendAsync(_stream, type, body);
        }

        public async Task<T> ExpectAsync<T>(MsgType expected)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = await FrameIO.ReadAsync(_stream, cts.Token);
            Assert.NotNull(frame);
            if (frame!.Value.Type != (ushort)expected)
            {
                string detail = "";
                if (frame.Value.Type == (ushort)MsgType.Error)
                {
                    var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
                    detail = $" {err.Code} {err.Message}";
                }

                Assert.Fail($"expected {expected}, got {(MsgType)frame.Value.Type}{detail}");
            }

            return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
        }

        public async Task<ErrorMsg> ExpectErrorAsync() => await ExpectAsync<ErrorMsg>(MsgType.Error);

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await Task.CompletedTask;
        }

        public readonly record struct Arrival(UserInfoMsg User, ItemListMsg Items);
    }
}
