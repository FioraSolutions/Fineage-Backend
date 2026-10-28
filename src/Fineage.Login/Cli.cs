using System.Net;
using System.Net.Sockets;

namespace Fineage.Login;

public static class Cli
{
    public static async Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? error = null)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        var rest = new List<string>(args);
        string storePath = TakeOption(rest, "--store")
            ?? Environment.GetEnvironmentVariable("FINEAGE_LOGIN_STORE")
            ?? Path.Combine("data", "accounts.json");
        string portText = TakeOption(rest, "--port")
            ?? Environment.GetEnvironmentVariable("FINEAGE_LOGIN_PORT")
            ?? "9106";
        if (!int.TryParse(portText, out int port) || port is < 1 or > 65535)
        {
            error.WriteLine("invalid --port");
            return 1;
        }

        if (rest.Count == 0)
        {
            try
            {
                await ListenAsync(storePath, port, output, error).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                return 1;
            }

            return 0;
        }

        var tokens = HandoffTokens.FromEnvironment();
        var service = new LoginService(JsonAccountStore.Open(storePath), tokens);
        string cmd = rest[0].ToLowerInvariant();
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
              Fineage.Login [--port 9106] [--store data/accounts.json]
            """);
        return 1;
    }

    static async Task ListenAsync(string storePath, int port, TextWriter output, TextWriter error)
    {
        var service = new LoginService(JsonAccountStore.Open(storePath), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            error.WriteLine($"listen failed on 127.0.0.1:{port}: {ex.Message}");
            throw;
        }

        output.WriteLine($"[LOGIN] listen 127.0.0.1:{port} store={storePath}");
        output.WriteLine($"[LOGIN] zone handoff target {ZoneHandoff.Host}:{ZoneHandoff.Port}");
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var stream = client.GetStream();
                    await server.HandleAsync(stream, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[LOGIN] session error {ex.Message}");
                }
                finally
                {
                    client.Dispose();
                }
            });
        }
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
