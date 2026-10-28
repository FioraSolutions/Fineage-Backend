using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Bosses;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneBossWireTests
{
    [Fact]
    public async Task Boss_status_and_admin_spawn_round_trip()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(
            tokens,
            new ZoneWorld(),
            () => DateTimeOffset.UtcNow,
            new ZoneServerOptions { AdminCommands = true });
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var ticket = login.Play("boss", "pass1234", "Scout", HumanClasses.Fighter);
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

        await FrameIO.SendAsync(stream, (ushort)MsgType.BossStatusRequest,
            MessagePackSerializer.Serialize(new BossStatusRequestMsg { NpcId = 29001 }));
        var status = await ReadAsync<BossStatusMsg>(stream, MsgType.BossStatus);
        var queen = Assert.Single(status.Bosses);
        Assert.Equal(29001, queen.NpcId);
        Assert.Equal(BossProtocol.KindGrand, queen.Kind);
        Assert.Equal(BossProtocol.StatusAlive, queen.Status);
        Assert.False(queen.Enabled);
        Assert.False(queen.Spawned);
        Assert.Equal(0, queen.RespawnUnixMs);
        Assert.Equal(queen.MaxHp, queen.CurrentHp);
        int objectId = queen.ObjectId;

        await FrameIO.SendAsync(stream, (ushort)MsgType.AdminBossSchedule,
            MessagePackSerializer.Serialize(new AdminBossScheduleMsg
            {
                NpcId = 29001,
                Action = BossProtocol.ActionForceSpawn
            }));
        var spawned = await ReadAsync<NpcInfoMsg>(stream, MsgType.NpcInfo);
        Assert.Equal(objectId, spawned.ObjectId);
        Assert.Equal(29001, spawned.NpcId);
        Assert.Equal("Queen Ant", spawned.Name);

        await FrameIO.SendAsync(stream, (ushort)MsgType.BossStatusRequest,
            MessagePackSerializer.Serialize(new BossStatusRequestMsg { NpcId = 29001 }));
        var alive = Assert.Single((await ReadAsync<BossStatusMsg>(stream, MsgType.BossStatus)).Bosses);
        Assert.True(alive.Spawned);
        Assert.Equal(BossProtocol.StatusAlive, alive.Status);

        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await FrameIO.SendAsync(stream, (ushort)MsgType.AdminBossSchedule,
            MessagePackSerializer.Serialize(new AdminBossScheduleMsg
            {
                NpcId = 29001,
                Action = BossProtocol.ActionMarkDead
            }));
        var deleted = await ReadAsync<DeleteObjectMsg>(stream, MsgType.DeleteObject);
        Assert.Equal(objectId, deleted.ObjectId);
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await FrameIO.SendAsync(stream, (ushort)MsgType.BossStatusRequest,
            MessagePackSerializer.Serialize(new BossStatusRequestMsg { NpcId = 29001 }));
        var dead = Assert.Single((await ReadAsync<BossStatusMsg>(stream, MsgType.BossStatus)).Bosses);
        Assert.Equal(BossProtocol.StatusDead, dead.Status);
        Assert.False(dead.Spawned);
        long min = before + BossCatalog.GrandWindow(36, 8).MinMillis;
        long max = after + BossCatalog.GrandWindow(36, 8).MaxMillis;
        Assert.InRange(dead.RespawnUnixMs, min, max);

        await FrameIO.SendAsync(stream, (ushort)MsgType.BossStatusRequest,
            MessagePackSerializer.Serialize(new BossStatusRequestMsg { NpcId = 1 }));
        var missing = await ReadErrorAsync(stream);
        Assert.Equal(BossProtocol.UnknownBoss, missing.Code);

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

    [Fact]
    public async Task Admin_boss_schedule_is_forbidden_when_admin_is_off()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(
            tokens,
            new ZoneWorld(),
            clock: null,
            options: new ZoneServerOptions { AdminCommands = false });
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var ticket = login.Play("boss", "pass1234", "Scout", HumanClasses.Fighter);
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
        await FrameIO.SendAsync(stream, (ushort)MsgType.AdminBossSchedule,
            MessagePackSerializer.Serialize(new AdminBossScheduleMsg
            {
                NpcId = 29001,
                Action = BossProtocol.ActionMarkDead
            }));
        var error = await ReadErrorAsync(stream);
        Assert.Equal(InventoryProtocol.Forbidden, error.Code);

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
        await ReadFrameAsync(stream, MsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)MsgType.Auth, MessagePackSerializer.Serialize(ticket.ToZoneAuth()));
        await ReadFrameAsync(stream, MsgType.AuthOk);
        await FrameIO.SendAsync(stream, (ushort)MsgType.EnterWorld, MessagePackSerializer.Serialize(new EnterWorldMsg()));
        await ReadFrameAsync(stream, MsgType.UserInfo);
        await ReadFrameAsync(stream, MsgType.ItemList);
        await ZoneEnterWire.ReadSkillListAsync(stream);
        await ReadFrameAsync(stream, MsgType.PlayerSpawned);
        for (int i = 0; i < TalkingIsland.NpcCount; i++)
            await ReadFrameAsync(stream, MsgType.NpcInfo);
        await ReadFrameAsync(stream, MsgType.SystemMessage);
    }

    static async Task<T> ReadAsync<T>(Stream stream, MsgType type)
    {
        var frame = await ReadFrameAsync(stream, type);
        return MessagePackSerializer.Deserialize<T>(frame.Body);
    }

    static async Task<(ushort Type, byte[] Body)> ReadFrameAsync(Stream stream, MsgType type)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)type, frame!.Value.Type);
        return frame.Value;
    }

    static async Task<ErrorMsg> ReadErrorAsync(Stream stream)
    {
        var frame = await ReadFrameAsync(stream, MsgType.Error);
        return MessagePackSerializer.Deserialize<ErrorMsg>(frame.Body);
    }
}
