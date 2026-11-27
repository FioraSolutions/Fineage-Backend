using System.Net;
using System.Net.Sockets;
using Fineage.Accounts;
using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Models;
using Fineage.Login;
using Fineage.Login.Net;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.LoadTest.Tests;

/// <summary>
/// Entry handshake failures must name the protocol step and the server's rejection code. A failed create used to
/// fall through to <c>roster.Characters[0]</c> and surface only as "Index was outside the bounds of the array."
/// </summary>
public sealed class BotEntryHandshakeTests
{
    const string Account = "loadbot00000";
    const string Password = "loadtest-pass-v1";

    [Fact]
    public async Task Create_rejection_is_reported_with_the_server_code_not_an_index_error()
    {
        await using var login = await LoginHarness.StartAsync(new CharacterCreateOptions { CreateAllowed = false });

        var stats = new BotStats { BotId = 0, Account = Account };
        await using var bot = new FineageBotClient(Scenario(login.Port), 0, stats, new BotPeerDirectory());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.ConnectAndEnterAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Contains("character create rejected", ex.Message);
        Assert.Contains("create_not_allowed", ex.Message);
        Assert.DoesNotContain("outside the bounds", ex.Message);
        Assert.Equal("login_create_character", stats.EntryPhase);
    }

    [Fact]
    public async Task Enter_failure_keeps_the_real_exception_stack_and_phase()
    {
        await using var login = await LoginHarness.StartAsync(new CharacterCreateOptions { CreateAllowed = false });

        var stats = new BotStats { BotId = 0, Account = Account };
        await using var bot = new FineageBotClient(Scenario(login.Port), 0, stats, new BotPeerDirectory());
        using var slots = new SemaphoreSlim(1, 1);
        slots.Wait();

        await LoadTestRunner.EnterOneAsync(bot, slots, CancellationToken.None);

        Assert.True(stats.Disconnected);
        Assert.Equal("login_create_character", stats.EntryPhase);
        Assert.NotNull(stats.EntryFailure);
        Assert.Contains("InvalidOperationException", stats.EntryFailure);
        Assert.Contains("character create rejected", stats.EntryFailure);
        // A real stack, not just ex.Message.
        Assert.Contains(" at ", stats.EntryFailure);
    }

    [Fact]
    public async Task Successful_login_reaches_zone_connect_and_keeps_the_bounded_handshake()
    {
        await using var login = await LoginHarness.StartAsync(new CharacterCreateOptions());
        int closedZonePort = UnusedPort();

        var stats = new BotStats { BotId = 0, Account = Account };
        await using var bot = new FineageBotClient(Scenario(login.Port), 0, stats, new BotPeerDirectory());

        // The handoff points at a closed port: the failure is the zone socket, which proves login (including the
        // character create + roster read) completed and the bot moved on to the next phase.
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => bot.ConnectAndEnterAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.IsAssignableFrom<SocketException>(ex);
        Assert.Equal("zone_connect", stats.EntryPhase);
        Assert.False(stats.InWorld);
    }

    static LoadScenario Scenario(int loginPort) => new()
    {
        Name = "bot-entry-handshake",
        BotCount = 1,
        LoginHost = IPAddress.Loopback.ToString(),
        LoginPort = loginPort,
        GatekeeperTravelShare = 0
    };

    static int UnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    sealed class LoginHarness : IAsyncDisposable
    {
        readonly TcpListener _listener;
        readonly CancellationTokenSource _cts = new();
        Task _serve = Task.CompletedTask;

        LoginHarness(TcpListener listener, int port)
        {
            _listener = listener;
            Port = port;
        }

        public int Port { get; }

        public static Task<LoginHarness> StartAsync(CharacterCreateOptions create)
        {
            var store = MemoryAccountStore.InMemory();
            var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
            var service = new LoginService(store, tokens, create: create);
            Assert.True(service.Register(Account, Password).Ok);
            int zonePort = UnusedPort();
            var server = new NetLoginServer(service, zonePort: zonePort);

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var harness = new LoginHarness(listener, port);
            harness._serve = Task.Run(() => harness.ServeAsync(server), CancellationToken.None);
            return Task.FromResult(harness);
        }

        async Task ServeAsync(NetLoginServer server)
        {
            try
            {
                using TcpClient accepted = await _listener.AcceptTcpClientAsync(_cts.Token);
                await using var conn = new NetConnection(accepted, 256);
                Task read = conn.RunReadLoopAsync(_cts.Token);
                try
                {
                    await server.HandleAsync(conn, _cts.Token);
                }
                finally
                {
                    await _cts.CancelAsync();
                    try { await read; } catch { /* shutdown */ }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            try { await _serve.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* shutdown */ }
            _cts.Dispose();
        }
    }
}
