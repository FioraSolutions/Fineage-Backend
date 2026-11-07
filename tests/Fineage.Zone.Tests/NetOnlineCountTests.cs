using System.Net;
using System.Net.Http;
using System.Text.Json;
using Fineage.Accounts;
using Fineage.Login;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The public online count is what Login shows on the server list, so it has to follow a Net
/// enter/leave cycle and survive a disconnect without a leave frame.
/// </summary>
public sealed class NetOnlineCountTests
{
    [Fact]
    public async Task OnlinePlayerCount_tracks_enter_and_disconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await NetZoneHost.StartAsync();
        await using var session = await host.ConnectAsync();
        Assert.Equal(0, host.World.OnlinePlayerCount);

        await session.HelloAsync(timeout.Token);
        await session.AuthAsync("hero", "Fighter", timeout.Token);

        // Auth binds the character but leaves it out of the world until EnterWorld.
        Assert.Equal(0, host.World.OnlinePlayerCount);

        await session.EnterWorldAsync(timeout.Token);
        Assert.Equal(1, host.World.OnlinePlayerCount);

        await session.DisconnectAsync();
        Assert.Equal(0, host.World.OnlinePlayerCount);
    }

    [Fact]
    public async Task Player_in_world_is_the_public_online_count()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await NetZoneHost.StartAsync();
        await using var session = await host.ConnectAsync();

        ZoneStatusHost? zoneStatus = ZoneStatusHost.TryStart(
            0,
            () => host.World.OnlinePlayerCount,
            TextWriter.Null);
        Assert.NotNull(zoneStatus);
        var probe = new HttpZoneStatusSource(new Uri(zoneStatus.Url), TimeSpan.FromSeconds(2));
        await using var http = await LoginHttpHost.StartAsync(
            new LoginService(MemoryAccountStore.InMemory(), host.Tokens),
            IPAddress.Loopback,
            0,
            zoneStatus: probe);
        using var client = new HttpClient { BaseAddress = new Uri(http.BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            await AssertStatus(client, zone: true, players: 0);

            await session.HelloAsync(timeout.Token);
            await session.AuthAsync("hero", "Fighter", timeout.Token);
            await AssertStatus(client, zone: true, players: 0);

            await session.EnterWorldAsync(timeout.Token);

            // The zone's own endpoint and the Login status both read the live count.
            using (var zoneClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                Assert.Equal("{\"playersOnline\":1}", await zoneClient.GetStringAsync(zoneStatus.Url));

            await AssertStatus(client, zone: true, players: 1);

            await session.DisconnectAsync();
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
}
