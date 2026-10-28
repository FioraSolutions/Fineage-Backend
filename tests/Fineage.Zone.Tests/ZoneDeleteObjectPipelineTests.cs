using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Zone.Interest;
using Fineage.Zone.World;
using MessagePack;
using Xunit;
using ProtocolMsgType = Fineage.Protocol.MsgType;
using V2MsgType = Fineage.Protocol.V2.MsgType;

namespace Fineage.Zone.Tests;

/// <summary>
/// V2 pipeline fan-out when players disconnect or leave AOI (Fineage #22 server slice).
/// </summary>
public sealed class ZoneDeleteObjectPipelineTests
{
    [Fact]
    public async Task Disconnect_broadcasts_DeleteObject_on_pipeline()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var fighter = Play(login, "leavea", "HumanOne", HumanClasses.Fighter);
        var mystic = Play(login, "leaveb", "HumanTwo", HumanClasses.Mystic);
        var world = new ZoneWorld();
        var server = new ZoneServer(tokens, world);
        await using var host = await PipelineZoneHost.StartAsync(server);

        await using var leaver = await ZonePipelinePeer.ConnectAsync(host.Port);
        await using var watcher = await ZonePipelinePeer.ConnectAsync(host.Port);
        await leaver.AuthEnterAsync(fighter, expectOthers: 0);
        await watcher.AuthEnterAsync(fighter, mystic);

        var deleteTask = watcher.ExpectDeleteObjectAsync();
        await leaver.DisposeAsync();
        var deleted = await deleteTask;
        Assert.Equal(fighter.CharacterId, deleted.ObjectId);
        watcher.ExpectQuiet();
    }

    static HandoffTicket Play(LoginService login, string account, string name, int classId)
    {
        var result = login.Play(account, "pass1234", name, classId);
        Assert.True(result.Ok && result.Value != null, $"{result.Code} {result.Message}");
        return result.Value!;
    }

    sealed class ZonePipelinePeer : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        bool _closed;

        ZonePipelinePeer(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public static async Task<ZonePipelinePeer> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new ZonePipelinePeer(client, client.GetStream());
        }

        public async Task AuthAsync(HandoffTicket ticket)
        {
            await SendAsync(ProtocolMsgType.Hello, new HelloMsg { ProtocolVersion = 1, ClientBuild = "deleteobject-e2e" });
            await ExpectAsync<HelloAckMsg>(ProtocolMsgType.HelloAck);
            await SendAsync(ProtocolMsgType.Auth, ticket.ToZoneAuth());
            await ExpectAsync<AuthOkMsg>(ProtocolMsgType.AuthOk);
        }

        public async Task EnterAsync(int expectOthers)
        {
            await SendAsync(ProtocolMsgType.EnterWorld, new EnterWorldMsg());
            await ExpectAsync<UserInfoMsg>(ProtocolMsgType.UserInfo);
            await ExpectAsync<ItemListMsg>(ProtocolMsgType.ItemList);
            await ExpectAsync<PlayerSpawnedMsg>(ProtocolMsgType.PlayerSpawned);
            var pending = TalkingIsland.Gremlins.Length + expectOthers;
            int npcs = 0;
            int others = 0;
            for (int i = 0; i < pending; i++)
            {
                var frame = await ReadFrameAsync("enter presence");
                if (frame.Type == (ushort)ProtocolMsgType.NpcInfo)
                {
                    MessagePackSerializer.Deserialize<NpcInfoMsg>(frame.Body);
                    npcs++;
                }
                else if (frame.Type == (ushort)ProtocolMsgType.CharInfo)
                {
                    MessagePackSerializer.Deserialize<CharInfoMsg>(frame.Body);
                    others++;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"enter presence expected NpcInfo or CharInfo, got {(ProtocolMsgType)frame.Type} ({frame.Type})");
                }
            }

            if (npcs != TalkingIsland.Gremlins.Length || others != expectOthers)
                throw new InvalidOperationException($"enter presence npcs={npcs} others={others}");

            var welcome = await ExpectAsync<SystemMessageMsg>(ProtocolMsgType.SystemMessage);
            if (welcome.Id != ChatProtocol.Welcome)
                throw new InvalidOperationException($"welcome system message id={welcome.Id}");
        }

        public async Task AuthEnterAsync(HandoffTicket ticket, int expectOthers)
        {
            await AuthAsync(ticket);
            await EnterAsync(expectOthers);
        }

        public async Task AuthEnterAsync(HandoffTicket alreadyIn, HandoffTicket self)
        {
            await AuthAsync(self);
            await EnterAsync(expectOthers: 1);
        }

        public async Task SendAsync<T>(ProtocolMsgType type, T msg)
        {
            byte[] body = MessagePackSerializer.Serialize(msg);
            await FrameIO.SendAsync(_stream, (ushort)type, body);
        }

        public async Task<T> ExpectAsync<T>(ProtocolMsgType expected)
        {
            var frame = await ReadFrameAsync($"waiting for {expected}");
            if (frame.Type != (ushort)expected)
                throw new InvalidOperationException(
                    $"expected {expected} ({(ushort)expected}), got {(ProtocolMsgType)frame.Type} ({frame.Type})");
            return MessagePackSerializer.Deserialize<T>(frame.Body);
        }

        public async Task<Fineage.Protocol.DeleteObjectMsg> ExpectDeleteObjectAsync()
        {
            var frame = await ReadFrameAsync("waiting for DeleteObject");
            Assert.Equal((ushort)V2MsgType.DeleteObject, frame.Type);
            Assert.Equal((ushort)ProtocolMsgType.DeleteObject, frame.Type);
            return MessagePackSerializer.Deserialize<Fineage.Protocol.DeleteObjectMsg>(frame.Body);
        }

        public void ExpectQuiet()
        {
            if (_stream.DataAvailable)
                throw new InvalidOperationException("unexpected trailing zone bytes");
        }

        async Task<(ushort Type, byte[] Body)> ReadFrameAsync(string context)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = await FrameIO.ReadAsync(_stream, cts.Token);
            if (frame == null)
                throw new InvalidOperationException($"{context}: EOF");
            return frame.Value;
        }

        public async ValueTask DisposeAsync()
        {
            if (_closed)
                return;
            _closed = true;
            _client.Dispose();
            await Task.CompletedTask;
        }
    }

    sealed class PipelineZoneHost : IAsyncDisposable
    {
        readonly CancellationTokenSource _cts;
        readonly Task _run;

        PipelineZoneHost(int port, CancellationTokenSource cts, Task run)
        {
            Port = port;
            _cts = cts;
            _run = run;
        }

        public int Port { get; }

        public static async Task<PipelineZoneHost> StartAsync(ZoneServer server)
        {
            var cts = new CancellationTokenSource();
            var tcp = new TcpServer();
            var run = Task.Run(() => tcp.RunAsync("127.0.0.1", 0, 4, 8, "zone-deleteobject", server.HandleAsync, cts.Token));

            for (int i = 0; i < 100; i++)
            {
                if (tcp.BoundPort != 0)
                    return new PipelineZoneHost(tcp.BoundPort, cts, run);

                await Task.Delay(20);
            }

            await cts.CancelAsync();
            throw new InvalidOperationException("TcpServer did not bind.");
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try
            {
                await _run.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }
}
