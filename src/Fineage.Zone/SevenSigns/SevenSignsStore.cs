using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Zone.SevenSigns;

public sealed class SevenSignsSnapshot
{
    public int Version { get; set; } = 1;

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
            if (!File.Exists(_path))
                return new SevenSignsSnapshot();

            string json = File.ReadAllText(_path);
            var data = JsonSerializer.Deserialize<SevenSignsSnapshot>(json, JsonOptions)
                ?? throw new InvalidDataException($"empty seven signs store: {_path}");
            if (data.Version != 1)
                throw new InvalidDataException($"unsupported seven signs store version {data.Version}");
            data.Players ??= [];
            return data;
        }
    }

    public void Save(SevenSignsSnapshot snapshot)
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
