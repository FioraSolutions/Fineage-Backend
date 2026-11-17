using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Zone.Olympiad;

public sealed class OlympiadSnapshot
{
    public int Version { get; set; } = 1;
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
            if (!File.Exists(_path))
                return new OlympiadSnapshot();
            var data = JsonSerializer.Deserialize<OlympiadSnapshot>(File.ReadAllText(_path), JsonOptions)
                ?? throw new InvalidDataException($"empty olympiad store: {_path}");
            if (data.Version != 1)
                throw new InvalidDataException($"unsupported olympiad store version {data.Version}");
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
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
    }
}
