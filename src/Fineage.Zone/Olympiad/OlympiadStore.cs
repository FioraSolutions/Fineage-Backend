using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game.Persistence;

namespace Fineage.Zone.Olympiad;

public sealed class OlympiadSnapshot : IVersionedStateSnapshot
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonIgnore]
    public int Version
    {
        get => SchemaVersion;
        set => SchemaVersion = value;
    }

    public int Cycle { get; set; } = 1;
    public int Period { get; set; }
    public long OlympiadEndUnixMs { get; set; }
    public long ValidationEndUnixMs { get; set; }
    public long NextWeeklyUnixMs { get; set; }
    public List<int> Granted { get; set; } = [];
    public List<OlympiadNobleRow> Nobles { get; set; } = [];

    /// <summary>Mobius <c>heroes</c>: heroes of the last finished period (claimed or not).</summary>
    public List<OlympiadHeroRow> Heroes { get; set; } = [];

    /// <summary>Mobius <c>olympiad_nobles_eom</c>: the nobles table frozen at the last period end (ranking pages).</summary>
    public List<OlympiadNobleRow> LastPeriod { get; set; } = [];

    /// <summary>Unclaimed Noblesse Gate Pass rewards per character (Mobius <c>UNCLAIMED_OLYMPIAD_PASSES</c>).</summary>
    public List<OlympiadPassRow> Passes { get; set; } = [];
}

public sealed class OlympiadHeroRow
{
    public int CharacterId { get; set; }
    public int ClassId { get; set; }
    public string Name { get; set; } = "";
    public bool Claimed { get; set; }
    public int Count { get; set; }
}

public sealed class OlympiadPassRow
{
    public int CharacterId { get; set; }
    public int Passes { get; set; }
}

public sealed class OlympiadNobleRow
{
    public int CharacterId { get; set; }
    public int ClassId { get; set; }
    public string Name { get; set; } = "";
    public int Points { get; set; }
    public int Done { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Drawn { get; set; }
}

public interface IOlympiadStore
{
    OlympiadSnapshot Load();
    void Save(OlympiadSnapshot snapshot);
}

public sealed class MemoryOlympiadStore : IOlympiadStore
{
    OlympiadSnapshot _snapshot = new();

    public OlympiadSnapshot Load() => Clone(_snapshot);

    public void Save(OlympiadSnapshot snapshot) => _snapshot = Clone(snapshot);

    static OlympiadSnapshot Clone(OlympiadSnapshot source)
    {
        string json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<OlympiadSnapshot>(json) ?? new OlympiadSnapshot();
    }
}

public sealed class JsonOlympiadStore : IOlympiadStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    readonly string _path;
    readonly object _gate = new();

    public JsonOlympiadStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Olympiad store path is required.", nameof(path));
        _path = path;
    }

    public OlympiadSnapshot Load()
    {
        lock (_gate)
        {
            var data = AtomicJsonFile.Load(
                _path,
                "olympiad",
                1,
                JsonOptions,
                static () => new OlympiadSnapshot());
            data.Granted ??= [];
            data.Nobles ??= [];
            data.Heroes ??= [];
            data.LastPeriod ??= [];
            data.Passes ??= [];
            return data;
        }
    }

    public void Save(OlympiadSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
            AtomicJsonFile.Write(_path, snapshot, JsonOptions);
    }
}
