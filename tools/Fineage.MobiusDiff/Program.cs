using System.Diagnostics;
using Fineage.Game;
using Fineage.Zone.Npcs;

namespace Fineage.MobiusDiff;

public static class RepoPaths
{
    /// <summary>Repository root (the directory containing <c>Fineage.Backend.slnx</c>).</summary>
    public static string FindRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Fineage.Backend.slnx")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root (Fineage.Backend.slnx).");
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("mobius-diff: " + ex.Message);
            return 2;
        }
    }

    static int Run(string[] args)
    {
        string root = RepoPaths.FindRoot();
        if (args.Any(static a => a is "--ai-all" or "--ai-scene" or "--refresh-ai-oracle" or "--ai-report-dir"))
            return AiProgram.Run(root, args);

        string? scenarioPath = null;
        bool all = false;
        bool refresh = false;
        string reportDir = Path.Combine(root, "local-logs", "mobius-diff");
        string java = "java";
        string? refOverride = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scenario":
                    scenarioPath = args[++i];
                    break;
                case "--all":
                    all = true;
                    break;
                case "--refresh-oracle":
                    refresh = true;
                    break;
                case "--report-dir":
                    reportDir = args[++i];
                    break;
                case "--java":
                    java = args[++i];
                    break;
                case "--ref":
                    refOverride = args[++i];
                    break;
                case "--help" or "-h":
                    PrintHelp();
                    return 0;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'. Use --help.");
            }
        }

        if (!all && scenarioPath == null)
            throw new ArgumentException("Pass --scenario <file> or --all.");

        var scenarios = new List<Scenario>();
        if (all)
        {
            string dir = Path.Combine(root, "tools", "Fineage.MobiusDiff", "scenarios");
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(static f => f, StringComparer.Ordinal))
                scenarios.Add(ScenarioJson.Load(file));
        }
        else
        {
            string path = Path.IsPathRooted(scenarioPath!) ? scenarioPath! : Path.GetFullPath(scenarioPath!, root);
            scenarios.Add(ScenarioJson.Load(path));
        }

        var data = GameData.LoadDefault();
        var npcs = NpcCatalog.LoadDefault();
        bool allPass = true;
        foreach (var scenario in scenarios)
        {
            if (refresh)
                RefreshOracle(root, scenario, java, refOverride);

            string goldenPath = Path.Combine(root, scenario.Oracle.Golden);
            if (!File.Exists(goldenPath))
                throw new FileNotFoundException(
                    $"Oracle golden missing for {scenario.Id}: {goldenPath}. Run --refresh-oracle with a readable Mobius ref tree.",
                    goldenPath);
            var oracle = ScenarioJson.LoadOracle(goldenPath);
            var report = ScenarioRunner.Run(scenario, oracle, data, npcs);
            DiffReportWriter.Write(report, reportDir);
            allPass &= report.Pass;
            Console.WriteLine(
                $"{scenario.Id}: {(report.Pass ? "PASS" : "FAIL")} ({report.Passed} passed, {report.Failed} failed) -> {Path.Combine(reportDir, scenario.Id + ".md")}");
            foreach (var row in report.Rows.Where(static r => !r.Pass))
                Console.WriteLine(
                    $"  FAIL {row.Parameter}: expected {row.Expected.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}, " +
                    $"actual {row.Actual.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}, " +
                    $"tolerance {row.Tolerance.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        return allPass ? 0 : 1;
    }

    static void RefreshOracle(string root, Scenario scenario, string java, string? refOverride)
    {
        string refRoot = refOverride ??
            Path.Combine(root, scenario.Oracle.Reference);
        if (!Directory.Exists(refRoot))
            throw new DirectoryNotFoundException(
                $"Mobius reference tree not found: {refRoot}. Set --ref or place ref/mobius-ct0-interlude in this checkout.");

        string driver = Path.Combine(root, "tools", "Fineage.MobiusDiff", "java", "MobiusFormulaDriver.java");
        if (!File.Exists(driver))
            throw new FileNotFoundException($"Oracle driver not found: {driver}", driver);

        string golden = Path.Combine(root, scenario.Oracle.Golden);
        Directory.CreateDirectory(Path.GetDirectoryName(golden)!);

        var psi = new ProcessStartInfo(java)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(driver);
        Add(psi, "--ref", refRoot);
        Add(psi, "--reference-label", scenario.Oracle.Reference);
        Add(psi, "--scenario", scenario.Id);
        Add(psi, "--class-id", scenario.Attacker.ClassId.ToString());
        Add(psi, "--level", scenario.Attacker.Level.ToString());
        Add(psi, "--npc", scenario.Target.NpcId.ToString());
        Add(psi, "--gear", string.Join(",", scenario.Attacker.Gear.Select(static g => $"{g.Slot}:{g.ItemId}:{g.Enchant}")));
        Add(psi, "--buffs", string.Join(",", scenario.Attacker.Buffs.Select(static b => $"{b.SkillId}:{b.Level}")));
        Add(psi, "--side", scenario.Contact.Side);
        Add(psi, "--soulshot", scenario.Contact.Soulshot.ToString().ToLowerInvariant());
        Add(psi, "--out", golden);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start Java ({java}).");
        string stderr = process.StandardError.ReadToEnd();
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Mobius oracle driver failed (exit {process.ExitCode}). " +
                $"Blocker: full Mobius dist/libs may be absent; the formula driver only needs a JDK. stderr: {stderr.Trim()} stdout: {stdout.Trim()}");
        Console.WriteLine($"refreshed oracle {scenario.Id} -> {golden}");
    }

    static void Add(ProcessStartInfo psi, string key, string value)
    {
        psi.ArgumentList.Add(key);
        psi.ArgumentList.Add(value);
    }

    static void PrintHelp() =>
        Console.WriteLine(
            """
            Fineage Mobius differential harness

              --all                     run every tools/Fineage.MobiusDiff/scenarios/*.json
              --scenario <file>         run one scenario
              --report-dir <dir>        report output (default local-logs/mobius-diff)
              --refresh-oracle          regenerate the golden with the Java formula driver
              --ref <dir>               Mobius ref tree (default <root>/ref/mobius-ct0-interlude)
              --java <exe>              Java executable (default: java)

            AI behavioural reference scenes:
              --ai-all                  run every tools/Fineage.MobiusDiff/scenes/ai/*.json
              --ai-scene <file>         run one AI scene
              --refresh-ai-oracle       regenerate AI goldens with the independent Mobius rule model
              --ai-report-dir <dir>     AI report output (default local-logs/ai-reference-diff)
            """);
}
