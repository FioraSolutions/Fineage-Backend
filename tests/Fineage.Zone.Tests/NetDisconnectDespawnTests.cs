using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Dropping the socket has to remove the leaver from everyone who can still see them: Net fans
/// <c>EntityDespawn</c> to the knownlist on the leave path, and the observer must not get a second
/// remove for the same entity afterwards.
/// </summary>
public sealed class NetDisconnectDespawnTests
{
    [Fact]
    public async Task Disconnect_broadcasts_EntityDespawn_to_the_watcher()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await NetZoneHost.StartAsync();
        await using var leaver = await host.ConnectAsync();
        await using var watcher = await host.ConnectAsync();

        await leaver.HelloAsync(timeout.Token);
        await leaver.AuthAsync("leavea", "HumanOne", timeout.Token);
        await leaver.EnterWorldAsync(timeout.Token);

        await watcher.HelloAsync(timeout.Token);
        await watcher.AuthAsync("leaveb", "HumanTwo", timeout.Token);
        await watcher.EnterWorldAsync(timeout.Token);

        Assert.Equal(2, host.World.OnlinePlayerCount);
        int leaverObjectId = leaver.Ticket("leavea", "HumanOne").CharacterId;

        await leaver.DisconnectAsync();

        PacketEnvelope despawn = await watcher.ExpectAsync(NetOpcode.EntityDespawn, timeout.Token);
        EntityDespawnMsg removed = NetCodec.DecodeEntityDespawn(despawn.Payload);
        Assert.Equal((ulong)(uint)leaverObjectId, removed.EntityValue);

        Assert.Equal(1, host.World.OnlinePlayerCount);
    }

    [Fact]
    public async Task A_leaver_is_gone_from_the_watchers_knownlist()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await NetZoneHost.StartAsync();
        await using var leaver = await host.ConnectAsync();
        await using var watcher = await host.ConnectAsync();

        await leaver.HelloAsync(timeout.Token);
        await leaver.AuthAsync("leavea", "HumanOne", timeout.Token);
        await leaver.EnterWorldAsync(timeout.Token);

        await watcher.HelloAsync(timeout.Token);
        await watcher.AuthAsync("leaveb", "HumanTwo", timeout.Token);
        await watcher.EnterWorldAsync(timeout.Token);

        await leaver.DisconnectAsync();
        await watcher.ExpectAsync(NetOpcode.EntityDespawn, timeout.Token);

        // Nothing further about the leaver reaches the watcher: one despawn, not a re-spawn or a status line.
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        quiet.CancelAfter(TimeSpan.FromMilliseconds(400));
        while (true)
        {
            PacketEnvelope? trailing;
            try
            {
                trailing = await NetFraming.ReadAsync(watcher.Stream, quiet.Token);
            }
            catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
            {
                break;
            }

            Assert.NotEqual(NetOpcode.EntityDespawn, trailing?.Opcode);
        }
    }
}
