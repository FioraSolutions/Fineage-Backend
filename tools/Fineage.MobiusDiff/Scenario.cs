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

    /// <summary>
    /// When true, both probes emit the deterministic auto-attack matrix (front/side/behind,
    /// soulshot, crit, shield, timing, range, positional hit chance, CP split for player targets).
    /// </summary>
    public bool Matrix { get; set; }

    /// <summary>
    /// When true, both probes emit the six base attributes, their bonus-table multipliers,
    /// and the derived combat/vital stats (P.Atk … M.Atk.Spd, accuracy, max HP/MP/CP).
    /// </summary>
    public bool StatsMatrix { get; set; }

    /// <summary>
    /// When true, both probes emit the deterministic <c>calcMagicDam</c> cells for <see cref="Magic"/>:
    /// success rate, base / spiritshot / blessed spiritshot damage, crit (NPC ×3, PvP ×2.5), half-fail,
    /// resisted, shield block / perfect block, and the random-spread bounds.
    /// </summary>
    public bool MagicMatrix { get; set; }

    /// <summary>The magic skill row (power / magicLevel from <c>skill-combat.json</c>) driving <see cref="MagicMatrix"/>.</summary>
    public MagicSpec Magic { get; set; } = new();

    /// <summary>
    /// Mobius <c>PlayerStatus.reduceHp</c> CP split cases. Each case emits <c>cpLoss_&lt;key&gt;</c> and
    /// <c>hpLoss_&lt;key&gt;</c> from the same source-verified barrier; empty for ordinary combat scenarios.
    /// </summary>
    public List<CpSplitSpec> CpSplits { get; set; } = new();

    public OracleSpec Oracle { get; set; } = new();

    /// <summary>Metric name → tolerance. A <c>default</c> entry covers metrics without their own row.</summary>
    public Dictionary<string, ToleranceSpec> Tolerances { get; set; } = new();

    /// <summary>Optional allowlist of oracle metrics to compare; empty means every metric in the golden.</summary>
    public List<string> Metrics { get; set; } = new();

    /// <summary>Optional human note included in the report for a metric that is expected to expose a known gap.</summary>
    public Dictionary<string, string> KnownDivergences { get; set; } = new();
}

public sealed class MagicSpec
{
    public int SkillId { get; set; }
    public int Level { get; set; } = 1;

    /// <summary><c>MAGIC_CRIT_DMG</c> stat multiplier folded on a crit (1 = no buff).</summary>
    public double CritPower { get; set; } = 1d;
}

public sealed class AttackerSpec
{
    /// <summary><c>player</c> (default), <c>npc</c>, or <c>summon</c> (pet/servitor catalog row).</summary>
    public string Kind { get; set; } = "player";

    public int ClassId { get; set; }
    public int Level { get; set; }

    /// <summary>Set when <see cref="Kind"/> is <c>npc</c> or <c>summon</c>.</summary>
    public int NpcId { get; set; }

    public List<GearSpec> Gear { get; set; } = new();

    /// <summary>Active abnormals applied through <c>skill-stat-effects.json</c>.</summary>
    public List<BuffSpec> Buffs { get; set; } = new();

    /// <summary>Learned operateType-P skills folded through <c>skill-passives.json</c>.</summary>
    public List<BuffSpec> Passives { get; set; } = new();
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
    /// <summary><c>npc</c> (default when <see cref="NpcId"/> is set), <c>player</c>, or <c>summon</c>.</summary>
    public string Kind { get; set; } = "";

    public int NpcId { get; set; }

    public int ClassId { get; set; }
    public int Level { get; set; }
    public List<GearSpec> Gear { get; set; } = new();
    public List<BuffSpec> Buffs { get; set; } = new();

    /// <summary>Starting bars for the matrix CP/HP split on a player target (defaults 200/200).</summary>
    public int Hp { get; set; } = 200;
    public int Cp { get; set; } = 200;
}

public sealed class ContactSpec
{
    /// <summary>front, side, or behind. Plain attacks use Mobius proximity 1 / 1.1 / 1.2.</summary>
    public string Side { get; set; } = "front";
    public bool Soulshot { get; set; }
    public bool AttackerBow { get; set; }
}

/// <summary>One deterministic CP-barrier input: damage, target bars and the attacker kind gate.</summary>
public sealed class CpSplitSpec
{
    public string Key { get; set; } = "";
    public int TargetHp { get; set; }
    public int TargetCp { get; set; }
    public int Damage { get; set; }
    /// <summary>Mobius <c>attacker.isPlayable()</c>: player or summon.</summary>
    public bool AttackerPlayable { get; set; }
    public bool IgnoreCp { get; set; }
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
        string attackerKind = ActorKinds.Normalize(scenario.Attacker.Kind, playerIfEmpty: true);
        scenario.Attacker.Kind = attackerKind;
        string targetKind = ActorKinds.Normalize(
            scenario.Target.Kind,
            playerIfEmpty: false,
            npcIfPositiveId: scenario.Target.NpcId);
        scenario.Target.Kind = targetKind;
        if (attackerKind == "player" && scenario.Attacker.Level < 1)
            throw new InvalidDataException($"{path} attacker.level must be positive.");
        if (attackerKind != "player" && scenario.Attacker.NpcId <= 0)
            throw new InvalidDataException($"{path} attacker.npcId must be set for {attackerKind} attackers.");
        if (targetKind != "player" && scenario.Target.NpcId <= 0)
            throw new InvalidDataException($"{path} target.npcId must be set for {targetKind} targets.");
        if (targetKind == "player" && scenario.Target.Level < 1)
            throw new InvalidDataException($"{path} target.level must be positive for a player target.");
        if (scenario.Samples < 0)
            throw new InvalidDataException($"{path} samples cannot be negative.");
        return scenario;
    }

    public static OracleFile LoadOracle(string path) =>
        JsonSerializer.Deserialize<OracleFile>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"{path} was empty.");
}


internal static class ActorKinds
{
    public static string Normalize(string? kind, bool playerIfEmpty, int npcIfPositiveId = 0)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            if (playerIfEmpty) return "player";
            return npcIfPositiveId > 0 ? "npc" : "player";
        }

        string value = kind.Trim().ToLowerInvariant();
        if (value is "player" or "npc" or "summon")
            return value;
        throw new InvalidDataException($"Unknown actor kind '{kind}' (expected player, npc, or summon).");
    }

    public static bool IsPlayable(string kind) => kind is "player" or "summon";
}
