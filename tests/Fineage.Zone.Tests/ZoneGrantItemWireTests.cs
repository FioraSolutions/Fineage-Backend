using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Slice A2 — authoritative grant into bag via InventoryUpdate.</summary>
public sealed class ZoneGrantItemWireTests
{
    const int AdenaId = 57;

    [Fact]
    public async Task GrantItemRequest_adds_adena_and_persists_on_leave()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var store = JsonAccountStore.InMemory();
        var server = new ZoneServer(tokens, new ZoneWorld(accounts: store));
        var login = new LoginService(store, tokens);
        var ticket = login.Play("grant", "pass1234", "Merchant", HumanClasses.Fighter);
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

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();

        await EnterWorldAsync(stream, ticket.Value!);
        await FrameIO.SendAsync(stream, (ushort)MsgType.GrantItemRequest,
            MessagePackSerializer.Serialize(new GrantItemRequestMsg { ItemId = AdenaId, Count = 25 }));

        var updateFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(updateFrame);
        Assert.Equal((ushort)MsgType.InventoryUpdate, updateFrame!.Value.Type);
        var update = MessagePackSerializer.Deserialize<InventoryUpdateMsg>(updateFrame.Value.Body);
        var row = Assert.Single(update.Changes);
        Assert.Equal(InventoryChange.Add, row.Change);
        Assert.Equal(AdenaId, row.ItemId);
        Assert.Equal(25, row.Count);
        Assert.Equal(-1, row.EquippedSlot);

        listener.Stop();
        await cts.CancelAsync();
        try
        {
            await acceptTask;
        }
        catch (OperationCanceledException)
        {
        }

        var saved = store.FindById(ticket.Value.Account, ticket.Value.CharacterId);
        Assert.Contains(saved!.Items!, item => item.ItemId == AdenaId && item.Count >= 25);
    }

    [Fact]
    public async Task GrantItemRequest_before_enter_returns_world_error()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var ticket = login.Play("early", "pass1234", "Early", HumanClasses.Fighter);
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

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, MsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)MsgType.Auth,
            MessagePackSerializer.Serialize(ticket.Value!.ToZoneAuth()));
        await ReadAsync(stream, MsgType.AuthOk);
        await FrameIO.SendAsync(stream, (ushort)MsgType.GrantItemRequest,
            MessagePackSerializer.Serialize(new GrantItemRequestMsg { ItemId = AdenaId, Count = 1 }));

        var err = await ReadErrorAsync(stream);
        Assert.Equal("world", err.Code);

        listener.Stop();
        await cts.CancelAsync();
        try
        {
            await acceptTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    static async Task EnterWorldAsync(Stream stream, HandoffTicket ticket)
    {
        await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, MsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)MsgType.Auth, MessagePackSerializer.Serialize(ticket.ToZoneAuth()));
        await ReadAsync(stream, MsgType.AuthOk);
        await FrameIO.SendAsync(stream, (ushort)MsgType.EnterWorld, MessagePackSerializer.Serialize(new EnterWorldMsg()));
        await ReadAsync(stream, MsgType.UserInfo);
        await ReadAsync(stream, MsgType.ItemList);
        await ReadAsync(stream, MsgType.PlayerSpawned);
        await SkipGremlinsAsync(stream);
        await ReadAsync(stream, MsgType.SystemMessage);
    }

    static async Task SkipGremlinsAsync(Stream stream)
    {
        for (int i = 0; i < TalkingIsland.Gremlins.Length; i++)
            await ReadAsync(stream, MsgType.NpcInfo);
    }

    static async Task ReadAsync(Stream stream, MsgType type)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)type, frame!.Value.Type);
    }

    static async Task<ErrorMsg> ReadErrorAsync(Stream stream)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.Error, frame!.Value.Type);
        return MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
    }
}
