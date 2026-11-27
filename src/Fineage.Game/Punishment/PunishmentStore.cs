using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game.Persistence;

namespace Fineage.Game.Punishment;

public interface IPunishmentStore { PunishmentSnapshot Load(); void Save(PunishmentSnapshot snapshot); }

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
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    readonly string _path;
    readonly object _gate = new();
    public string Path => _path;
    public JsonPunishmentStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Punishment store path is required.", nameof(path));
        _path = path;
    }
    public PunishmentSnapshot Load()
    {
        lock (_gate)
        {
            var data = AtomicJsonFile.Load(
                _path,
                "punishment",
                1,
                JsonOptions,
                static () => new PunishmentSnapshot());
            data.Rows ??= [];
            return data;
        }
    }
    public void Save(PunishmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
            AtomicJsonFile.Write(_path, snapshot, JsonOptions);
    }
}
