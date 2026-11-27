using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game.Persistence;

namespace Fineage.Zone.SevenSigns;

public sealed class SevenSignsSnapshot : IVersionedStateSnapshot
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

    public int PreviousWinner { get; set; }

    public long DawnStonePoints { get; set; }

    public long DuskStonePoints { get; set; }

    public int DawnFestivalScore { get; set; }

    public int DuskFestivalScore { get; set; }

    public long NextChangeUnixMs { get; set; }

    public int AvariceOwner { get; set; }

    public int GnosisOwner { get; set; }

    public int StrifeOwner { get; set; }

    public List<SevenSignsPlayerRow> Players { get; set; } = [];
}

public sealed class SevenSignsPlayerRow
{
    public int CharacterId { get; set; }

    public int Cabal { get; set; }

    public int Seal { get; set; }

    public int BlueStones { get; set; }

    public int GreenStones { get; set; }

    public int RedStones { get; set; }

    public int Contribution { get; set; }

    public int AncientAdena { get; set; }
}

public interface ISevenSignsStore
{
    SevenSignsSnapshot Load();

    void Save(SevenSignsSnapshot snapshot);
}

public sealed class MemorySevenSignsStore : ISevenSignsStore
{
    SevenSignsSnapshot _snapshot = new();

    public SevenSignsSnapshot Load() => Clone(_snapshot);

    public void Save(SevenSignsSnapshot snapshot) => _snapshot = Clone(snapshot);

    static SevenSignsSnapshot Clone(SevenSignsSnapshot source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<SevenSignsSnapshot>(json) ?? new SevenSignsSnapshot();
    }
}

public sealed class JsonSevenSignsStore : ISevenSignsStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    readonly string _path;
    readonly object _gate = new();

    public JsonSevenSignsStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Seven Signs store path is required.", nameof(path));
        _path = path;
    }

    public SevenSignsSnapshot Load()
    {
        lock (_gate)
        {
            var data = AtomicJsonFile.Load(
                _path,
                "seven-signs",
                1,
                JsonOptions,
                static () => new SevenSignsSnapshot());
            data.Players ??= [];
            return data;
        }
    }

    public void Save(SevenSignsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
            AtomicJsonFile.Write(_path, snapshot, JsonOptions);
    }
}
