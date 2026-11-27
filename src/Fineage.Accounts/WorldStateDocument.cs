using System.Text.Json;

namespace Fineage.Accounts;

/// <summary>
/// The saved state of one world subsystem (lottery, monster race, Seven Signs, …), one row per system (#996).
/// <see cref="Json"/> is the subsystem's versioned snapshot; <see cref="SchemaVersion"/> repeats its
/// <c>schemaVersion</c> so a reader can refuse an unknown layout before it parses anything. <see cref="Revision"/>
/// grows with every save in one zone process and across restarts, so an older write never replaces a newer one.
/// </summary>
public sealed record WorldStateDocument(string System, int SchemaVersion, long Revision, string Json)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(System))
            throw new ArgumentException("world state system id is required", nameof(System));
        if (SchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), SchemaVersion, $"world state '{System}': schema version must be positive");
        if (Revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(Revision), Revision, $"world state '{System}': revision must be positive");
        if (string.IsNullOrWhiteSpace(Json))
            throw new ArgumentException($"world state '{System}': payload is empty", nameof(Json));
        int payloadVersion;
        try
        {
            using var json = JsonDocument.Parse(Json);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("schemaVersion", out var version)
                || !version.TryGetInt32(out payloadVersion))
                throw new ArgumentException($"world state '{System}': payload has no integer schemaVersion", nameof(Json));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"world state '{System}': payload is not JSON: {ex.Message}", nameof(Json), ex);
        }

        // The same rule as the world_state CHECK constraint, so the memory store refuses what Postgres refuses.
        if (payloadVersion != SchemaVersion)
            throw new ArgumentException(
                $"world state '{System}': payload schemaVersion {payloadVersion} differs from the row's {SchemaVersion}", nameof(Json));
    }
}

/// <summary>
/// The one-time import of a legacy JSON sidecar into <see cref="WorldStateDocument"/> storage (#996 cutover). The
/// record stays so a restart can tell "this file was already imported and archived" from "two sources of truth".
/// </summary>
public sealed record WorldStateImport(string System, string SourcePath, string SourceSha256, string ArchivedPath, DateTimeOffset ImportedAt);
