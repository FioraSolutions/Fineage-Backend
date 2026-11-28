using System.Net;
using Fineage.Game.Punishment;
using Fineage.Login.Net;
using Fineage.Net.Host;
using Fineage.Protocol.Health;
using Fineage.Protocol.Telemetry;
using Fineage.Accounts;
using Fineage.Game.Economy;

namespace Fineage.Login;

public static partial class Cli
{
    /// <summary>
    /// #989 Login readiness: the database answers and every migration of this build is applied (checked at most every
    /// 2 s, 3 s connect limit). An in-memory store (tests, tools) has no database to lose.
    /// </summary>
    internal static HealthBoard LoginHealth(IAccountStore accounts)
    {
        var board = new HealthBoard();
        if (accounts is PostgresAccountStore postgres)
        {
            var probe = new CachedProbe(() => postgres.CheckDatabase(TimeSpan.FromSeconds(3)), TimeSpan.FromSeconds(2));
            board.AddReadyProbe("database", probe.Read);
        }
        else
        {
            board.AddReadyProbe("database", () => new HealthCheck("database", true, $"{accounts.GetType().Name}: no external database"));
        }

        return board;
    }

    static async Task<bool> ListenAsync(
        IAccountStore? store,
        string? databaseUrl,
        IPAddress bindAddress,
        string bindDisplay,
        int httpPort,
        TextWriter output,
        TextWriter error)
    {
        IAccountStore accounts;
        try
        {
            accounts = store ?? AccountStoreFactory.Open(databaseUrl);
        }
        catch (Exception ex)
        {
            error.WriteLine($"[LOGIN] {ex.Message}");
            return false;
        }

        MemoryHandoffStore handoff;
        try
        {
            handoff = MemoryHandoffStore.FromEnvironment(
                allowDefaultSecret: HandoffTokens.AllowDefaultSecretForBind(bindAddress));
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine($"[LOGIN] {ex.Message}");
            return false;
        }

        // #996: the zone's punishments in the shared database, not a punishments.json beside the zone.
        var accountBans = WorldStatePunishments.Gate(accounts);
        EconomyLedger? ledger = null;
        LedgerTxSequence? ledgerIds = null;
        if (accounts is PostgresAccountStore postgres)
        {
            ledger = new EconomyLedger(postgres.OpenLedgerJournal(), StarterKitLedger.IdempotencyWallMilliseconds);
            ledgerIds = new LedgerTxSequence();
        }

        var service = new LoginService(
            accounts,
            handoff.Tokens,
            deleteAfterDays: CharacterRules.DeleteAfterDays,
            create: CharacterCreateOptions.FromEnvironment(),
            accountBans: accountBans,
            ledger: ledger,
            ledgerIds: ledgerIds);
        LoginHttpHost? http = null;
        string host = bindAddress.Equals(IPAddress.Any) ? "0.0.0.0" : bindDisplay;
        int maxConnections = LoginNetSettings.MaxConnections;
        int outboundQueue = LoginNetSettings.OutboundQueue;
        int maxMsgPerSec = LoginNetSettings.MaxMessagesPerSecond;

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };
        // A service manager or container stops Login with SIGTERM. Without this the runtime exits on it
        // straight away and in-flight work (HTTP, Net accept/handlers) is not given a clean stop (#993).
        using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM,
            context =>
            {
                context.Cancel = true;
                shutdown.Cancel();
            });

        // Start Net TCP before HTTP so PacketEnvelope login works even if Kestrel hangs.
        // One zone endpoint for the handoff ticket, the startup log and the clients: ZoneHandoff
        // owns the FINEAGE_ZONE_PORT / FINEAGE_NET_ZONE_PORT precedence. Using NetSettings.ZonePort
        // here while the log used AdvertisePort let a stale env var advertise a dead source port.
        var netLogin = new NetLoginServer(service, zoneHost: ZoneHandoff.AdvertiseHost, zonePort: ZoneHandoff.AdvertisePort);
        var netTcp = new NetTcpServer { Endpoint = "login" };
        output.WriteLine("[LOGIN] account login bans from database world_state 'punishment'");

        output.WriteLine($"[LOGIN] net listen {bindDisplay}:{NetSettings.LoginPort} (PacketEnvelope)");
        Task netTask = netTcp.RunAsync(
            host,
            NetSettings.LoginPort,
            netLogin.HandleAsync,
            shutdown.Token,
            maxConnections,
            NetSettings.InboxCapacity,
            outboundQueue);

        HttpZoneStatusSource? zoneProbe = HttpZoneStatusSource.FromEnvironment(error);
        try
        {
            IZoneStatusSource zoneStatus = zoneProbe != null
                ? zoneProbe
                : UnavailableZoneStatus.Instance;
            var loginHealth = LoginHealth(accounts);
            // #992: /metrics exports the same liveness/readiness the HTTP host serves.
            Fineage.Protocol.Telemetry.FineageTelemetry.Health = loginHealth;
            http = await LoginHttpHost.StartAsync(
                service,
                bindAddress,
                httpPort,
                zoneStatus: zoneStatus,
                health: loginHealth).ConfigureAwait(false);
            zoneProbe = null;
        }
        catch (Exception ex)
        {
            zoneProbe?.Dispose();
            if (netTask == null)
            {
                error.WriteLine($"listen failed on {bindDisplay} http={httpPort}: {ex.Message}");
                return false;
            }

            error.WriteLine($"[LOGIN] http {httpPort} failed ({ex.Message}); continuing with Net only");
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_SECRET")) &&
            IPAddress.IsLoopback(bindAddress))
        {
            output.WriteLine("[LOGIN] FINEAGE_HANDOFF_SECRET unset; using DefaultSecret (loopback only).");
        }

        output.WriteLine(
            $"[LOGIN] listen net={bindDisplay}:{NetSettings.LoginPort} http={(http != null ? http.BaseUrl : "off")} store=postgres (maxConn={maxConnections} msgPerSec={maxMsgPerSec})");
        if (http != null)
        {
            output.WriteLine(http.ZoneStatusProbe == null
                ? "[LOGIN] GET /api/status (zone probe disabled)"
                : $"[LOGIN] GET /api/status (zone probe {http.ZoneStatusProbe})");
        }

        output.WriteLine($"[LOGIN] zone handoff target {ZoneHandoff.AdvertiseHost}:{ZoneHandoff.AdvertisePort}");
        if (CharacterCreateOptions.EnvironmentUsesLegacyHumanOnly())
        {
            output.WriteLine(
                "[LOGIN] ignoring Human-only FINEAGE_ALLOWED_RACES/CLASSES; all Interlude base races are creatable.");
        }

        output.WriteLine("[LOGIN] creatable classes " + FormatIds(service.CreatableClassIds));

        await using FineageMetricsHost? metrics = FineageMetricsHost.Start("login", output, error);

        if (netTask == null)
        {
            error.WriteLine("[LOGIN] nothing to serve: Net is always on, this should be unreachable");
            return false;
        }

        try
        {
            await netTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error.WriteLine($"listen failed on {bindDisplay}: {ex.Message}");
            return false;
        }
        finally
        {
            shutdown.Cancel();
            if (http != null)
                await http.DisposeAsync().ConfigureAwait(false);
        }

        return true;
    }

    static string FormatIds(IReadOnlySet<int> ids)
    {
        var list = new int[ids.Count];
        int n = 0;
        foreach (int id in ids)
            list[n++] = id;
        Array.Sort(list);
        return string.Join(',', list);
    }
}
