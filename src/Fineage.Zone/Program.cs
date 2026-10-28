using System.Net;
using Fineage.Login;
using Fineage.Protocol.Net;
using Fineage.Protocol.Telemetry;
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

string storePath = Arg(args, "--store")
    ?? Environment.GetEnvironmentVariable("FINEAGE_LOGIN_STORE")
    ?? Path.Combine("data", "accounts.json");
var accounts = JsonAccountStore.Open(storePath);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

var world = ZoneAoiSettings.CreateWorld(accounts: accounts);
world.StartSimulation(shutdown.Token);
FineageTelemetry.OnlineCount = () => world.OnlinePlayerCount;
var server = new ZoneServer(HandoffTokens.FromEnvironment(), world);
string host = bindAddress.Equals(IPAddress.Any) ? "0.0.0.0" : bindDisplay;
int maxConnections = ZoneNetSettings.MaxConnections;
int outboundQueue = ZoneNetSettings.OutboundQueue;
int maxMsgPerSec = ZoneNetSettings.MaxMessagesPerSecond;
int interestRange = ZoneAoiSettings.InterestRange;

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_SECRET")) &&
    !IPAddress.IsLoopback(bindAddress))
{
    Console.WriteLine("[ZONE] warning: FINEAGE_HANDOFF_SECRET is unset. It must match Login before clients hand off from another machine.");
}

Console.WriteLine($"[ZONE] listen {bindDisplay}:{port} store={storePath} Talking Island (maxConn={maxConnections} outboundQ={outboundQueue} msgPerSec={maxMsgPerSec} interestRange={interestRange})");
Console.WriteLine("[ZONE] Auth.Password is the login handoff token. EnterWorld sends UserInfo, ItemList, then the village. Say is zone chat.");

var tcp = new TcpServer();
try
{
    await tcp.RunAsync(
        host,
        port,
        maxConnections,
        outboundQueue,
        "zone",
        server.HandleAsync,
        shutdown.Token,
        ZoneOverload.RejectAsync).ConfigureAwait(false);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Console.Error.WriteLine($"listen failed on {bindDisplay}:{port}: {ex.Message}");
    Environment.ExitCode = 1;
}

static string? Arg(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    if (i < 0 || i + 1 >= args.Length)
        return null;
    return args[i + 1];
}
