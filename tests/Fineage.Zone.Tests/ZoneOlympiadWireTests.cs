using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Olympiad;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public class ZoneOlympiadWireTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 28, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Status_register_and_unregister_follow_the_noble_check()
    {
        var store = JsonAccountStore.InMemory();
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        Assert.True(new LoginService(store, tokens).Register("oly", "secret1").Ok);
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter("oly", "Oly", HumanClasses.Fighter, 0, 0, out var created));
        Assert.NotNull(created);
        var world = new ZoneWorld(accounts: store, clock: () => Now);
        await using var host = OlympiadHost.Start(tokens, world, () => Now);
        await using var peer = await OlympiadPeer.ConnectAsync(host.Port);
        await peer.AuthAsync(tokens.Issue(created, Now));
        await peer.EnterAsync();

        await peer.SendAsync(MsgType.OlympiadStatusRequest, new OlympiadStatusRequestMsg());
        var period = await peer.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(OlympiadProtocol.CompetitionStarted, period.Id);
        Assert.Equal("period", Assert.Single(period.Params.Take(1)));
        var comp = await peer.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal("comp", Assert.Single(comp.Params));
        var status = await peer.ExpectAsync<OlympiadStatusMsg>(MsgType.OlympiadStatus);
        Assert.False(status.Noble);
        Assert.True(status.InCompetition);
        Assert.True(status.RegistrationOpen);
        Assert.Equal(0, status.Points);
        Assert.Equal(OlympiadProtocol.QueueNone, status.Queue);

        await peer.SendAsync(MsgType.OlympiadRegisterRequest, new OlympiadRegisterRequestMsg { Classed = 1 });
        var nobles = await peer.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(OlympiadProtocol.NoblesOnly, nobles.Id);
        Assert.Equal(OlympiadProtocol.Noble, (await peer.ExpectAsync<ErrorMsg>(MsgType.Error)).Code);

        Assert.True(world.GrantNoblesse(created.Id));
        await peer.SendAsync(MsgType.OlympiadRegisterRequest, new OlympiadRegisterRequestMsg { Classed = 1 });
        Assert.Equal(OlympiadProtocol.RegisteredClassed, (await peer.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        var queued = await peer.ExpectAsync<OlympiadStatusMsg>(MsgType.OlympiadStatus);
        Assert.True(queued.Noble);
        Assert.Equal(18, queued.Points);
        Assert.Equal(OlympiadProtocol.QueueClassed, queued.Queue);

        await peer.SendAsync(MsgType.OlympiadUnregisterRequest, new OlympiadUnregisterRequestMsg());
        Assert.Equal(OlympiadProtocol.Unregistered, (await peer.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(OlympiadProtocol.QueueNone, (await peer.ExpectAsync<OlympiadStatusMsg>(MsgType.OlympiadStatus)).Queue);

        await peer.SendAsync(MsgType.AdminReportOlympiadResult, new AdminReportOlympiadResultMsg { MatchId = 1, WinnerObjectId = 0 });
        Assert.Equal(InventoryProtocol.Forbidden, (await peer.ExpectAsync<ErrorMsg>(MsgType.Error)).Code);
    }

    [Fact]
    public async Task Admin_report_is_rejected_when_the_match_does_not_exist()
    {
        var store = JsonAccountStore.InMemory();
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        Assert.True(new LoginService(store, tokens).Register("oly", "secret1").Ok);
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter("oly", "Oly", HumanClasses.Fighter, 0, 0, out var created));
        Assert.NotNull(created);
        var world = new ZoneWorld(accounts: store, clock: () => Now);
        await using var host = OlympiadHost.Start(tokens, world, () => Now, new ZoneServerOptions { AdminCommands = true });
        await using var peer = await OlympiadPeer.ConnectAsync(host.Port);
        await peer.AuthAsync(tokens.Issue(created, Now));
        await peer.EnterAsync();
        await peer.SendAsync(MsgType.AdminReportOlympiadResult, new AdminReportOlympiadResultMsg { MatchId = 4, WinnerObjectId = 0 });
        Assert.Equal(OlympiadProtocol.MatchMissing, (await peer.ExpectAsync<ErrorMsg>(MsgType.Error)).Code);
    }

    sealed class OlympiadHost : IAsyncDisposable
    {
        readonly TcpListener _listener;
        readonly CancellationTokenSource _cts;
        readonly Task _loop;

        OlympiadHost(TcpListener listener, CancellationTokenSource cts, Task loop)
        {
            _listener = listener;
            _cts = cts;
            _loop = loop;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public static OlympiadHost Start(HandoffTokens tokens, ZoneWorld world, Func<DateTimeOffset> clock, ZoneServerOptions? options = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var cts = new CancellationTokenSource();
            var server = new ZoneServer(tokens, world, clock, options);
            var loop = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                    {
                        break;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await using var stream = client.GetStream();
                            await server.HandleAsync(stream, cts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                        {
                        }
                        finally
                        {
                            client.Dispose();
                        }
                    });
                }
            });
            return new OlympiadHost(listener, cts, loop);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _listener.Stop();
            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }

    sealed class OlympiadPeer : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;

        OlympiadPeer(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public static async Task<OlympiadPeer> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new OlympiadPeer(client, client.GetStream());
        }

        public async Task AuthAsync(HandoffTicket ticket)
        {
            await SendAsync(MsgType.Hello, new HelloMsg());
            await ExpectAsync<HelloAckMsg>(MsgType.HelloAck);
            await SendAsync(MsgType.Auth, ticket.ToZoneAuth());
            await ExpectAsync<AuthOkMsg>(MsgType.AuthOk);
        }

        public async Task EnterAsync()
        {
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg());
            await ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
            await ExpectAsync<ItemListMsg>(MsgType.ItemList);
            await ExpectAsync<SkillListMsg>(MsgType.SkillList);
            await ExpectAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned);
            for (int i = 0; i < TalkingIsland.NpcCount; i++)
                await ExpectAsync<NpcInfoMsg>(MsgType.NpcInfo);
            await ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        }

        public async Task SendAsync<T>(MsgType type, T message)
        {
            byte[] body = MessagePackSerializer.Serialize(message);
            await FrameIO.SendAsync(_stream, (ushort)type, body);
        }

        public async Task<T> ExpectAsync<T>(MsgType expected)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = await FrameIO.ReadAsync(_stream, cts.Token);
            Assert.NotNull(frame);
            if (frame!.Value.Type != (ushort)expected)
            {
                string detail = frame.Value.Type == (ushort)MsgType.Error
                    ? " " + MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body).Code
                    : "";
                Assert.Fail($"expected {expected}, got {(MsgType)frame.Value.Type}{detail}");
            }

            return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
        }

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
