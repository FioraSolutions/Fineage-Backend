using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.MobiusDiff;

/// <summary>
/// One executable Mobius-vs-Fineage scenario. The same JSON drives the Fineage probe and the Java
/// Mobius formula oracle; nothing scenario-specific lives in code.
/// </summary>
public sealed class Scenario
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Seed for the Fineage sample roll (deterministic inputs).</summary>
    public int Seed { get; set; }

    /// <summary>Number of swings sampled for statistical metrics. 0 disables sampling.</summary>
    public int Samples { get; set; }

    public AttackerSpec Attacker { get; set; } = new();

    public TargetSpec Target { get; set; } = new();

    public ContactSpec Contact { get; set; } = new();

    public OracleSpec Oracle { get; set; } = new();

    /// <summary>Metric name → tolerance. A <c>default</c> entry covers metrics without their own row.</summary>
    public Dictionary<string, ToleranceSpec> Tolerances { get; set; } = new();

    /// <summary>Optional allowlist of oracle metrics to compare; empty means every metric in the golden.</summary>
    public List<string> Metrics { get; set; } = new();

    /// <summary>Optional human note included in the report for a metric that is expected to expose a known gap.</summary>
    public Dictionary<string, string> KnownDivergences { get; set; } = new();
}

public sealed class AttackerSpec
{
    public int ClassId { get; set; }
    public int Level { get; set; }

    public List<GearSpec> Gear { get; set; } = new();

    /// <summary>Active abnormals applied through <c>skill-stat-effects.json</c>.</summary>
    public List<BuffSpec> Buffs { get; set; } = new();
}

public sealed class GearSpec
{
    /// <summary>Fineage paperdoll slot: rhand, lhand, chest, legs, head, gloves, feet, under, cloak, neck, …</summary>
    public string Slot { get; set; } = "";
    public int ItemId { get; set; }
    public int Enchant { get; set; }
}

public sealed class BuffSpec
{
    public int SkillId { get; set; }
    public int Level { get; set; }
}

public sealed class TargetSpec
{
    public int NpcId { get; set; }
}

public sealed class ContactSpec
{
    /// <summary>front, side, or behind. Plain attacks use Mobius proximity 1 / 1.1 / 1.2.</summary>
    public string Side { get; set; } = "front";
    public bool Soulshot { get; set; }
    public bool AttackerBow { get; set; }
}

public sealed class OracleSpec
{
    /// <summary>Currently <c>mobius-formula-java</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Committed golden metrics, relative to the repository root.</summary>
    public string Golden { get; set; } = "";

    /// <summary>Mobius reference tree used when regenerating the golden.</summary>
    public string Reference { get; set; } = "ref/mobius-ct0-interlude";
}

public sealed class ToleranceSpec
{
    public double Absolute { get; set; }
    public double Relative { get; set; }

    public double Effective(double expected) => Math.Abs(Absolute) + Math.Abs(Relative) * Math.Abs(expected);
}

public sealed class OracleFile
{
    public string Scenario { get; set; } = "";
    public OracleProvenance Oracle { get; set; } = new();
    public Dictionary<string, double> Metrics { get; set; } = new();
}

public sealed class OracleProvenance
{
    public string Kind { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Driver { get; set; } = "";
    public List<string> Formulas { get; set; } = new();
    public bool FullServerVerified { get; set; }
    public string? Blocker { get; set; }
}

public static class ScenarioJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Scenario Load(string path)
    {
        var scenario = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"{path} was empty.");
        if (string.IsNullOrWhiteSpace(scenario.Id))
            throw new InvalidDataException($"{path} has no id.");
        if (scenario.Attacker.Level < 1)
            throw new InvalidDataException($"{path} attacker.level must be positive.");
        if (scenario.Samples < 0)
            throw new InvalidDataException($"{path} samples cannot be negative.");
        return scenario;
    }

    public static OracleFile LoadOracle(string path) =>
        JsonSerializer.Deserialize<OracleFile>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"{path} was empty.");
}
