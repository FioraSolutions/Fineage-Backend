using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game.Persistence;

namespace Fineage.Zone.RaidBossPoints;

public sealed class RaidBossPointsSnapshot : IVersionedStateSnapshot
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonIgnore]
    public int Version
    {
        get => SchemaVersion;
        set => SchemaVersion = value;
    }

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
