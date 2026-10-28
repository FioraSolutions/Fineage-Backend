using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Zone;

if (args.Contains("--selftest"))
{
    try
    {
        await ZoneAcceptance.RunAsync();
        await ZoneMultiplayerAcceptance.RunAsync();
        ZoneAcceptance.Write(Console.Out);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("selftest FAIL " + ex.Message);
        Environment.ExitCode = 1;
    }

    return;
}

string portText = Arg(args, "--port")
    ?? Environment.GetEnvironmentVariable("FINEAGE_ZONE_PORT")
    ?? ZoneHandoff.Port.ToString();
if (!int.TryParse(portText, out int port) || port is < 1 or > 65535)
{
    Console.Error.WriteLine("invalid --port");
    Environment.ExitCode = 1;
    return;
}

string bindText = Arg(args, "--bind")
    ?? Environment.GetEnvironmentVariable("FINEAGE_BIND")
    ?? BindAddress.Default;
if (!BindAddress.TryResolve(bindText, out var bindAddress, out string bindDisplay))
{
    Console.Error.WriteLine("invalid --bind");
    Environment.ExitCode = 1;
    return;
}

var server = new ZoneServer(HandoffTokens.FromEnvironment());
var listener = new TcpListener(bindAddress, port);
try
{
    listener.Start();
}
catch (SocketException ex)
{
    Console.Error.WriteLine($"listen failed on {bindDisplay}:{port}: {ex.Message}");
    Environment.ExitCode = 1;
    return;
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_SECRET")) &&
    !IPAddress.IsLoopback(bindAddress))
{
    Console.WriteLine("[ZONE] warning: FINEAGE_HANDOFF_SECRET is unset. It must match Login before clients hand off from another machine.");
}

Console.WriteLine($"[ZONE] listen {bindDisplay}:{port} Talking Island");
Console.WriteLine("[ZONE] Auth.Password is the login handoff token. EnterWorld spawns the village, shared Gremlins, and other players. Say is zone chat.");
while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        try
        {
            await using var stream = client.GetStream();
            await server.HandleAsync(stream, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ZONE] session error {ex.Message}");
        }
        finally
        {
            client.Dispose();
        }
    });
}

static string? Arg(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    if (i < 0 || i + 1 >= args.Length)
        return null;
    return args[i + 1];
}
