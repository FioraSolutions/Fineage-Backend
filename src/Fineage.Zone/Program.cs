using System.Net;
using Fineage.Game;
using Fineage.Game.Mobius;
using Fineage.Accounts;
using Fineage.Net.Host;
using Fineage.Protocol;
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

// Accounts are PostgreSQL-only. --state-dir (default data/) holds the world sidecar files:
// seven-signs.json, olympiad.json, boss-schedule.json.
string stateDir = Arg(args, "--state-dir")
    ?? Environment.GetEnvironmentVariable("FINEAGE_STATE_DIR")
    ?? Path.Combine("data");
IAccountStore accounts;
try
{
    accounts = AccountStoreFactory.Open(Arg(args, "--database"));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ZONE] {ex.Message}");
    Environment.ExitCode = 1;
    return;
}
string storeLabel = "postgres";
TryMobiusCatalogImportAtBoot(stateDir);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

var sevenSigns = SevenSignsEngine.OpenInStateDirectory(stateDir);
var bosses = BossSchedule.OpenInStateDirectory(stateDir);
var olympiad = OlympiadBoard.OpenInStateDirectory(stateDir, DateTimeOffset.UtcNow);
// Hosted zone always loads every enabled spawn map (TI, Elven Village, Giran, …).
// Opt out with FINEAGE_ZONE_INCLUDE_ENABLED_MAPS=0, or narrow with FINEAGE_ZONE_MAPS.
bool includeEnabledMaps = !ZoneMapFilter.IsExplicitlyOff(
    Environment.GetEnvironmentVariable("FINEAGE_ZONE_INCLUDE_ENABLED_MAPS"));
var world = ZoneAoiSettings.CreateWorld(accounts: accounts, sevenSigns: sevenSigns, includeEnabledMaps: includeEnabledMaps, bosses: bosses, olympiad: olympiad);
world.StartSimulation(shutdown.Token);
FineageTelemetry.OnlineCount = () => world.OnlinePlayerCount;
string host = bindAddress.Equals(IPAddress.Any) ? "0.0.0.0" : bindDisplay;
int maxConnections = ZoneNetSettings.MaxConnections;
int outboundQueue = ZoneNetSettings.OutboundQueue;
int maxMsgPerSec = ZoneNetSettings.MaxMessagesPerSecond;
bool adminCommands = ZoneAdmin.Enabled;
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_SECRET")) &&
    !IPAddress.IsLoopback(bindAddress))
{
    Console.WriteLine("[ZONE] warning: FINEAGE_HANDOFF_SECRET is unset. It must match Login before clients hand off from another machine.");
}

string worldLabel = includeEnabledMaps ? "whole world (all enabled spawn maps)" : "single spawn map";
Console.WriteLine($"[ZONE] world {storeLabel} {worldLabel} (maxConn={maxConnections} outboundQ={outboundQueue} aoi={ZoneAoiSettings.DescribeEffective()} npcs={world.NpcObjectIds.Count})");
Console.WriteLine("[ZONE] Seven Signs period, cabal, and seal-stone scores are stored as seven-signs.json in the state directory.");
Console.WriteLine("[ZONE] Olympiad registration and points are stored as olympiad.json in the state directory.");
Console.WriteLine("[ZONE] Catacomb and necropolis gates follow that cabal.");
Console.WriteLine("[ZONE] Raid and grand boss respawn state is stored as boss-schedule.json in the state directory.");
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

static void TryMobiusCatalogImportAtBoot(string stateDirectory)
{
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MobiusXmlRoot.EnvVar)) &&
        !IsTruthy(Environment.GetEnvironmentVariable("FINEAGE_MOBIUS_IMPORT_ON_BOOT")))
        return;

    var dataDir = GameData.FindDataDirectory(stateDirectory)
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
