using Fineage.Game;
using Fineage.Zone.Npcs;

namespace Fineage.MobiusDiff;

/// <summary>CLI for the behavioural AI reference set. Kept separate from the pure formula harness.</summary>
public static class AiProgram
{
    public static int Run(string root, string[] args)
    {
        bool all = false;
        bool refresh = false;
        string? scenePath = null;
        string reportDir = Path.Combine(root, "local-logs", "ai-reference-diff");
        string? referenceOverride = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ai-all":
                    all = true;
                    break;
                case "--ai-scene":
                    scenePath = args[++i];
                    break;
                case "--refresh-ai-oracle":
                    refresh = true;
                    break;
                case "--ai-report-dir":
                    reportDir = args[++i];
                    break;
                case "--ref":
                    referenceOverride = args[++i];
                    break;
                default:
                    throw new ArgumentException($"Unknown AI differential argument '{args[i]}'. Use --help.");
            }
        }

        if (!all && scenePath == null)
            throw new ArgumentException("Pass --ai-all or --ai-scene <file>.");

        var scenes = LoadScenes(root, all, scenePath);
        var game = GameData.LoadDefault();
        var npcs = NpcCatalog.LoadDefault();
        var minions = NpcMinionCatalog.LoadDefault();

        if (refresh)
        {
            foreach (var (path, scene) in scenes)
            {
                if (referenceOverride != null)
                    scene.Oracle.Reference = referenceOverride;
                var trace = MobiusAiReferenceModel.Build(scene, npcs, minions);
                string golden = Path.Combine(root, scene.Oracle.Golden);
                AiSceneJson.WriteTrace(golden, trace);
                Console.WriteLine($"refreshed AI oracle {scene.Id} -> {golden}");
            }
            Console.WriteLine(
                "AI oracle boundary: reference-model trace only; no headless Mobius GameServer was run " +
                "(fullServerVerified=false).");
            if (!all)
                return 0;
        }

        bool allPass = true;
        var reports = new List<AiSceneReport>();
        foreach (var (path, scene) in scenes)
        {
            string golden = Path.Combine(root, scene.Oracle.Golden);
            if (!File.Exists(golden))
                throw new FileNotFoundException(
                    $"AI oracle golden missing for {scene.Id}: {golden}. Run --refresh-ai-oracle.", golden);
            var expected = AiSceneJson.LoadTrace(golden);
            var actual = AiSceneProbe.RunAsync(scene, game, npcs).GetAwaiter().GetResult();
            var report = AiSceneRunner.Run(scene, Relative(root, path), expected, actual);
            reports.Add(report);
            AiReportWriter.Write(report, reportDir);
            File.WriteAllText(
                Path.Combine(reportDir, scene.Id + ".actual.json"),
                System.Text.Json.JsonSerializer.Serialize(actual, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));
            allPass &= report.Pass;
            Console.WriteLine(
                $"{scene.Id}: {(report.Pass ? "PASS" : "FAIL")} " +
                $"({report.Passed} passed, {report.Failed} failed) -> {Path.Combine(reportDir, scene.Id + ".md")}");
            foreach (var row in report.Rows.Where(static r => !r.Pass).Take(8))
                Console.WriteLine($"  FAIL t{row.Tick} {row.Parameter}: expected {row.Expected}, actual {row.Actual}");
        }
        AiReportWriter.WriteSummary(reports, reportDir);
        Console.WriteLine(
            $"AI reference differential: {reports.Count(static r => r.Pass)}/{reports.Count} scenes pass; " +
            $"summary -> {Path.Combine(reportDir, "summary.md")}");
        return allPass ? 0 : 1;
    }

    static List<(string Path, AiScene Scene)> LoadScenes(string root, bool all, string? scenePath)
    {
        var paths = new List<string>();
        if (all)
        {
            string directory = Path.Combine(root, "tools", "Fineage.MobiusDiff", "scenes", "ai");
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"AI scene directory not found: {directory}");
            paths.AddRange(Directory.GetFiles(directory, "*.json")
                .Where(static f => !string.Equals(Path.GetFileName(f), "manifest.json", StringComparison.Ordinal))
                .OrderBy(static f => f, StringComparer.Ordinal));
        }
        else
        {
            string path = Path.IsPathRooted(scenePath!) ? scenePath! : Path.GetFullPath(scenePath!, root);
            paths.Add(path);
        }
        if (paths.Count == 0)
            throw new InvalidDataException("No AI scenes found.");
        var loaded = paths.Select(path => (path, AiSceneJson.Load(path))).ToList();
        if (all)
        {
            string manifestPath = Path.Combine(
                root, "tools", "Fineage.MobiusDiff", "scenes", "ai", "manifest.json");
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException(
                    $"AI coverage manifest missing: {manifestPath}. Refusing to run a partial scene set.",
                    manifestPath);
            AiCoverageManifest.Load(manifestPath)
                .Validate(loaded.Select(static s => s.Item2).ToList(), manifestPath);
        }
        return loaded;
    }

    static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
