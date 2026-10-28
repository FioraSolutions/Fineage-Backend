using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using MessagePack;
using Xunit;
using ProtocolMsgType = Fineage.Protocol.MsgType;

namespace Fineage.Login.Tests;

[CollectionDefinition("Tcp idle environment", DisableParallelization = true)]
public sealed class TcpIdleEnvironmentCollection;

[Collection("Tcp idle environment")]
public sealed class LoginIdleTimeoutTests
{
    [Fact]
    public async Task Pipeline_idle_timeout_returns_error()
    {
        string? priorIdle = Environment.GetEnvironmentVariable("FINEAGE_TCP_IDLE_SECONDS");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_TCP_IDLE_SECONDS", "2");
            var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
            var server = new LoginServer(service);
            await using var host = await PipelineLoginHost.StartAsync(server);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, host.Port);
            await using var stream = client.GetStream();
            await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello,
                MessagePackSerializer.Serialize(new HelloMsg()));
            var ack = await FrameIO.ReadAsync(stream);
            Assert.NotNull(ack);
            Assert.Equal((ushort)ProtocolMsgType.HelloAck, ack!.Value.Type);

            await Task.Delay(TimeSpan.FromSeconds(2.5));

            var frame = await FrameIO.ReadAsync(stream);
            Assert.NotNull(frame);
            Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
            var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
            Assert.Equal("idle_timeout", err.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_TCP_IDLE_SECONDS", priorIdle);
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
}
