using System.Globalization;
using System.Text.Json;
using Fineage.Game;
using Fineage.Zone.Npcs;

namespace Fineage.MobiusDiff;

public sealed class DiffRow
{
    public string Parameter { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Expected { get; set; }
    public double Actual { get; set; }
    public double Delta { get; set; }
    public double Tolerance { get; set; }
    public bool Pass { get; set; }
    public string? Note { get; set; }
}

public sealed class DiffReport
{
    public string Scenario { get; set; } = "";
    public string Description { get; set; } = "";
    public int Seed { get; set; }
    public int Samples { get; set; }
    public OracleProvenance Oracle { get; set; } = new();
    public bool Pass { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public List<DiffRow> Rows { get; set; } = new();
}

public static class ScenarioRunner
{
    public static DiffReport Run(
        Scenario scenario,
        OracleFile oracle,
        GameData data,
        NpcCatalog npcs,
        Action<ActualMetrics>? mutateActual = null)
    {
        if (oracle.Scenario != scenario.Id)
            throw new InvalidDataException(
                $"Oracle file scenario '{oracle.Scenario}' does not match scenario '{scenario.Id}'.");

        var actual = FineageProbe.Measure(scenario, data, npcs);
        mutateActual?.Invoke(actual);

        var names = scenario.Metrics.Count > 0
            ? scenario.Metrics
            : oracle.Metrics.Keys.ToList();

        var report = new DiffReport
        {
            Scenario = scenario.Id,
            Description = scenario.Description,
            Seed = scenario.Seed,
            Samples = scenario.Samples,
            Oracle = oracle.Oracle
        };

        foreach (var name in names)
        {
            if (!oracle.Metrics.TryGetValue(name, out double expected))
                throw new InvalidDataException(
                    $"Scenario {scenario.Id} requests metric '{name}', but the oracle has no such value.");
            if (!actual.TryGetValue(name, out double measured))
                throw new InvalidDataException(
                    $"Scenario {scenario.Id} requests metric '{name}', but the Fineage probe produced no value.");

            if (!scenario.Tolerances.TryGetValue(name, out var tolerance)
                && !scenario.Tolerances.TryGetValue("default", out tolerance))
            {
                report.Rows.Add(new DiffRow
                {
                    Parameter = name,
                    Unit = UnitOf(name),
                    Expected = expected,
                    Actual = measured,
                    Delta = measured - expected,
                    Tolerance = 0,
                    Pass = false,
                    Note = "No tolerance configured for this metric."
                });
                continue;
            }

            double effective = tolerance.Effective(expected);
            double delta = measured - expected;
            bool pass = Math.Abs(delta) <= effective;
            string? known = scenario.KnownDivergences.TryGetValue(name, out var text) ? text : null;
            report.Rows.Add(new DiffRow
            {
                Parameter = name,
                Unit = UnitOf(name),
                Expected = expected,
                Actual = measured,
                Delta = delta,
                Tolerance = effective,
                Pass = pass,
                Note = known
            });
        }

        report.Passed = report.Rows.Count(static r => r.Pass);
        report.Failed = report.Rows.Count - report.Passed;
        report.Pass = report.Failed == 0;
        return report;
    }

    public static string UnitOf(string metric) => metric switch
    {
        "hitChancePermille" or "critChancePermille" => "permille",
        "damageNoCrit" or "damageMeanPerSwing" => "hp",
        _ => "points"
    };
}

public static class DiffReportWriter
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Write(DiffReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        string jsonPath = Path.Combine(directory, report.Scenario + ".json");
        string markdownPath = Path.Combine(directory, report.Scenario + ".md");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, Json));
        File.WriteAllText(markdownPath, Markdown(report));
    }

    public static string Markdown(DiffReport report)
    {
        var lines = new List<string>
        {
            $"# Mobius differential: {report.Scenario}",
            "",
            report.Description,
            "",
            $"- Overall: **{(report.Pass ? "PASS" : "FAIL")}** ({report.Passed} passed, {report.Failed} failed)",
            $"- Seed: `{report.Seed}`; samples: `{report.Samples}`",
            $"- Oracle: `{report.Oracle.Kind}` from `{report.Oracle.Reference}`",
            $"- Driver: `{report.Oracle.Driver}`",
            $"- Full Mobius server verified: **{(report.Oracle.FullServerVerified ? "yes" : "no")}**"
        };
        if (!string.IsNullOrWhiteSpace(report.Oracle.Blocker))
            lines.Add($"- Oracle limitation: {report.Oracle.Blocker}");
        if (report.Oracle.Formulas.Count > 0)
            lines.Add($"- Formulas: {string.Join(", ", report.Oracle.Formulas.Select(static f => $"`{f}`"))}");

        lines.Add("");
        lines.Add("| Parameter | Unit | Expected | Actual | Tolerance | Delta | Result | Note |");
        lines.Add("|---|---:|---:|---:|---:|---:|---|---|");
        foreach (var row in report.Rows)
        {
            lines.Add(
                $"| {Escape(row.Parameter)} | {row.Unit} | {Invariant(row.Expected, "0.######")} | " +
                $"{Invariant(row.Actual, "0.######")} | {Invariant(row.Tolerance, "0.######")} | " +
                $"{Invariant(row.Delta, "+0.######;-0.######;0")} | " +
                $"{(row.Pass ? "PASS" : "FAIL")} | {Escape(row.Note ?? "")} |");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    static string Invariant(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);

    static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
