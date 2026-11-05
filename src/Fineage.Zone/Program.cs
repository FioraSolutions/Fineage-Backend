using System.Net;
using Fineage.Game;
using Fineage.Game.Mobius;
using Fineage.Login;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Protocol.Telemetry;
using Fineage.Zone;
using Fineage.Zone.Bosses;
using Fineage.Zone.Net;
using Fineage.Zone.Olympiad;
using Fineage.Zone.SevenSigns;

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

// Net owns 9777 and is the only client transport; the MessagePack V2 listener is gone.
string portText = Arg(args, "--port")
    ?? Environment.GetEnvironmentVariable("FINEAGE_ZONE_PORT")
    ?? NetSettings.DefaultZonePort.ToString();
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
IAccountStore accounts = AccountStoreFactory.Open(storePath);
string storeLabel = string.Equals(
    Environment.GetEnvironmentVariable(AccountStoreFactory.StoreEnv),
    "postgres",
    StringComparison.OrdinalIgnoreCase)
    || string.Equals(
        Environment.GetEnvironmentVariable(AccountStoreFactory.StoreEnv),
        "pg",
        StringComparison.OrdinalIgnoreCase)
    ? "postgres"
    : storePath;
TryMobiusCatalogImportAtBoot(storePath);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

var sevenSigns = SevenSignsEngine.OpenBesideAccounts(storePath);
var bosses = BossSchedule.OpenBesideAccounts(storePath);
var olympiad = OlympiadBoard.OpenBesideAccounts(storePath, DateTimeOffset.UtcNow);
bool includeEnabledMaps = string.Equals(
    Environment.GetEnvironmentVariable("FINEAGE_ZONE_INCLUDE_ENABLED_MAPS"),
    "1",
    StringComparison.OrdinalIgnoreCase)
    || string.Equals(
        Environment.GetEnvironmentVariable("FINEAGE_ZONE_INCLUDE_ENABLED_MAPS"),
        "true",
        StringComparison.OrdinalIgnoreCase);
var world = ZoneAoiSettings.CreateWorld(accounts: accounts, sevenSigns: sevenSigns, includeEnabledMaps: includeEnabledMaps, bosses: bosses, olympiad: olympiad);
world.StartSimulation(shutdown.Token);
FineageTelemetry.OnlineCount = () => world.OnlinePlayerCount;
string host = bindAddress.Equals(IPAddress.Any) ? "0.0.0.0" : bindDisplay;
int maxConnections = ZoneNetSettings.MaxConnections;
int outboundQueue = ZoneNetSettings.OutboundQueue;
int maxMsgPerSec = ZoneNetSettings.MaxMessagesPerSecond;
int interestRange = ZoneAoiSettings.InterestRange;
bool adminCommands = ZoneAdmin.Enabled;
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_SECRET")) &&
    !IPAddress.IsLoopback(bindAddress))
{
    Console.WriteLine("[ZONE] warning: FINEAGE_HANDOFF_SECRET is unset. It must match Login before clients hand off from another machine.");
}

string worldLabel = includeEnabledMaps ? "whole world (all enabled spawn maps)" : "single spawn map";
Console.WriteLine($"[ZONE] world {storeLabel} {worldLabel} (maxConn={maxConnections} outboundQ={outboundQueue} interestRange={interestRange} npcs={world.NpcObjectIds.Count})");
Console.WriteLine("[ZONE] Seven Signs period, cabal, and seal-stone scores are stored beside the account file as seven-signs.json.");
Console.WriteLine("[ZONE] Olympiad registration and points are stored beside the account file as olympiad.json.");
Console.WriteLine("[ZONE] Catacomb and necropolis gates follow that cabal.");
Console.WriteLine("[ZONE] Raid and grand boss respawn state is stored beside the account file as boss-schedule.json.");
if (adminCommands)
    Console.WriteLine("[ZONE] admin commands enabled (GiveItem, AdminCaptureCastle, AdminBossSchedule, AdminReportOlympiadResult, AdminKillPlayer).");

int statusPort = ZoneStatus.ResolvePort(Environment.GetEnvironmentVariable(ZoneStatus.PortVariable), out string? statusPortError);
if (statusPortError != null)
    Console.Error.WriteLine($"[ZONE] ignoring {ZoneStatus.PortVariable}: {statusPortError}");
else if (statusPort <= 0)
    Console.WriteLine("[ZONE] status disabled");

await using ZoneStatusHost? zoneStatus = statusPort > 0
    ? ZoneStatusHost.TryStart(statusPort, () => world.OnlinePlayerCount, Console.Error)
    : null;
if (zoneStatus != null)
    Console.WriteLine($"[ZONE] status {zoneStatus.Url}");

await using FineageMetricsHost? metrics = FineageMetricsHost.Start("zone", Console.Out, Console.Error);
var netZone = new NetZoneServer(HandoffTokens.FromEnvironment(), world, accounts);
var netTcp = new NetTcpServer { Endpoint = "zone" };
Console.WriteLine($"[ZONE] net listen {bindDisplay}:{NetSettings.ZonePort} (PacketEnvelope)");
Task netTask = netTcp.RunAsync(
    host,
    NetSettings.ZonePort,
    netZone.HandleAsync,
    shutdown.Token,
    maxConnections,
    NetSettings.InboxCapacity);

try
{
    await netTask.ConfigureAwait(false);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Console.Error.WriteLine($"listen failed on {bindDisplay}:{NetSettings.ZonePort}: {ex.Message}");
    Environment.ExitCode = 1;
}
finally
{
    shutdown.Cancel();
    bosses.Save();
}

static string? Arg(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    if (i < 0 || i + 1 >= args.Length)
        return null;
    return args[i + 1];
}

static void TryMobiusCatalogImportAtBoot(string storePath)
{
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MobiusXmlRoot.EnvVar)) &&
        !IsTruthy(Environment.GetEnvironmentVariable("FINEAGE_MOBIUS_IMPORT_ON_BOOT")))
        return;

    var dataDir = GameData.FindDataDirectory(Path.GetDirectoryName(Path.GetFullPath(storePath)) ?? Directory.GetCurrentDirectory())
        ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory());
    if (dataDir == null)
    {
        Console.WriteLine("[ZONE] Mobius import skipped: could not locate data/ catalog directory.");
        return;
    }

    if (IsTruthy(Environment.GetEnvironmentVariable("FINEAGE_MOBIUS_IMPORT_ON_BOOT")))
    {
        try
        {
            var result = MobiusCatalogImporter.RunFromEnvironment(
                dataDir,
                MobiusImportTargets.Npcs | MobiusImportTargets.Spawns);
            Console.WriteLine("[ZONE] Mobius import wrote " + string.Join(", ", result.WrittenPaths));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ZONE] Mobius import failed: " + ex.Message);
        }

        return;
    }

    MobiusCatalogImporter.LogRootStatus(Console.Out);
}

static bool IsTruthy(string? text) =>
    text is "1" or "true" or "yes" or "on" ||
    string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(text, "YES", StringComparison.OrdinalIgnoreCase);
