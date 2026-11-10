using System.Net;
using Fineage.Login.Net;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Fineage.Accounts;

namespace Fineage.Login;

public static partial class Cli
{
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

        var service = new LoginService(
            accounts,
            handoff.Tokens,
            deleteAfterDays: CharacterRules.DeleteAfterDays,
            create: CharacterCreateOptions.FromEnvironment());
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

        // Start Net TCP before HTTP so PacketEnvelope login works even if Kestrel hangs.
        var netLogin = new NetLoginServer(service, zoneHost: ZoneHandoff.AdvertiseHost, zonePort: NetSettings.ZonePort);
        var netTcp = new NetTcpServer { Endpoint = "login" };
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
            http = await LoginHttpHost.StartAsync(
                service,
                bindAddress,
                httpPort,
                zoneStatus: zoneStatus).ConfigureAwait(false);
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
