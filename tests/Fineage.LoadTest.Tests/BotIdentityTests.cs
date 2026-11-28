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
/// Character names are global on the Login server, so the load-test identity must be namespaced by the same
/// account prefix that namespaces the accounts. Otherwise a second run with fresh accounts collides with the first
/// run's characters and every create fails with <c>name_taken</c> (see #1047).
/// </summary>
public sealed class BotIdentityTests
{
    const string Password = "loadtest-pass-v1";

    [Fact]
    public void Independent_prefixes_get_distinct_character_names_for_the_same_bot()
    {
        string first = BotIdentity.CharacterName("s1154", 0);
        string second = BotIdentity.CharacterName("s1155", 0);

        Assert.NotEqual(first, second, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith("s1154", first, StringComparison.Ordinal);
        Assert.StartsWith("s1155", second, StringComparison.Ordinal);
        Assert.True(NameRules.IsCharacterName(first), first);
        Assert.True(NameRules.IsCharacterName(second), second);
    }

    [Fact]
    public void Character_name_is_deterministic_across_reconnects_of_the_same_run()
    {
        Assert.Equal(
            BotIdentity.CharacterName("s1154", 17),
            BotIdentity.CharacterName("s1154", 17));
        Assert.Equal(
            BotIdentity.AccountName("s1154", 17),
            BotIdentity.AccountName("s1154", 17));
    }

    [Fact]
    public void Default_prefix_keeps_the_legacy_character_names()
    {
        Assert.Equal("Bot0000", BotIdentity.CharacterName(BotIdentity.DefaultAccountPrefix, 0));
        Assert.Equal("Bot0042", BotIdentity.CharacterName("loadbot", 42));
        Assert.Equal("Bot0042", BotIdentity.CharacterName("LoadBot", 42));
    }

    [Fact]
    public void Every_prefix_length_produces_a_valid_name()
    {
        for (int length = 1; length <= 64; length++)
        {
            string prefix = "p" + new string('a', length - 1);
            foreach (int botId in new[] { 0, 1, 999, 9999, 10000 })
            {
                string name = BotIdentity.CharacterName(prefix, botId);
                Assert.InRange(name.Length, BotIdentity.MinCharacterNameLength, BotIdentity.MaxCharacterNameLength);
                Assert.True(NameRules.IsCharacterName(name), $"prefix length {length}, bot {botId}: {name}");
            }
        }
    }

    [Fact]
    public void Long_prefixes_that_share_a_readable_head_stay_distinct()
    {
        string head = new('a', 11);
        string first = BotIdentity.CharacterName(head + "b", 0);
        string second = BotIdentity.CharacterName(head + "c", 0);

        Assert.NotEqual(first, second, StringComparer.OrdinalIgnoreCase);
        Assert.True(NameRules.IsCharacterName(first), first);
        Assert.True(NameRules.IsCharacterName(second), second);
    }

    [Fact]
    public void Fleet_names_are_unique_inside_one_prefix()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int botId = 0; botId < 2000; botId++)
            Assert.True(seen.Add(BotIdentity.CharacterName("s1154", botId)), $"duplicate name for bot {botId}");
    }

    [Fact]
    public async Task Two_prefixes_create_their_own_characters_on_one_login_store()
    {
        await using var login = await SharedLoginHarness.StartAsync("s115400000", "s115500000");

        var firstStats = new BotStats { BotId = 0 };
        await using (var first = new FineageBotClient(Scenario(login.Port, "s1154"), 0, firstStats, new BotPeerDirectory()))
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(
                () => first.ConnectAndEnterAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsAssignableFrom<SocketException>(ex);
            Assert.Equal("zone_connect", firstStats.EntryPhase);
        }

        var created = login.Store.FindByCharacterName(BotIdentity.CharacterName("s1154", 0));
        Assert.NotNull(created);
        Assert.Equal("s115400000", created!.Account);

        var secondStats = new BotStats { BotId = 0 };
        await using (var second = new FineageBotClient(Scenario(login.Port, "s1155"), 0, secondStats, new BotPeerDirectory()))
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(
                () => second.ConnectAndEnterAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsAssignableFrom<SocketException>(ex);
            Assert.Equal("zone_connect", secondStats.EntryPhase);
        }

        var secondCreated = login.Store.FindByCharacterName(BotIdentity.CharacterName("s1155", 0));
        Assert.NotNull(secondCreated);
        Assert.Equal("s115500000", secondCreated!.Account);
        Assert.NotEqual(created.Name, secondCreated.Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_existing_roster_is_reused_instead_of_created_again()
    {
        const string prefix = "s1154";
        const string account = prefix + "00000";
        await using var login = await SharedLoginHarness.StartAsync(account);
        Assert.Equal(
            CreateCharacterStatus.Ok,
            login.Store.TryAddCharacter(account, "LegacyHero", HumanClasses.Fighter, HumanClasses.RaceHuman,
                HumanClasses.SexMale, out _));

        var stats = new BotStats { BotId = 0 };
        await using var bot = new FineageBotClient(Scenario(login.Port, prefix), 0, stats, new BotPeerDirectory());

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => bot.ConnectAndEnterAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.IsAssignableFrom<SocketException>(ex);
        Assert.Equal("zone_connect", stats.EntryPhase);
        Assert.NotNull(login.Store.FindByCharacterName("LegacyHero"));
        // No second character was created under the prefix-derived name.
        Assert.Null(login.Store.FindByCharacterName(BotIdentity.CharacterName(prefix, 0)));
    }

    static LoadScenario Scenario(int loginPort, string prefix) => new()
    {
        Name = "bot-identity",
        BotCount = 1,
        AccountPrefix = prefix,
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

    /// <summary>One in-memory Login store served over several sequential bot connections.</summary>
    sealed class SharedLoginHarness : IAsyncDisposable
    {
        readonly TcpListener _listener;
        readonly CancellationTokenSource _cts = new();
        Task _serve = Task.CompletedTask;

        SharedLoginHarness(TcpListener listener, int port, MemoryAccountStore store)
        {
            _listener = listener;
            Port = port;
            Store = store;
        }

        public int Port { get; }
        public MemoryAccountStore Store { get; }

        public static Task<SharedLoginHarness> StartAsync(params string[] accounts)
        {
            var store = MemoryAccountStore.InMemory();
            var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
            var service = new LoginService(store, tokens, create: new CharacterCreateOptions());
            foreach (string account in accounts)
                Assert.True(service.Register(account, Password).Ok, $"failed to register {account}");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var harness = new SharedLoginHarness(listener, port, store);
            var server = new NetLoginServer(service, zonePort: UnusedPort());
            harness._serve = Task.Run(() => harness.ServeAsync(server), CancellationToken.None);
            return Task.FromResult(harness);
        }

        async Task ServeAsync(NetLoginServer server)
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient accepted = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = HandleAsync(server, accepted);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
        }

        async Task HandleAsync(NetLoginServer server, TcpClient accepted)
        {
            using (accepted)
            {
                using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                await using var conn = new NetConnection(accepted, 256);
                Task read = conn.RunReadLoopAsync(connectionCts.Token);
                try
                {
                    await server.HandleAsync(conn, connectionCts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    await connectionCts.CancelAsync();
                    try { await read; } catch { /* shutdown */ }
                }
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
