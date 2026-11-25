using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Zone.RaidBossPoints;

public sealed class RaidBossPointsSnapshot
{
    public int Version { get; set; } = 1;
    public List<RaidBossPointsRow> Characters { get; set; } = [];
}

public sealed class RaidBossPointsRow
{
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    public Dictionary<int, int> ByBoss { get; set; } = [];
}

public interface IRaidBossPointsStore
{
    RaidBossPointsSnapshot Load();
    void Save(RaidBossPointsSnapshot snapshot);
}

public sealed class MemoryRaidBossPointsStore : IRaidBossPointsStore
{
    RaidBossPointsSnapshot _snapshot = new();

    public RaidBossPointsSnapshot Load() => Clone(_snapshot);

    public void Save(RaidBossPointsSnapshot snapshot) => _snapshot = Clone(snapshot);

    static RaidBossPointsSnapshot Clone(RaidBossPointsSnapshot source)
    {
        string json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<RaidBossPointsSnapshot>(json) ?? new RaidBossPointsSnapshot();
    }
}

public sealed class JsonRaidBossPointsStore : IRaidBossPointsStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    readonly string _path;
    readonly object _gate = new();

    public JsonRaidBossPointsStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Raid boss points store path is required.", nameof(path));
        _path = path;
    }

    public RaidBossPointsSnapshot Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return new RaidBossPointsSnapshot();
            var data = JsonSerializer.Deserialize<RaidBossPointsSnapshot>(File.ReadAllText(_path), JsonOptions)
                ?? throw new InvalidDataException($"empty raid boss points store: {_path}");
            if (data.Version != 1)
                throw new InvalidDataException($"unsupported raid boss points store version {data.Version}");
            data.Characters ??= [];
            foreach (var row in data.Characters)
                row.ByBoss ??= [];
            return data;
        }
    }

    public void Save(RaidBossPointsSnapshot snapshot)
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
