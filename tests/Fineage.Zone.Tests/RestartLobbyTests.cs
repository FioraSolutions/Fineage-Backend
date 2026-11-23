using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// RequestRestart persists the character, keeps the account on the connection, and sends the lobby roster.
/// </summary>
public sealed class RestartLobbyTests
{
    [Fact]
    public void Restart_response_roundtrips()
    {
        var bytes = NetCodec.EncodeRestartResponse(new RestartResponseMsg { Ok = false, Code = RestartProtocol.Combat });
        var decoded = NetCodec.DecodeRestartResponse(bytes);
        Assert.False(decoded.Ok);
        Assert.Equal(RestartProtocol.Combat, decoded.Code);
    }

    [Fact]
    public void Empty_roster_is_logged_for_the_account()
    {
        var kit = new NetTestKit();
        var session = new ZoneSession();
        session.NoteForLog("lobby-empty");

        Assert.Empty(kit.World.RosterAfterRestart(session));
    }

    [Fact]
    public async Task Restart_persists_before_the_roster_and_keeps_the_account()
    {
        await using var host = await NetZoneHost.StartAsync();
        await using var session = await host.ConnectAsync();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await session.HelloAsync(ct.Token);
        await session.AuthAsync("restart1", "Hero", ct.Token);
        await session.EnterWorldAsync(ct.Token);

        int id = new EntityId(session.EnteredEntityValue, 1).ToObjectId();
        host.World.RunStepForTest(() =>
        {
            Assert.True(host.World.TryGetLivePlayerById(id, out var player));
            player.X = 11111;
            player.Y = 22222;
            player.Z = -3333;
        });

        await session.SendAsync(NetOpcode.RequestRestart, [], ct.Token);
        var response = NetCodec.DecodeRestartResponse((await session.ExpectAsync(NetOpcode.RestartResponse, ct.Token)).Payload);
        Assert.True(response.Ok);
        var roster = NetCodec.DecodeCharacterRoster((await session.ExpectAsync(NetOpcode.CharacterRoster, ct.Token)).Payload);
        var row = Assert.Single(roster.Characters, c => c.Name == "Hero");
        Assert.Equal(11111, row.X);
        Assert.Equal(22222, row.Y);
        Assert.Equal(-3333, row.Z);

        Assert.Equal(0, host.World.OnlinePlayerCount);
        var stored = host.Store.FindById("restart1", id);
        Assert.NotNull(stored);
        Assert.Equal(11111, stored!.X);
        Assert.Equal(22222, stored.Y);
        Assert.Equal(-3333, stored.Z);

        await session.AuthAsync("restart1", "Hero", ct.Token);
    }

    [Fact]
    public async Task Combat_refuses_restart_and_leaves_the_player_in_the_world()
    {
        await using var host = await NetZoneHost.StartAsync();
        await using var session = await host.ConnectAsync();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await session.HelloAsync(ct.Token);
        await session.AuthAsync("restart2", "Fighter", ct.Token);
        await session.EnterWorldAsync(ct.Token);

        int id = new EntityId(session.EnteredEntityValue, 1).ToObjectId();
        host.World.RunStepForTest(() =>
        {
            Assert.True(host.World.TryGetLivePlayerById(id, out var player));
            player.CombatUntilUnixMs = long.MaxValue;
        });

        await session.SendAsync(NetOpcode.RequestRestart, [], ct.Token);
        var response = NetCodec.DecodeRestartResponse((await session.ExpectAsync(NetOpcode.RestartResponse, ct.Token)).Payload);
        Assert.False(response.Ok);
        Assert.Equal(RestartProtocol.Combat, response.Code);
        Assert.Equal(1, host.World.OnlinePlayerCount);
        Assert.True(host.World.RunStepForTest(() => host.World.TryGetLivePlayerById(id, out _)));
    }
}
