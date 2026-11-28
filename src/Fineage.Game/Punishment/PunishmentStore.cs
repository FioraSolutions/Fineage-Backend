using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>
/// The saved punishment layout (#996: the database <c>world_state</c> row <c>punishment</c>, written by the zone and
/// read by login). Enums are names so a reordered enum cannot turn a ban into a chat ban.
/// </summary>
public static class PunishmentStateJson
{
    public const string System = "punishment";
    public const int SchemaVersion = 1;

    public static JsonSerializerOptions Options { get; } = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
