using System.Globalization;
using System.Text.Json;

namespace Fineage.MobiusDiff;

public sealed class AiDiffRow
{
    public int Tick { get; set; }
    public string Parameter { get; set; } = "";
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
    public bool Pass { get; set; }
    public string? Note { get; set; }
}

public sealed class AiSceneReport
{
    public string Scenario { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string ScenePath { get; set; } = "";
    public int Seed { get; set; }
    public AiTraceProvenance Oracle { get; set; } = new();
    public bool Pass { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public List<AiDiffRow> Rows { get; set; } = [];

    /// <summary>Actions/tick list sufficient to reproduce the scene outside the harness.</summary>
    public List<string> Reproduction { get; set; } = [];
}

public static class AiSceneRunner
{
    public static AiSceneReport Run(
        AiScene scene,
        string scenePath,
        AiTraceFile expected,
        AiTraceFile actual,
        Action<AiTraceFile>? mutateActual = null)
    {
        mutateActual?.Invoke(actual);
        if (expected.Scenario != scene.Id)
            throw new InvalidDataException(
                $"{scene.Id}: oracle scenario '{expected.Scenario}' does not match the scene.");
        if (actual.Scenario != scene.Id)
            throw new InvalidDataException(
                $"{scene.Id}: probe scenario '{actual.Scenario}' does not match the scene.");
        if (expected.Oracle.FineageOutputUsed)
            throw new InvalidDataException(
                $"{scene.Id}: oracle provenance says Fineage output was used; AI expected values must be independent.");

        var actualByTick = actual.Steps.ToDictionary(static s => s.Tick, static s => s.Values);
        var report = new AiSceneReport
        {
            Scenario = scene.Id,
            Category = scene.Category,
            Description = scene.Description,
            ScenePath = scenePath,
            Seed = scene.Seed,
            Oracle = expected.Oracle,
            Reproduction = Reproduction(scene)
        };

        foreach (var step in expected.Steps.OrderBy(static s => s.Tick))
        {
            actualByTick.TryGetValue(step.Tick, out var actualValues);
            foreach (var (field, expectedValue) in step.Values)
            {
                string actualValue = actualValues != null && actualValues.TryGetValue(field, out var v) ? v : "<missing>";
                var tolerance = scene.Tolerances.TryGetValue(field, out var own)
                    ? own
                    : scene.Tolerances.TryGetValue("default", out var fallback) ? fallback : null;
                bool pass = Compare(expectedValue, actualValue, tolerance);
                string? note = scene.KnownDivergences.TryGetValue(field, out var known) ? known : null;
                report.Rows.Add(new AiDiffRow
                {
                    Tick = step.Tick,
                    Parameter = field,
                    Expected = expectedValue,
                    Actual = actualValue,
                    Pass = pass,
                    Note = note
                });
            }
        }

        report.Passed = report.Rows.Count(static r => r.Pass);
        report.Failed = report.Rows.Count - report.Passed;
        report.Pass = report.Failed == 0;
        return report;
    }

    static bool Compare(string expected, string actual, AiToleranceSpec? tolerance)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return true;
        if (double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out double e) &&
            double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out double a))
        {
            double allowed = tolerance?.Effective(e) ?? 0;
            return Math.Abs(a - e) <= allowed;
        }
        return false;
    }

    static List<string> Reproduction(AiScene scene)
    {
        var lines = new List<string>
        {
            $"scene {scene.Id} ({scene.Category}); seed {scene.Seed}",
            $"player class={scene.Player.ClassId} level={scene.Player.Level} pos=({scene.Player.X},{scene.Player.Y},{scene.Player.Z}) karma={scene.Player.Karma}"
        };
        foreach (var npc in scene.Npcs)
        {
            lines.Add(
                $"npc {npc.Key}: mobius={npc.MobiusId} fineage={npc.FineageId} " +
                $"pos=({npc.X},{npc.Y},{npc.Z}) hp%={npc.HpPercent} leader={npc.Leader} " +
                $"source={npc.Source}");
        }
        foreach (var action in scene.Actions.OrderBy(static a => a.Tick))
        {
            lines.Add(action.Type switch
            {
                "move" => $"t{action.Tick}: move player -> ({action.X},{action.Y},{action.Z})",
                "damage" => $"t{action.Tick}: damage {action.Npc} by {action.Amount}",
                "karma" => $"t{action.Tick}: set player karma {action.Value}",
                "setGlobalAggro" => $"t{action.Tick}: set {action.Npc} global aggro {action.Value}",
                _ => $"t{action.Tick}: {action.Type}"
            });
        }
        return lines;
    }
}

public static class AiReportWriter
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Write(AiSceneReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, report.Scenario + ".json"), JsonSerializer.Serialize(report, Json));
        File.WriteAllText(Path.Combine(directory, report.Scenario + ".md"), Markdown(report));
    }

    public static void WriteSummary(IReadOnlyList<AiSceneReport> reports, string directory)
    {
        Directory.CreateDirectory(directory);
        int passed = reports.Count(static r => r.Pass);
        var lines = new List<string>
        {
            "# AI behavioural reference differential",
            "",
            $"- Scenes: {reports.Count}, passing: {passed}, failing: {reports.Count - passed}",
            $"- Oracle: `{reports.FirstOrDefault()?.Oracle.Driver}`",
            $"- Full Mobius server verified: **{reports.Any(static r => r.Oracle.FullServerVerified)}**",
            "",
            "| Scene | Category | Result | Failed parameters |",
            "|---|---|---|---|"
        };
        foreach (var report in reports.OrderBy(static r => r.Category, StringComparer.Ordinal)
                     .ThenBy(static r => r.Scenario, StringComparer.Ordinal))
        {
            string failed = string.Join(", ", report.Rows.Where(static r => !r.Pass)
                .Select(static r => $"t{r.Tick}:{r.Parameter}").Take(8));
            lines.Add($"| {Escape(report.Scenario)} | {Escape(report.Category)} | " +
                      $"{(report.Pass ? "PASS" : "FAIL")} | {Escape(failed)} |");
        }
        if (reports.Any(static r => !r.Oracle.FullServerVerified))
        {
            lines.Add("");
            lines.Add("> Oracle boundary: the expected traces are produced by the independent Mobius AI rule model " +
                      "over Mobius-extracted catalogs, not by a headless Mobius GameServer. See the scene oracle " +
                      "provenance for the blocker.");
        }
        File.WriteAllText(Path.Combine(directory, "summary.md"), string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    public static string Markdown(AiSceneReport report)
    {
        var lines = new List<string>
        {
            $"# AI reference scene: {report.Scenario}",
            "",
            report.Description,
            "",
            $"- Overall: **{(report.Pass ? "PASS" : "FAIL")}** ({report.Passed} passed, {report.Failed} failed)",
            $"- Category: `{report.Category}`; seed: `{report.Seed}`",
            $"- Scene: `{report.ScenePath}`",
            $"- Oracle: `{report.Oracle.Driver}` from `{report.Oracle.Reference}`",
            $"- Full Mobius server verified: **{(report.Oracle.FullServerVerified ? "yes" : "no")}**",
            $"- Fineage output used as expected: **{(report.Oracle.FineageOutputUsed ? "yes" : "no")}**"
        };
        if (!string.IsNullOrWhiteSpace(report.Oracle.Blocker))
            lines.Add($"- Oracle limitation: {report.Oracle.Blocker}");
        lines.Add("");
        lines.Add("## Reproduction");
        foreach (string line in report.Reproduction)
            lines.Add($"- {line}");
        lines.Add("");
        lines.Add("| Tick | Parameter | Expected | Actual | Result | Note |");
        lines.Add("|---:|---|---|---|---|---|");
        foreach (var row in report.Rows)
            lines.Add($"| {row.Tick} | {Escape(row.Parameter)} | {Escape(row.Expected)} | {Escape(row.Actual)} | " +
                      $"{(row.Pass ? "PASS" : "FAIL")} | {Escape(row.Note ?? "")} |");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
