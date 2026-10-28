using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneWireBoundaryTests
{
    [Fact]
    public async Task Register_on_zone_pipeline_returns_wrong_port()
    {
        var tokens = HandoffTokens.FromEnvironment();
        var server = new ZoneServer(tokens, new ZoneWorld());
        await using var host = await PipelineZoneHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();
        await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, MsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)MsgType.Register,
            MessagePackSerializer.Serialize(new RegisterMsg { Account = "x", Password = "yyyyyyyy" }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("wrong_port", err.Code);
    }

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
    public async Task CreateCharacter_on_zone_port_returns_wrong_port()
    {
        await AssertWrongPortAsync(MsgType.CreateCharacter,
            MessagePackSerializer.Serialize(new CreateCharacterMsg { Name = "X", ClassId = 1 }));
    }

    [Fact]
    public async Task SelectCharacter_on_zone_port_returns_wrong_port()
    {
        await AssertWrongPortAsync(MsgType.SelectCharacter,
            MessagePackSerializer.Serialize(new SelectCharacterMsg { CharacterId = 1, Name = "X" }));
    }

    [Fact]
    public async Task AuthToken_on_zone_port_returns_wrong_port()
    {
        await AssertWrongPortAsync((MsgType)7,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.AuthTokenMsg { Account = "hero" }));
    }

    [Fact]
    public async Task RequestServerList_on_zone_port_returns_wrong_port()
    {
        await AssertWrongPortAsync((MsgType)10, []);
    }

    [Fact]
    public async Task SelectServer_on_zone_port_returns_wrong_port()
    {
        await AssertWrongPortAsync((MsgType)11,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SelectServerMsg { ServerId = 1 }));
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
            var run = Task.Run(() => tcp.RunAsync("127.0.0.1", 0, 4, 8, "zone-test", server.HandleAsync, cts.Token));

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
