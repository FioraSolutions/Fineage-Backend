using System.Net.Sockets;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Net enter-world packet order. Mobius <c>EnterWorld.java</c> sends <c>UserInfo</c> (153), <c>ItemList</c> (343),
/// <c>ShortCutInit</c> (346) and only then <c>EtcStatusUpdate</c> (401), so the overload icon lands on an already
/// dressed status bar. Fineage adds <c>EntitySpawn</c>/<c>PlayerStats</c> in front because its client needs the entity
/// identity and the live stat sheet before it can render any of them.
/// </summary>
public sealed class NetEnterWorldOrderTests
{
    [Fact]
    public async Task Etc_status_is_sent_after_item_list_and_shortcut_init()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var store = MemoryAccountStore.InMemory();
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(store, tokens);
        var ticket = login.Play("wireorder", "pass1234", "Orderly", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        var world = new ZoneWorld(accounts: store);
        var zone = new NetZoneServer(tokens, world);
        var server = new NetTcpServer();
        var host = Task.Run(
            () => server.RunAsync("127.0.0.1", 0, zone.HandleAsync, timeout.Token, maxConnections: 4, inboxCapacity: 64),
            timeout.Token);

        int port = 0;
        for (int i = 0; i < 100 && port == 0; i++)
        {
            await Task.Delay(20, timeout.Token);
            port = server.BoundPort;
        }

        Assert.True(port > 0, "zone net server did not bind");

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port, timeout.Token);
        await using var stream = client.GetStream();

        await NetFraming.WriteAsync(stream, new PacketEnvelope
        {
            Opcode = NetOpcode.Hello,
            RequestSequence = 1,
            Payload = NetCodec.EncodeHello(new HelloMsg { ClientName = "order-test" })
        }, timeout.Token);
        Assert.NotNull(await NetFraming.ReadAsync(stream, timeout.Token));

        await NetFraming.WriteAsync(stream, new PacketEnvelope
        {
            Opcode = NetOpcode.ZoneAuth,
            RequestSequence = 2,
            Payload = NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = ticket.Value!.Account, Token = ticket.Value.Token })
        }, timeout.Token);
        var auth = await NetFraming.ReadAsync(stream, timeout.Token);
        Assert.NotNull(auth);
        Assert.True(NetCodec.DecodeZoneAuthResult(auth!.Payload).Ok);

        await NetFraming.WriteAsync(stream, new PacketEnvelope
        {
            Opcode = NetOpcode.EnterWorld,
            RequestSequence = 3,
            Payload = NetCodec.EncodeEnterWorld(new EnterWorldMsg())
        }, timeout.Token);

        // Read the enter burst up to and including EtcStatusUpdate; the known-entity spawns follow it.
        var order = new List<ushort>();
        EtcStatusUpdateNetMsg? etc = null;
        while (etc == null)
        {
            var frame = await NetFraming.ReadAsync(stream, timeout.Token);
            Assert.NotNull(frame);
            order.Add(frame!.Opcode);
            if (frame.Opcode == NetOpcode.EtcStatusUpdate)
                etc = NetCodec.DecodeEtcStatusUpdate(frame.Payload);
        }

        Assert.Contains(NetOpcode.UserInfo, order);
        Assert.Contains(NetOpcode.ItemList, order);
        Assert.Contains(NetOpcode.SkillList, order);
        Assert.Contains(NetOpcode.ShortCutInit, order);
        Assert.True(
            order.IndexOf(NetOpcode.EtcStatusUpdate) > order.IndexOf(NetOpcode.ItemList),
            $"EtcStatusUpdate must follow ItemList (got {string.Join(",", order)})");
        Assert.True(
            order.IndexOf(NetOpcode.EtcStatusUpdate) > order.IndexOf(NetOpcode.ShortCutInit),
            $"EtcStatusUpdate must follow ShortCutInit (got {string.Join(",", order)})");

        // Human Fighter CON 43 → limit floor(1.58 * 69000) = 109020; the starter kit is ~40k, so a fresh character is
        // not overloaded, but the packet still goes out with level 0 so the client clears any stale icon.
        Assert.True(NetOpcode.EtcStatusUpdate == 0x035D);
        Assert.InRange(etc!.WeightPenalty, 0, 4);
        Assert.True(world.TryGetLivePlayerById(ticket.Value.CharacterId, out var live));
        Assert.Equal(ZoneWorld.EtcStatusOf(live).WeightPenalty, etc.WeightPenalty);

        await timeout.CancelAsync();
        try { await host; } catch (OperationCanceledException) { /* expected */ }
    }
}
