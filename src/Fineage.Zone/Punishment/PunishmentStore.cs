using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Zone.Punishment;

public interface IPunishmentStore
{
  PunishmentSnapshot Load();

  void Save(PunishmentSnapshot snapshot);
}

public sealed class MemoryPunishmentStore : IPunishmentStore
{
  PunishmentSnapshot _snapshot = new();

  public PunishmentSnapshot Load() => Clone(_snapshot);

  public void Save(PunishmentSnapshot snapshot) => _snapshot = Clone(snapshot);

  static PunishmentSnapshot Clone(PunishmentSnapshot source)
  {
    var json = JsonSerializer.Serialize(source);
    return JsonSerializer.Deserialize<PunishmentSnapshot>(json) ?? new PunishmentSnapshot();
  }
}

public sealed class JsonPunishmentStore : IPunishmentStore
{
  static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
  };

  readonly string _path;
  readonly object _gate = new();

  public JsonPunishmentStore(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
      throw new ArgumentException("Punishment store path is required.", nameof(path));
    _path = path;
  }

  public PunishmentSnapshot Load()
  {
    lock (_gate)
    {
      if (!File.Exists(_path))
        return new PunishmentSnapshot();

      string json = File.ReadAllText(_path);
      var data = JsonSerializer.Deserialize<PunishmentSnapshot>(json, JsonOptions)
        ?? throw new InvalidDataException($"empty punishment store: {_path}");
      if (data.Version != 1)
        throw new InvalidDataException($"unsupported punishment store version {data.Version}");
      data.Rows ??= [];
      return data;
    }
  }

  public void Save(PunishmentSnapshot snapshot)
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