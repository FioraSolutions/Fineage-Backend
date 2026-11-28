using Fineage.LoadTest;
using Fineage.LoadTest.Models;
using Fineage.LoadTest.Reporting;

if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
    Console.WriteLine("Fineage.LoadTest — run --scenario <yaml> [--target fineage|mobius] [--report-dir dir] [--login-log path] [--zone-log path] [--zone-pid N] [--zone-dll path] [--expect-zone-commit sha] [--require-clean-provenance] [--require-gates] [--region-workers N] [--consistency-trace] [--duration-seconds N] [--ramp-up-seconds N] [--bot-count N]");
    return;
}

string? scenarioPath = Arg(args, "--scenario");
if (scenarioPath == null)
{
    Console.Error.WriteLine("missing --scenario");
    Environment.ExitCode = 1;
    return;
}

string target = Arg(args, "--target") ?? "fineage";
string reportDir = Arg(args, "--report-dir") ?? Path.Combine("local-logs", "loadtest", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
string? loginLog = Arg(args, "--login-log");
string? zoneLog = Arg(args, "--zone-log");
int regionWorkers = int.TryParse(Arg(args, "--region-workers"), out int rw) ? rw : 0;
int? zonePid = int.TryParse(Arg(args, "--zone-pid"), out int pid) ? pid : null;
string? zoneDll = Arg(args, "--zone-dll") ?? Environment.GetEnvironmentVariable("FINEAGE_ZONE_DLL");
string? expectedZoneCommit = Arg(args, "--expect-zone-commit")
                            ?? Environment.GetEnvironmentVariable("FINEAGE_EXPECT_ZONE_COMMIT");
bool requireCleanProvenance = args.Contains("--require-clean-provenance");
bool requireGates = args.Contains("--require-gates");

var scenario = ScenarioLoader.Load(scenarioPath);
if (string.IsNullOrWhiteSpace(scenario.Name))
    scenario.Name = Path.GetFileNameWithoutExtension(scenarioPath);
string? prefixOverride = Environment.GetEnvironmentVariable("FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX");
if (!string.IsNullOrWhiteSpace(prefixOverride))
    scenario.AccountPrefix = prefixOverride.Trim();
string? passwordOverride = Environment.GetEnvironmentVariable("FINEAGE_LOGIN_LOADTEST_PASSWORD");
if (!string.IsNullOrWhiteSpace(passwordOverride))
    scenario.Password = passwordOverride;
string? layoutOverride = Arg(args, "--spawn-layout");
if (!string.IsNullOrWhiteSpace(layoutOverride))
    scenario.SpawnLayout = layoutOverride;
if (args.Contains("--consistency-trace"))
    scenario.ConsistencyTrace.Enabled = true;
if (int.TryParse(Arg(args, "--duration-seconds"), out int durationSeconds))
{
    if (durationSeconds < 1)
    {
        Console.Error.WriteLine("--duration-seconds must be >= 1");
        Environment.ExitCode = 1;
        return;
    }

    scenario.DurationSeconds = durationSeconds;
}

if (int.TryParse(Arg(args, "--ramp-up-seconds"), out int rampUpSeconds))
{
    if (rampUpSeconds < 0)
    {
        Console.Error.WriteLine("--ramp-up-seconds must be >= 0");
        Environment.ExitCode = 1;
        return;
    }

    scenario.RampUpSeconds = rampUpSeconds;
}

if (int.TryParse(Arg(args, "--bot-count"), out int botCount))
{
    if (botCount < 1)
    {
        Console.Error.WriteLine("--bot-count must be >= 1");
        Environment.ExitCode = 1;
        return;
    }

    scenario.BotCount = botCount;
}

ScenarioLoader.Validate(scenario);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var runner = new LoadTestRunner();
RunReport report;
try
{
    report = await runner.RunAsync(scenario, target, regionWorkers, loginLog, zoneLog, zonePid, cts.Token,
        zoneDll, expectedZoneCommit, requireCleanProvenance);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    report = SimulationHangGate.RunnerFault(
        scenario, target, regionWorkers, ex, SimulationHangGate.FromLogFile(zoneLog));
}

string mdPath = Path.Combine(reportDir, "load-test-report.md");
try
{
    ReportWriter.Write(report, reportDir);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: load test runner wrote no report ({ex.Message})");
    Environment.ExitCode = SimulationHangGate.ExitCode(reportWritten: false, passed: false);
    return;
}

if (!File.Exists(mdPath))
{
    Console.Error.WriteLine("FAIL: load test runner wrote no report");
    Environment.ExitCode = SimulationHangGate.ExitCode(reportWritten: false, passed: false);
    return;
}

Console.WriteLine(
    $"simulation stall: {report.SimulationStallSeconds:0.0}s count={report.SimulationStallCount} limit={SimulationHangGate.StallLimit.TotalSeconds:0}s");
if (report.FailReasons.FirstOrDefault(reason => reason.StartsWith("Simulation hang:", StringComparison.Ordinal)) is { } hangReason)
    Console.WriteLine(hangReason);
bool gatesPassed = report.GateVerdicts.Count > 0 && report.GateVerdicts.All(gate => gate.Passed);
bool passed = report.Passed && (!requireGates || gatesPassed);
Console.WriteLine($"Report: {mdPath} ({(passed ? "PASS" : "FAIL")})");
if (requireGates && !gatesPassed)
    Console.WriteLine($"gate failures: {report.GateVerdicts.Count(gate => !gate.Passed)}");
Environment.ExitCode = SimulationHangGate.ExitCode(reportWritten: true, passed: passed);

static string? Arg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name)
            return args[i + 1];
    }

    return null;
}