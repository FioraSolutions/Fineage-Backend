using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using MessagePack;
using Xunit;
using ProtocolMsgType = Fineage.Protocol.MsgType;
using V2MsgType = Fineage.Protocol.V2.MsgType;

namespace Fineage.Login.Tests;

public sealed class LoginWireBoundaryTests
{
    [Fact]
    public async Task MoveTo_on_login_port_returns_wrong_endpoint()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);

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
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.MoveTo,
            MessagePackSerializer.Serialize(new MoveToMsg()));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("wrong_endpoint", err.Code);

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
    public async Task MoveTo_on_login_pipeline_returns_wrong_endpoint()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.MoveTo,
            MessagePackSerializer.Serialize(new MoveToMsg()));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("wrong_endpoint", err.Code);
    }

    [Fact]
    public async Task AuthToken_on_login_pipeline_returns_not_implemented()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)V2MsgType.AuthToken,
            MessagePackSerializer.Serialize(new Fineage.Protocol.V2.AuthTokenMsg { Account = "hero" }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("not_implemented", err.Code);
        Assert.Contains("Auth (2)", err.Message);
    }

    [Fact]
    public async Task Say_on_login_pipeline_returns_wrong_endpoint()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Say,
            MessagePackSerializer.Serialize(new SayMsg { Text = "hello", ChatType = 0 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("wrong_endpoint", err.Code);
    }

    [Fact]
    public async Task Say_on_login_port_returns_wrong_endpoint()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);

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
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, ProtocolMsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Say,
            MessagePackSerializer.Serialize(new SayMsg { Text = "hello", ChatType = 0 }));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("wrong_endpoint", err.Code);

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

    sealed class PipelineLoginHost : IAsyncDisposable
    {
        readonly CancellationTokenSource _cts;
        readonly Task _run;

        PipelineLoginHost(int port, CancellationTokenSource cts, Task run)
        {
            Port = port;
            _cts = cts;
            _run = run;
        }

        public int Port { get; }

        public static async Task<PipelineLoginHost> StartAsync(LoginServer server)
        {
            var cts = new CancellationTokenSource();
            var tcp = new TcpServer();
            var run = Task.Run(() => tcp.RunAsync("127.0.0.1", 0, 4, 8, "login-test", server.HandleAsync, cts.Token));

            for (int i = 0; i < 100; i++)
            {
                if (tcp.BoundPort != 0)
                    return new PipelineLoginHost(tcp.BoundPort, cts, run);

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

    static async Task ReadAsync(NetworkStream stream, ProtocolMsgType expected)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)expected, frame!.Value.Type);
    }
}
