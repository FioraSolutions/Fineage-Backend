using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using MessagePack;
using Xunit;

namespace Fineage.Login.Tests;

public sealed class LoginRateLimitTests
{
    [Fact]
    public async Task Pipeline_session_closes_after_burst_over_limit()
    {
        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service, new LoginServerOptions { MaxMessagesPerSecond = 2 });

        await using var host = await PipelineLoginHost.StartAsync(server);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);

        await using var stream = client.GetStream();
        for (int i = 0; i < 3; i++)
        {
            await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        }

        for (int i = 0; i < 2; i++)
        {
            var ack = await FrameIO.ReadAsync(stream, CancellationToken.None);
            Assert.NotNull(ack);
            Assert.Equal((ushort)MsgType.HelloAck, ack.Value.Type);
        }

        var frame = await FrameIO.ReadAsync(stream, CancellationToken.None);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.Error, frame.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("rate_limit", err.Code);

        Assert.Null(await FrameIO.ReadAsync(stream, CancellationToken.None));
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
}
