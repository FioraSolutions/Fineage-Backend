using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game.Persistence;

namespace Fineage.Zone.Bosses;

public sealed class BossScheduleSnapshot : IVersionedStateSnapshot
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonIgnore]
    public int Version
    {
        get => SchemaVersion;
        set => SchemaVersion = value;
    }

    public List<BossScheduleRow> Bosses { get; set; } = [];
}

public sealed class BossScheduleRow
{
    public int NpcId { get; set; }

    public int Status { get; set; }

    public long RespawnUnixMs { get; set; }

    public int CurrentHp { get; set; }

    public int CurrentMp { get; set; }
}

public interface IBossStore
{
    BossScheduleSnapshot Load();

    void Save(BossScheduleSnapshot snapshot);
}

public sealed class MemoryBossStore : IBossStore
{
    BossScheduleSnapshot _snapshot = new();

    public BossScheduleSnapshot Load() => Clone(_snapshot);

    public void Save(BossScheduleSnapshot snapshot) => _snapshot = Clone(snapshot);

    static BossScheduleSnapshot Clone(BossScheduleSnapshot source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<BossScheduleSnapshot>(json) ?? new BossScheduleSnapshot();
    }
}

public sealed class JsonBossStore : IBossStore
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

    public JsonBossStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Boss schedule store path is required.", nameof(path));
        _path = path;
    }

    public BossScheduleSnapshot Load()
    {
        lock (_gate)
        {
            var data = AtomicJsonFile.Load(
                _path,
                "boss-schedule",
                1,
                JsonOptions,
                static () => new BossScheduleSnapshot());
            data.Bosses ??= [];
            return data;
        }
    }

    public void Save(BossScheduleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
            AtomicJsonFile.Write(_path, snapshot, JsonOptions);
    }
}
