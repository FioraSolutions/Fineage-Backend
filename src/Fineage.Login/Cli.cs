using System.Net;
using System.Net.Sockets;
using Fineage.Login.Net;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Fineage.Accounts;

namespace Fineage.Login;

public static class Cli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter? output = null,
        TextWriter? error = null,
        IAccountStore? store = null)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        var rest = new List<string>(args);
        string? databaseUrl = TakeOption(rest, "--database")
            ?? Environment.GetEnvironmentVariable(AccountStoreFactory.DatabaseUrlEnv);
        string? httpPortText = TakeOption(rest, "--http-port")
            ?? Environment.GetEnvironmentVariable("FINEAGE_LOGIN_HTTP_PORT")
            ?? "5080";
        if (!int.TryParse(httpPortText, out int httpPort) || httpPort is < 1 or > 65535)
        {
            error.WriteLine("invalid --http-port");
            return 1;
        }

        string bindText = TakeOption(rest, "--bind")
            ?? Environment.GetEnvironmentVariable("FINEAGE_BIND")
            ?? BindAddress.Default;
        if (!BindAddress.TryResolve(bindText, out var bindAddress, out string bindDisplay))
        {
            error.WriteLine("invalid --bind");
            return 1;
        }

        if (rest.Count == 0)
            return await ListenAsync(store, databaseUrl, bindAddress, bindDisplay, httpPort, output, error).ConfigureAwait(false) ? 0 : 1;

        string cmd = rest[0].ToLowerInvariant();
        if (cmd is not ("register" or "login" or "create" or "select" or "play" or "backfill-normalized"))
            return Usage(error);

        if (cmd is "backfill-normalized")
        {
            if (rest.Count != 1)
                return Usage(error);
            try
            {
                using var opened = store == null ? AccountStoreFactory.Open(databaseUrl) : null;
                (opened ?? (PostgresAccountStore)store!).BackfillNormalizedFromSnapshot();
                output.WriteLine("backfilled normalized character/item/quest tables from store_snapshot");
                return 0;
            }
            catch (Exception ex)
            {
                error.WriteLine("backfill-normalized failed: " + ex.Message);
                return 1;
            }
        }

        IAccountStore accounts;
        try
        {
            accounts = store ?? AccountStoreFactory.Open(databaseUrl);
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }

        var handoff = MemoryHandoffStore.FromEnvironment();
        var service = new LoginService(
            accounts,
            handoff.Tokens,
            deleteAfterDays: CharacterRules.DeleteAfterDays,
            create: CharacterCreateOptions.FromEnvironment());
        switch (cmd)
        {
            case "register":
                return rest.Count == 3
                    ? PrintSession(output, error, service.Register(rest[1], rest[2]), "registered")
                    : Usage(error);
            case "login":
                return rest.Count == 3
                    ? PrintSession(output, error, service.Login(rest[1], rest[2]), "login ok")
                    : Usage(error);
            case "create":
                return rest.Count == 5
                    ? Create(output, error, service, rest[1], rest[2], rest[3], rest[4])
                    : Usage(error);
            case "select":
                return rest.Count == 4
                    ? Select(output, error, service, rest[1], rest[2], rest[3])
                    : Usage(error);
            case "play":
                return rest.Count == 5
                    ? Play(output, error, service, rest[1], rest[2], rest[3], rest[4])
                    : Usage(error);
            default:
                return Usage(error);
        }
    }

    static int Create(TextWriter output, TextWriter error, LoginService service, string account, string password, string name, string archetype)
    {
        var session = service.AccountExists(account) ? service.Login(account, password) : service.Register(account, password);
        if (!session.Ok || session.Value == null)
            return Fail(error, session.Code, session.Message);
        if (!HumanClasses.TryParseArchetype(archetype, out int classId))
            return Fail(error, "class_not_supported", "Choose fighter or mystic.");
        var created = service.CreateHuman(session.Value, name, classId);
        if (!created.Ok || created.Value == null)
            return Fail(error, created.Code, created.Message);
        output.WriteLine($"created account={created.Value.Account} character={created.Value.Name} id={created.Value.Id} classId={created.Value.ClassId}");
        return 0;
    }

    static int Select(TextWriter output, TextWriter error, LoginService service, string account, string password, string name)
    {
        var session = service.Login(account, password);
        if (!session.Ok || session.Value == null)
            return Fail(error, session.Code, session.Message);
        var ticket = service.Select(session.Value, 0, name);
        if (!ticket.Ok || ticket.Value == null)
            return Fail(error, ticket.Code, ticket.Message);
        PrintHandoff(output, ticket.Value);
        return 0;
    }

    static int Play(TextWriter output, TextWriter error, LoginService service, string account, string password, string name, string archetype)
    {
        if (!HumanClasses.TryParseArchetype(archetype, out int classId))
            return Fail(error, "class_not_supported", "Choose fighter or mystic.");
        var ticket = service.Play(account, password, name, classId);
        if (!ticket.Ok || ticket.Value == null)
            return Fail(error, ticket.Code, ticket.Message);
        PrintHandoff(output, ticket.Value);
        return 0;
    }

    static int PrintSession(TextWriter output, TextWriter error, LoginResult<string> result, string label)
    {
        if (!result.Ok || result.Value == null)
            return Fail(error, result.Code, result.Message);
        output.WriteLine($"{label} account={result.Value}");
        return 0;
    }

    static void PrintHandoff(TextWriter output, HandoffTicket ticket) =>
        LoginAcceptance.WriteHandoff(output, ticket.Account, ticket.CharacterName, ticket.ClassId, ticket.Token,
            ticket.ZoneHost, ticket.ZonePort, ticket.ExpiresUnixSeconds);

    static string FormatIds(IReadOnlySet<int> ids)
    {
        var list = new int[ids.Count];
        int n = 0;
        foreach (int id in ids)
            list[n++] = id;
        Array.Sort(list);
        return string.Join(',', list);
    }

    static int Fail(TextWriter error, string code, string message)
    {
        error.WriteLine($"error {code}: {message}");
        return 1;
    }

    static int Usage(TextWriter error)
    {
        error.WriteLine("""
            usage:
              Fineage.Login --selftest
              Fineage.Login register <account> <password>
              Fineage.Login login <account> <password>
              Fineage.Login create <account> <password> <name> <fighter|mystic>
              Fineage.Login select <account> <password> <name>
              Fineage.Login play <account> <password> <name> <fighter|mystic>
              Fineage.Login [--bind 0.0.0.0] [--http-port 5080] [--database <conn>]
              Fineage.Login backfill-normalized           # re-project characters/items/quests from store_snapshot
              # PostgreSQL only. FINEAGE_DATABASE_URL=Host=...;Database=fineage;...
              # Net TCP login port: FINEAGE_NET_LOGIN_PORT (default 9106)
            """);
        return 1;
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

        var handoff = MemoryHandoffStore.FromEnvironment();
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
            NetSettings.InboxCapacity);

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
            !IPAddress.IsLoopback(bindAddress))
        {
            output.WriteLine("[LOGIN] warning: FINEAGE_HANDOFF_SECRET is unset. Set one shared secret on Login and Zone before exposing this bind.");
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

    static string? TakeOption(List<string> args, string name)
    {
        int i = args.IndexOf(name);
        if (i < 0 || i + 1 >= args.Count)
            return null;
        string value = args[i + 1];
        args.RemoveAt(i + 1);
        args.RemoveAt(i);
        return value;
    }
}
