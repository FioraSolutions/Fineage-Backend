using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneWireBoundaryTests
{
    [Fact]
    public async Task Register_on_zone_port_returns_wrong_port()
    {
        await AssertWrongPortAsync(MsgType.Register,
            MessagePackSerializer.Serialize(new RegisterMsg { Account = "x", Password = "yyyy" }));
    }

    [Fact]
    public async Task ListCharacters_on_zone_port_returns_wrong_port()
    {
        await AssertWrongPortAsync(MsgType.ListCharacters, []);
    }

    [Fact]
    public async Task AuthToken_on_zone_port_returns_wrong_port()
    {
        await AssertWrongPortAsync((MsgType)7,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.AuthTokenMsg { Account = "hero" }));
    }

    static async Task AssertWrongPortAsync(MsgType opcode, byte[] body)
    {
        var tokens = HandoffTokens.FromEnvironment();
        var server = new ZoneServer(tokens);
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
        await FrameIO.SendAsync(stream, (ushort)opcode, body);

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("wrong_port", err.Code);

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

    static async Task ReadAsync(NetworkStream stream, MsgType expected)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)expected, frame!.Value.Type);
    }
}
