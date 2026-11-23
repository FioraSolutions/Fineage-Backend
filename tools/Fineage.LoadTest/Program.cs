using Fineage.LoadTest;
using Fineage.LoadTest.Models;
using Fineage.LoadTest.Reporting;

if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
    Console.WriteLine("Fineage.LoadTest — run --scenario <yaml> [--target fineage|mobius] [--report-dir dir] [--login-log path] [--zone-log path] [--zone-pid N] [--region-workers N]");
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

var scenario = ScenarioLoader.Load(scenarioPath);
if (string.IsNullOrWhiteSpace(scenario.Name))
    scenario.Name = Path.GetFileNameWithoutExtension(scenarioPath);
string? layoutOverride = Arg(args, "--spawn-layout");
if (!string.IsNullOrWhiteSpace(layoutOverride))
    scenario.SpawnLayout = layoutOverride;

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
    report = await runner.RunAsync(scenario, target, regionWorkers, loginLog, zoneLog, zonePid, cts.Token);
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
Console.WriteLine($"Report: {mdPath} ({(report.Passed ? "PASS" : "FAIL")})");
Environment.ExitCode = SimulationHangGate.ExitCode(reportWritten: true, passed: report.Passed);

static string? Arg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name)
            return args[i + 1];
    }

    return null;
}