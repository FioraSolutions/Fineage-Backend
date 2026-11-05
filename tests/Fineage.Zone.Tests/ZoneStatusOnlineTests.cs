using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneStatusOnlineTests
{
    [Fact]
    public async Task Player_in_world_is_the_public_online_count()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var world = new ZoneWorld();
        var server = new ZoneServer(tokens, world);
        Assert.Equal(0, world.OnlinePlayerCount);

        ZoneStatusHost? zoneStatus = ZoneStatusHost.TryStart(0, () => world.OnlinePlayerCount, TextWriter.Null);
        Assert.NotNull(zoneStatus);
        var probe = new HttpZoneStatusSource(new Uri(zoneStatus.Url), TimeSpan.FromSeconds(2));
        await using var http = await LoginHttpHost.StartAsync(
            new LoginService(JsonAccountStore.InMemory(), tokens),
            IPAddress.Loopback,
            0,
            zoneStatus: probe);
        using var client = new HttpClient { BaseAddress = new Uri(http.BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            await AssertStatus(client, zone: true, players: 0);

            var login = new LoginService(JsonAccountStore.InMemory(), tokens);
            var ticket = login.Play("hero", "pass1234", "Fighter", HumanClasses.Fighter);
            Assert.True(ticket.Ok && ticket.Value != null);

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cts = new CancellationTokenSource();
            var acceptTask = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = accepted.GetStream();
                await server.HandleAsync(stream, cts.Token);
            });

            using var game = new TcpClient();
            await game.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = game.GetStream();

            await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
            await ReadAsync(stream, MsgType.HelloAck);
            await FrameIO.SendAsync(stream, (ushort)MsgType.Auth, MessagePackSerializer.Serialize(ticket.Value!.ToZoneAuth()));
            await ReadAsync(stream, MsgType.AuthOk);
            Assert.Equal(0, world.OnlinePlayerCount);
            await AssertStatus(client, zone: true, players: 0);

            await FrameIO.SendAsync(stream, (ushort)MsgType.EnterWorld, MessagePackSerializer.Serialize(new EnterWorldMsg()));
            await ReadAsync(stream, MsgType.UserInfo);
            await ReadAsync(stream, MsgType.ItemList);
            await ZoneEnterWire.ReadSkillListAsync(stream);
            await ReadAsync(stream, MsgType.PlayerSpawned);
            for (int i = 0; i < TalkingIsland.NpcCount; i++)
                await ReadAsync(stream, MsgType.NpcInfo);
            Assert.Equal(1, world.OnlinePlayerCount);

            using (var zoneClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
            {
                Assert.Equal("{\"playersOnline\":1}", await zoneClient.GetStringAsync(zoneStatus.Url));
            }

            await AssertStatus(client, zone: true, players: 1);

            listener.Stop();
            await cts.CancelAsync();
            try
            {
                await acceptTask;
            }
            catch (OperationCanceledException)
            {
            }

            Assert.Equal(0, world.OnlinePlayerCount);
            await AssertStatus(client, zone: true, players: 0);
        }
        finally
        {
            await zoneStatus.DisposeAsync();
        }

        await AssertStatus(client, zone: false, players: 0);
    }

    static async Task AssertStatus(HttpClient client, bool zone, int players)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement body = doc.RootElement;
        Assert.Equal(4, body.EnumerateObject().Count());
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True(body.GetProperty("login").GetBoolean());
        Assert.Equal(zone, body.GetProperty("zone").GetBoolean());
        Assert.Equal(players, body.GetProperty("playersOnline").GetInt32());
    }

    static async Task ReadAsync(NetworkStream stream, MsgType expected)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)expected, frame!.Value.Type);
    }
}
