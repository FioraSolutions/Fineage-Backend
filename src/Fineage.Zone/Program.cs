using System.Net;
using Fineage.Game;
using Fineage.Game.Mobius;
using Fineage.Game.RequiredData;
using Fineage.Accounts;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Protocol.Health;
using Fineage.Protocol.Telemetry;
using Fineage.Zone;
using Fineage.Zone.Bosses;
using Fineage.Zone.Net;
using Fineage.Zone.Olympiad;
using Fineage.Zone.Punishment;
using Fineage.Zone.Geo;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.World;
using Fineage.Zone.Persistence;

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

// Accounts and world subsystem state are PostgreSQL-only (#996: world_state, written with the economy
// commit). --state-dir (default data/) keeps gm-audit.jsonl and is where the one-time cutover looks for the
// pre-#996 JSON sidecars to import and archive (docs/architecture/world-state-storage.md).
string stateDir = Arg(args, "--state-dir")
    ?? Environment.GetEnvironmentVariable("FINEAGE_STATE_DIR")
    ?? Path.Combine("data");
PostgresAccountStore accounts;
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
// A service manager or container stops the zone with SIGTERM. Without this the runtime exits on it straight away:
// no shutdown save, no final economy commit, no shutdown reconcile (#955).
using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
    System.Runtime.InteropServices.PosixSignal.SIGTERM,
    context =>
    {
        context.Cancel = true;
        shutdown.Cancel();
    });

var time = Fineage.Zone.Time.SystemZoneTime.Instance;
bool includeEnabledMapsBoot = !ZoneMapFilter.IsExplicitlyOff(
    Environment.GetEnvironmentVariable("FINEAGE_ZONE_INCLUDE_ENABLED_MAPS"));
// Hosted zone always loads every enabled spawn map (TI, Elven Village, Giran, …).
// Opt out with FINEAGE_ZONE_INCLUDE_ENABLED_MAPS=0, or narrow with FINEAGE_ZONE_MAPS.
bool includeEnabledMaps = includeEnabledMapsBoot;
SevenSignsEngine sevenSigns;
SevenSignsFestivalRuntime festivalRuntime;
BossSchedule bosses;
OlympiadBoard olympiad;
PunishmentManager punishments;
Fineage.Zone.RaidBossPoints.RaidBossPointsManager raidBossPoints;
WorldStateStore worldState;
try
{
    // #996: the world subsystems live in the database. Sidecars a pre-#996 zone left in the state directory are
    // imported once and archived here, before anything loads; bad data or a second source of truth stops the start.
    foreach (var cutover in WorldStateCutover.Run(accounts, stateDir, () => time.UtcNow))
    {
        if (cutover.Action != WorldStateCutoverAction.None)
            Console.WriteLine($"[ZONE] world state '{cutover.System}' {cutover.Action.ToString().ToLowerInvariant()}: {cutover.SourcePath} -> {cutover.ArchivedPath}");
    }

    worldState = WorldStateStore.Open(accounts);
    var festivalCatalog = SevenSignsFestivalCatalog.LoadDefault();
    festivalRuntime = new SevenSignsFestivalRuntime(festivalCatalog);
    sevenSigns = SevenSignsEngine.Open(worldState, time.UtcNow, festivalRuntime);
    festivalRuntime.BindEngine(sevenSigns);
    bosses = BossSchedule.Open(
        worldState,
        time.UtcNow,
        new MapFilteredBossSpawnPolicy(includeEnabledMapsBoot));
    olympiad = OlympiadBoard.Open(worldState, time.UtcNow);
    punishments = PunishmentManager.Open(worldState, stateDir, time);
    raidBossPoints = Fineage.Zone.RaidBossPoints.RaidBossPointsManager.Open(worldState);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
{
    Console.Error.WriteLine($"[ZONE] {ex.Message}");
    Environment.ExitCode = 1;
    return;
}

// #989: the loopback status host starts before anything loads, so /health/ready answers 503 with the stage that is
// still pending (or failed) instead of refusing the connection; /health/live is the process + simulation heartbeat.
var health = new HealthBoard();
health.Require("world", "loading catalogs, boot validation and world state");
health.Require("state-audit", "waiting for the world");
health.Require("economy-reconcile", "waiting for the state audit");
ZoneWorld? liveWorld = null;
TimeSpan liveLag = HealthSettings.LiveTickLag();
TimeSpan readyLag = HealthSettings.ReadyTickLag();
health.AddLiveProbe("simulation-heartbeat", () => liveWorld is { SimulationStarted: true } running
    ? running.SimulationHealth(liveLag)
    : new HealthCheck("simulation-heartbeat", true, "booting: simulation not started yet"));
health.AddReadyProbe("simulation", () => liveWorld?.SimulationHealth(readyLag)
    ?? new HealthCheck("simulation", false, "world not built"));
var databaseProbe = new CachedProbe(() => accounts.CheckDatabase(TimeSpan.FromSeconds(3)), TimeSpan.FromSeconds(2));
health.AddReadyProbe("database", databaseProbe.Read);
// #992: /metrics exports the health contract and the state directory's free space.
FineageTelemetry.Health = health;
FineageTelemetry.RegisterDiskPath("state", stateDir);

int statusPort = ZoneStatus.ResolvePort(Environment.GetEnvironmentVariable(ZoneStatus.PortVariable), out string? statusPortError);
if (statusPortError != null)
    Console.Error.WriteLine($"[ZONE] ignoring {ZoneStatus.PortVariable}: {statusPortError}");
else if (statusPort <= 0)
    Console.WriteLine("[ZONE] status disabled (no /health/live or /health/ready either)");

await using ZoneStatusHost? zoneStatus = statusPort > 0
    ? ZoneStatusHost.TryStart(
        statusPort,
        () => liveWorld?.OnlinePlayerCount ?? throw new InvalidOperationException("world not built"),
        Console.Error,
        health)
    : null;
if (zoneStatus != null)
    Console.WriteLine($"[ZONE] status {zoneStatus.Url} health {zoneStatus.BaseUrl}/health/live {zoneStatus.BaseUrl}/health/ready");

ZoneWorld world;
Required.SilentFallbackObserver = static (kind, key) =>
    FineageTelemetry.DataSilentFallback.Add(1, new KeyValuePair<string, object?>("kind", kind.ToId()));
try
{
    string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
    Required.Registry.AssertBootPacksPresent(dataDir);
    Fineage.Zone.RequiredData.RequiredDataCrossCheck.AssertClean(dataDir, Required.Registry);
    _ = GeoEngine.LoadDefault();
    world = ZoneAoiSettings.CreateWorld(accounts: accounts, sevenSigns: sevenSigns, festivalRuntime: festivalRuntime, includeEnabledMaps: includeEnabledMaps, bosses: bosses, olympiad: olympiad,
        ledgerJournal: accounts.OpenLedgerJournal(), punishments: punishments, raidBossPoints: raidBossPoints, time: time);
    long loadedAtUnixMs = time.UtcNow.ToUnixTimeMilliseconds();
    world.UseWorldState(worldState);
    world.UseLottery(Fineage.Zone.Lottery.LotteryBoard.Open(worldState, loadedAtUnixMs));
    world.UseFishingChampionship(Fineage.Zone.Fishing.FishingChampionshipBoard.Open(worldState, loadedAtUnixMs));
    world.UseStoredMonsterRace(worldState, loadedAtUnixMs);
    world.UsePcCafe(new Fineage.Zone.PcCafe.PcCafePointsManager(new StoredPcCafeStore(worldState)));
    liveWorld = world;
    health.Complete("world", $"catalogs, boot validation and world state loaded (npcs={world.NpcObjectIds.Count})");
    // #984: saved state vs data catalogs vs replayed ledger, before any player can enter.
    var audit = world.AuditStoredStateAtStartup();
    health.Complete("state-audit", $"characters={audit.Characters} errors={audit.Errors.Count} notices={audit.Issues.Count - audit.Errors.Count}");
    // #955: ledger vs store vs the booted world (ground expired, nobody online) before any player can enter.
    var reconcile = world.ReconcileEconomyAndLog("startup");
    health.Complete("economy-reconcile", reconcile.Summary);
}
catch (Exception ex) when (ex is InvalidOperationException or RequiredDataException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"[ZONE] {ex.Message}");
    Environment.ExitCode = 1;
    return;
}
world.StartSimulation(shutdown.Token);
FineageTelemetry.OnlineCount = () => world.OnlinePlayerCount;
FineageTelemetry.CommandIngressDepth = () => world.CommandIngressQueuedDepth;
FineageTelemetry.BeforeMetricsScrape = world.PublishLedgerInvariantsForMetrics;
FineageTelemetry.ActivationBacklog = () => world.ActivationBacklog;
string host = bindAddress.Equals(IPAddress.Any) ? "0.0.0.0" : bindDisplay;
int maxConnections = ZoneNetSettings.MaxConnections;
int outboundQueue = ZoneNetSettings.OutboundQueue;
int maxMsgPerSec = ZoneNetSettings.MaxMessagesPerSecond;
bool adminCommands = ZoneAdmin.Enabled;
MemoryHandoffStore handoffStore;
try
{
    handoffStore = MemoryHandoffStore.FromEnvironment(
        allowDefaultSecret: HandoffTokens.AllowDefaultSecretForBind(bindAddress));
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[ZONE] {ex.Message}");
    Environment.ExitCode = 1;
    return;
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_SECRET")) &&
    IPAddress.IsLoopback(bindAddress))
{
    Console.WriteLine("[ZONE] FINEAGE_HANDOFF_SECRET unset; using DefaultSecret (loopback only).");
}

string worldLabel = includeEnabledMaps ? "whole world (all enabled spawn maps)" : "single spawn map";
Console.WriteLine($"[ZONE] world {storeLabel} {worldLabel} (maxConn={maxConnections} outboundQ={outboundQueue} aoi={ZoneAoiSettings.DescribeEffective()} npcs={world.NpcObjectIds.Count})");
Console.WriteLine($"[ZONE] {ZoneWorld.DescribeTrainerSpLearnMode(world.TrainerSpLearn)}");
Console.WriteLine($"[ZONE] simulation seed={world.SimulationSeed} step={ZoneSimulationSettings.StepMilliseconds}ms regions={ZoneSimulationSettings.DescribeRegionExecutor(ZoneSimulationSettings.RegionWorkers)} (FINEAGE_ZONE_SEED replays the random streams; FINEAGE_ZONE_REGION_WORKERS spreads regions over threads)");
Console.WriteLine("[ZONE] Seven Signs period, cabal, and seal-stone scores are stored as seven-signs.json in the state directory.");
Console.WriteLine("[ZONE] Olympiad registration and points are stored as olympiad.json in the state directory.");
Console.WriteLine("[ZONE] Catacomb and necropolis gates follow that cabal.");
Console.WriteLine("[ZONE] Raid and grand boss respawn state is stored as boss-schedule.json in the state directory.");
Console.WriteLine("[ZONE] Raid boss points ranking totals are stored as raid-boss-points-store.json in the state directory.");
Console.WriteLine("[ZONE] GM punishments are stored in database world_state 'punishment'; audit lines append to gm-audit.jsonl in the state directory.");
if (adminCommands)
    Console.WriteLine("[ZONE] admin commands enabled (GiveItem, AdminCaptureCastle, AdminBossSchedule, AdminReportOlympiadResult, AdminKillPlayer).");

await using FineageMetricsHost? metrics = FineageMetricsHost.Start("zone", Console.Out, Console.Error);
var netZone = new NetZoneServer(handoffStore, world, accounts);
var netTcp = new NetTcpServer { Endpoint = "zone" };
Console.WriteLine($"[ZONE] net listen {bindDisplay}:{NetSettings.ZonePort} (PacketEnvelope)");
Task netTask = netTcp.RunAsync(
    host,
    NetSettings.ZonePort,
    netZone.HandleAsync,
    shutdown.Token,
    maxConnections,
    NetSettings.InboxCapacity,
    outboundQueue);

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
    try
    {
        await world.WaitSimulationAsync().ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.Error.WriteLine($"[ZONE] simulation shutdown: {ex.Message}");
    }

    try
    {
        world.ShutdownGracefully();
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.Error.WriteLine($"[ZONE] shutdown save: {ex.Message}");
        Environment.ExitCode = 1;
    }
    finally
    {
        world.FlushLedgerOnShutdown();
    }

    try
    {
        // #955: every player saved and every economy batch committed: the store must equal the ledger now.
        if (!world.ReconcileEconomyAndLog("shutdown", _ => "log").Ok)
            Environment.ExitCode = 1;
    }
    catch (Exception ex) when (ex is InvalidOperationException)
    {
        Console.Error.WriteLine($"[ZONE] shutdown economy reconcile: {ex.Message}");
        Environment.ExitCode = 1;
    }
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
