using System.Net;
using System.Net.Sockets;
using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Models;
using Xunit;

namespace Fineage.LoadTest.Tests;

public sealed class HandshakeGateTests
{
    [Fact]
    public async Task Enter_phase_gate_returns_instead_of_waiting_for_a_stuck_handshake()
    {
        var stuck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        bool completed = await LoadTestRunner.CompletesWithinAsync(
            stuck.Task, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.False(completed);
    }

    [Fact]
    public async Task Enter_phase_gate_returns_true_for_a_completed_phase()
    {
        bool completed = await LoadTestRunner.CompletesWithinAsync(
            Task.CompletedTask, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(completed);
    }

    [Fact]
    public async Task Bot_handshake_has_a_bounded_timeout_when_login_never_answers()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var accepted = new CancellationTokenSource();
        var accept = Task.Run(async () =>
        {
            try
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(accepted.Token);
                await Task.Delay(Timeout.InfiniteTimeSpan, accepted.Token);
            }
            catch (OperationCanceledException)
            {
            }
        });

        var scenario = new LoadScenario
        {
            Name = "handshake-timeout",
            BotCount = 1,
            LoginHost = IPAddress.Loopback.ToString(),
            LoginPort = port,
            GatekeeperTravelShare = 0
        };
        var stats = new BotStats { BotId = 0, Account = "gate0" };
        await using var bot = new FineageBotClient(scenario, 0, stats, new BotPeerDirectory())
        {
            HandshakeTimeout = TimeSpan.FromMilliseconds(200)
        };

        try
        {
            var ex = await Assert.ThrowsAsync<TimeoutException>(
                () => bot.ConnectAndEnterAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Contains("handshake exceeded", ex.Message);
        }
        finally
        {
            await accepted.CancelAsync();
            await accept;
            listener.Stop();
        }
    }
}
