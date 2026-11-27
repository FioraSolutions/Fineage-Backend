using System.Text.Json;
using Fineage.Game.Punishment;

namespace Fineage.Accounts;

/// <summary>The punishment state as the zone saved it in <c>world_state</c> (#996), for login's ban gate.</summary>
public static class WorldStatePunishments
{
    /// <summary>Null when the zone has never saved punishments. A row this build cannot read throws, naming it.</summary>
    public static PunishmentSnapshot? Load(IAccountStore accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var document = accounts.LoadWorldState(PunishmentStateJson.System);
        if (document == null)
            return null;
        string where = $"database world_state system='{PunishmentStateJson.System}' revision={document.Revision}";
        if (document.SchemaVersion != PunishmentStateJson.SchemaVersion)
            throw new InvalidOperationException(
                $"punishment state at {where} has schema version {document.SchemaVersion}; this build reads {PunishmentStateJson.SchemaVersion}");
        PunishmentSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<PunishmentSnapshot>(document.Json, PunishmentStateJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"punishment state at {where} is not valid: {ex.Message}", ex);
        }

        if (snapshot == null)
            throw new InvalidOperationException($"punishment state at {where} is null");
        snapshot.Rows ??= [];
        return snapshot;
    }

    public static AccountBanGate Gate(IAccountStore accounts) => new(() => Load(accounts));
}
