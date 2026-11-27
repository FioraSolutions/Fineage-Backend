using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.MobiusDiff;

/// <summary>
/// One executable AI reference scene. It is a deterministic trace format: the same JSON drives
/// <see cref="AiSceneProbe"/> (production ZoneWorld AI) and <see cref="MobiusAiReferenceModel"/> (source-backed
/// reference trace). Nothing about a single NPC lives in runner code.
/// </summary>
public sealed class AiScene
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";

    /// <summary>Seed for the world's random streams; every scene is replayable with the same trace.</summary>
    public int Seed { get; set; }

    public AiPlayerSpec Player { get; set; } = new();

    /// <summary>Additional players for hate-transfer scenes. The first player's role is always <c>player</c>.</summary>
    public List<AiPlayerSpec> ExtraPlayers { get; set; } = [];

    public IReadOnlyList<AiPlayerSpec> AllPlayers => [Player, .. ExtraPlayers];
    public List<AiNpcSpec> Npcs { get; set; } = [];
    public List<AiActionSpec> Actions { get; set; } = [];
    public AiObserveSpec Observe { get; set; } = new();

    public AiOracleSpec Oracle { get; set; } = new();

    /// <summary>Metric name → tolerance; an entry named <c>default</c> applies to all remaining fields.</summary>
    public Dictionary<string, AiToleranceSpec> Tolerances { get; set; } = new();

    /// <summary>Report-only note for a divergence that is known to be owned by a separate implementation issue.</summary>
    public Dictionary<string, string> KnownDivergences { get; set; } = new();
}

public sealed class AiPlayerSpec
{
    /// <summary>Scene role used in <c>hate.&lt;role&gt;</c> and target values.</summary>
    public string Key { get; set; } = "player";

    public int ClassId { get; set; } = 25;
    public int Level { get; set; } = 1;
    public int MaxHp { get; set; } = 100;
    public int Hp { get; set; } = 100;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Karma { get; set; }
}

public sealed class AiNpcSpec
{
    /// <summary>Stable scene role, used in field names (<c>npc.mob.target</c>).</summary>
    public string Key { get; set; } = "";

    /// <summary>Mobius CT0 template id (the id in <c>stats/npcs</c>).</summary>
    public int MobiusId { get; set; }

    /// <summary>Fineage template id in <c>data/npcs.json</c>.</summary>
    public int FineageId { get; set; }

    /// <summary>Source path inside the Mobius reference tree.</summary>
    public string Source { get; set; } = "";

    public int ObjectId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }

    /// <summary>Respawn delay for this scene spawn (Mobius spawn row). 0 = no respawn.</summary>
    public int RespawnDelaySeconds { get; set; }

    public int RespawnRandomSeconds { get; set; }

    /// <summary>Scene precondition: Mobius <c>_globalAggro</c> starts at -10 and climbs once per think.</summary>
    public int GlobalAggro { get; set; }

    public int HpPercent { get; set; } = 100;

    /// <summary>Scene role of this NPC's leader (minion follow). Empty = no leader.</summary>
    public string Leader { get; set; } = "";
}

/// <summary>
/// A player action executed at the start of a tick, before that tick's simulation step.
/// Ticks are 1-based; <see cref="AiObserveSpec.Ticks"/> is observed after the step of that tick.
/// </summary>
public sealed class AiActionSpec
{
    public int Tick { get; set; }

    /// <summary>move, damage, karma, or setGlobalAggro.</summary>
    public string Type { get; set; } = "";

    public string Npc { get; set; } = "";
    public string Player { get; set; } = "player";
    public int Amount { get; set; }
    public int Value { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public sealed class AiObserveSpec
{
    /// <summary>Ticks at which the normalized field snapshot is taken.</summary>
    public List<int> Ticks { get; set; } = [];

    /// <summary>
    /// Field names: <c>npc.&lt;role&gt;.target</c>, <c>.hate.&lt;role&gt;</c>, <c>.chasing</c>, <c>.returningHome</c>,
    /// <c>.thinking</c>, <c>.present</c>, <c>.dead</c>, <c>.hp</c>, <c>.leader</c>, <c>.minionCount</c>,
    /// <c>.raidMinion</c>, <c>.aiSkills</c>, plus <c>world.npcCount</c>.
    /// </summary>
    public List<string> Fields { get; set; } = [];
}

public sealed class AiOracleSpec
{
    /// <summary>Always <c>mobius-ai-reference-trace</c> in this harness.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Committed golden trace, relative to the repository root.</summary>
    public string Golden { get; set; } = "";

    /// <summary>Mobius CT0 reference tree label used when refreshing.</summary>
    public string Reference { get; set; } = "ref/mobius-ct0-interlude";
}

public sealed class AiToleranceSpec
{
    public double Absolute { get; set; }
    public double Relative { get; set; }

    public double Effective(double expected) => Math.Abs(Absolute) + Math.Abs(Relative) * Math.Abs(expected);
}

public sealed class AiTraceStep
{
    public int Tick { get; set; }
    public Dictionary<string, string> Values { get; set; } = new();
}

public sealed class AiTraceFile
{
    public string Scenario { get; set; } = "";
    public string Category { get; set; } = "";
    public AiTraceProvenance Oracle { get; set; } = new();
    public List<AiNpcReference> Npcs { get; set; } = [];
    public List<AiTraceStep> Steps { get; set; } = [];
}

public sealed class AiNpcReference
{
    public string Key { get; set; } = "";
    public int MobiusId { get; set; }
    public int FineageId { get; set; }
    public string Source { get; set; } = "";
}

public sealed class AiTraceProvenance
{
    public string Kind { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Driver { get; set; } = "";
    public string GeneratedBy { get; set; } = "";
    public List<string> Sources { get; set; } = [];
    public bool FullServerVerified { get; set; }
    public string? Blocker { get; set; }
    public bool FineageOutputUsed { get; set; }
}

public static class AiSceneJson
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

    public static AiScene Load(string path)
    {
        var scene = JsonSerializer.Deserialize<AiScene>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"{path} was empty.");
        Validate(scene, path);
        return scene;
    }

    public static AiTraceFile LoadTrace(string path) =>
        JsonSerializer.Deserialize<AiTraceFile>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"{path} was empty.");

    public static void WriteTrace(string path, AiTraceFile trace)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(trace, Options));
    }

    static void Validate(AiScene scene, string path)
    {
        if (string.IsNullOrWhiteSpace(scene.Id))
            throw new InvalidDataException($"{path} has no id.");
        if (string.IsNullOrWhiteSpace(scene.Category))
            throw new InvalidDataException($"{path} has no category.");
        if (scene.Npcs.Count == 0)
            throw new InvalidDataException($"{path} has no npcs.");
        if (scene.Observe.Ticks.Count == 0)
            throw new InvalidDataException($"{path} has no observe.ticks.");
        if (scene.Observe.Fields.Count == 0)
            throw new InvalidDataException($"{path} has no observe.fields.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<int>();
        foreach (var npc in scene.Npcs)
        {
            if (string.IsNullOrWhiteSpace(npc.Key) || !keys.Add(npc.Key))
                throw new InvalidDataException($"{path} has a missing or duplicate npc key '{npc.Key}'.");
            if (npc.MobiusId <= 0 || npc.FineageId <= 0)
                throw new InvalidDataException($"{path} npc {npc.Key} must name both mobiusId and fineageId.");
            if (npc.ObjectId <= 0 || !ids.Add(npc.ObjectId))
                throw new InvalidDataException($"{path} npc {npc.Key} has a missing or duplicate objectId.");
            if (npc.HpPercent is < 1 or > 100)
                throw new InvalidDataException($"{path} npc {npc.Key} hpPercent must be 1..100.");
        }
        foreach (var npc in scene.Npcs)
        {
            if (!string.IsNullOrWhiteSpace(npc.Leader) && !keys.Contains(npc.Leader))
                throw new InvalidDataException($"{path} npc {npc.Key} leader '{npc.Leader}' is not a scene role.");
        }

        var playerKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in scene.AllPlayers)
        {
            if (string.IsNullOrWhiteSpace(player.Key) || !playerKeys.Add(player.Key))
                throw new InvalidDataException($"{path} has a missing or duplicate player key '{player.Key}'.");
        }
        foreach (var action in scene.Actions)
        {
            if (action.Tick < 1)
                throw new InvalidDataException($"{path} action tick must be >= 1.");
            if (action.Player.Length > 0 && !playerKeys.Contains(action.Player))
                throw new InvalidDataException($"{path} action at tick {action.Tick} names unknown player '{action.Player}'.");
        }
        foreach (var action in scene.Actions)
        {
            if (action.Tick < 1)
                throw new InvalidDataException($"{path} action tick must be >= 1.");
            if (action.Type is not ("move" or "damage" or "karma" or "setGlobalAggro"))
                throw new InvalidDataException($"{path} action type '{action.Type}' is unknown.");
            if (action.Type is "damage" or "setGlobalAggro" && !keys.Contains(action.Npc))
                throw new InvalidDataException($"{path} action at tick {action.Tick} names unknown npc '{action.Npc}'.");
        }

        foreach (var tick in scene.Observe.Ticks)
            if (tick < 1)
                throw new InvalidDataException($"{path} observe tick must be >= 1.");
        foreach (var field in scene.Observe.Fields)
            AiFieldSpec.Parse(field, keys);
    }
}

/// <summary>Parser for the normalized field names shared by the probe and the reference model.</summary>
public static class AiFieldSpec
{
    public readonly record struct Field(string Kind, string Npc, string Sub, string Target);

    public static Field Parse(string text, IReadOnlySet<string>? npcKeys = null)
    {
        string[] parts = text.Split('.');
        if (parts.Length == 2 && parts[0] == "world" && parts[1] is "npcCount" or "thinkingCount")
            return new Field("world", "", parts[1], "");
        if (parts.Length < 3 || parts[0] != "npc")
            throw new InvalidDataException($"Unknown AI observe field '{text}'.");
        string npc = parts[1];
        if (npcKeys != null && !npcKeys.Contains(npc))
            throw new InvalidDataException($"AI observe field '{text}' names unknown npc role '{npc}'.");
        string sub = parts[2];
        if (sub == "hate")
        {
            if (parts.Length != 4 || parts[3].Length == 0)
                throw new InvalidDataException($"AI observe field '{text}' must name a hate target role.");
            return new Field("npc", npc, "hate", parts[3]);
        }
        if (parts.Length != 3)
            throw new InvalidDataException($"Unknown AI observe field '{text}'.");
        if (sub is not ("target" or "chasing" or "returningHome" or "thinking" or "present" or "dead" or "hp"
            or "leader" or "minionCount" or "raidMinion" or "aiSkills"))
            throw new InvalidDataException($"Unknown AI observe field '{text}'.");
        return new Field("npc", npc, sub, "");
    }
}

/// <summary>
/// Strict scene-set contract. The nightly job and tests fail closed if a required category is missing,
/// duplicated, under-counted, or if an unlisted category appears. This prevents the catalogue from
/// silently shrinking and prevents a "green" run over an empty or partial set.
/// </summary>
public sealed class AiCoverageManifest
{
    public string Source { get; set; } = "";
    public int TotalMin { get; set; }
    public int TotalMax { get; set; }
    public Dictionary<string, int> RequiredCategories { get; set; } = new(StringComparer.Ordinal);

    public static AiCoverageManifest Load(string path)
    {
        var manifest = JsonSerializer.Deserialize<AiCoverageManifest>(File.ReadAllText(path), AiSceneJson.Options)
            ?? throw new InvalidDataException($"{path} was empty.");
        if (manifest.TotalMin <= 0 || manifest.TotalMax < manifest.TotalMin)
            throw new InvalidDataException($"{path} has an invalid total scene range.");
        if (manifest.RequiredCategories.Count == 0)
            throw new InvalidDataException($"{path} has no requiredCategories.");
        return manifest;
    }

    public void Validate(IReadOnlyList<AiScene> scenes, string path)
    {
        if (scenes.Count < TotalMin || scenes.Count > TotalMax)
            throw new InvalidDataException(
                $"{path}: expected {TotalMin}..{TotalMax} AI scenes, found {scenes.Count}.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scene in scenes)
        {
            if (!ids.Add(scene.Id))
                throw new InvalidDataException($"{path}: duplicate scene id '{scene.Id}'.");
            if (!RequiredCategories.ContainsKey(scene.Category))
                throw new InvalidDataException(
                    $"{path}: scene '{scene.Id}' uses unlisted category '{scene.Category}'.");
        }
        foreach (var (category, minimum) in RequiredCategories)
        {
            int count = scenes.Count(s => s.Category == category);
            if (count < minimum)
                throw new InvalidDataException(
                    $"{path}: category '{category}' needs at least {minimum} scenes, found {count}.");
        }
    }
}
