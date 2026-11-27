using System.Security.Cryptography;
using Fineage.Accounts;
using Fineage.Game.Persistence;

namespace Fineage.Zone.Persistence;

/// <summary>The cutover found two sources of truth or a sidecar it cannot import. Names the system and the file.</summary>
public sealed class WorldStateCutoverException : InvalidOperationException
{
    public WorldStateCutoverException(string system, string path, string detail, Exception? inner = null)
        : base($"World state '{system}' cutover: {detail} (file '{path}', database world_state system='{system}')", inner)
    {
        System = system;
        FilePath = path;
    }

    public string System { get; }

    public string FilePath { get; }
}

/// <summary>What one start did with one legacy sidecar.</summary>
public enum WorldStateCutoverAction
{
    /// <summary>No sidecar: the database is the only source.</summary>
    None,

    /// <summary>The sidecar was imported into the database and archived.</summary>
    Imported,

    /// <summary>An earlier start imported this exact file and stopped before archiving it; it is archived now.</summary>
    Archived,
}

public readonly record struct WorldStateCutoverResult(string System, WorldStateCutoverAction Action, string? SourcePath, string? ArchivedPath);

/// <summary>
/// The one-time move of the pre-#996 JSON sidecars into the database, run at zone start before any subsystem loads.
/// For each system of <see cref="WorldStateCatalog.All"/>:
/// <list type="bullet">
/// <item>no sidecar file — nothing to do;</item>
/// <item>a sidecar and no saved document — the file is read strictly (a missing schema version, an unknown one, or
/// JSON that does not fit the snapshot stops the start, naming the file; no default is imported), written into
/// <c>world_state</c> with its import record in one transaction, then renamed to
/// <c>&lt;name&gt;.imported-&lt;UTC stamp&gt;</c> so it is never read again;</item>
/// <item>a sidecar whose exact bytes were already imported (the previous start stopped before the rename) — it is
/// archived;</item>
/// <item>a sidecar and a saved document that did not come from it — two sources of truth: the start fails and says
/// which file to remove.</item>
/// </list>
/// </summary>
public static class WorldStateCutover
{
    public static IReadOnlyList<WorldStateCutoverResult> Run(IAccountStore accounts, string stateDirectory, Func<DateTimeOffset> now)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(now);
        string dir = string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory;
        var imports = accounts.ListWorldStateImports().ToDictionary(i => i.System, StringComparer.Ordinal);
        var results = new List<WorldStateCutoverResult>();
        foreach (var kind in WorldStateCatalog.All)
            results.Add(RunOne(accounts, kind, Path.Combine(dir, kind.LegacyFileName), imports, now));
        return results;
    }

    static WorldStateCutoverResult RunOne(
        IAccountStore accounts,
        IWorldStateKind kind,
        string path,
        IReadOnlyDictionary<string, WorldStateImport> imports,
        Func<DateTimeOffset> now)
    {
        if (!File.Exists(path))
            return new(kind.System, WorldStateCutoverAction.None, null, null);

        string full = Path.GetFullPath(path);
        if (kind.SharesNameWithContent && IsContentCatalog(full))
            return new(kind.System, WorldStateCutoverAction.None, null, null);
        string sha = Sha256(full);
        var saved = accounts.LoadWorldState(kind.System);
        if (saved != null)
        {
            if (imports.TryGetValue(kind.System, out var earlier) && earlier.SourceSha256 == sha)
            {
                string archived = Archive(kind, full, earlier.ArchivedPath);
                return new(kind.System, WorldStateCutoverAction.Archived, full, archived);
            }

            throw new WorldStateCutoverException(
                kind.System,
                full,
                $"two sources of truth: both the sidecar and a saved document (revision {saved.Revision}) exist and the file is not the one imported"
                + (imports.TryGetValue(kind.System, out var other) ? $" on {other.ImportedAt:O} from '{other.SourcePath}'" : "")
                + "; the database is the source of truth, so move the file out of the state directory (or restore the database it belongs to) and start again");
        }

        string json;
        try
        {
            json = kind.ReadLegacy(full);
        }
        catch (StateFileException ex)
        {
            throw new WorldStateCutoverException(kind.System, full, "the sidecar cannot be imported: " + ex.Message, ex);
        }

        var stamp = now().ToUniversalTime();
        string archivedPath = full + ".imported-" + stamp.ToString("yyyyMMdd'T'HHmmss'Z'");
        var document = new WorldStateDocument(kind.System, kind.SchemaVersion, 1, json);
        var record = new WorldStateImport(kind.System, full, sha, archivedPath, stamp);
        if (!accounts.TryImportWorldState(document, record))
            throw new WorldStateCutoverException(kind.System, full, "a saved document appeared while the sidecar was being imported");
        Archive(kind, full, archivedPath);
        return new(kind.System, WorldStateCutoverAction.Imported, full, archivedPath);
    }

    static string Archive(IWorldStateKind kind, string full, string archivedPath)
    {
        try
        {
            File.Move(full, archivedPath, overwrite: false);
            return archivedPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorldStateCutoverException(
                kind.System, full, $"imported, but the sidecar could not be archived to '{archivedPath}': {ex.Message}", ex);
        }
    }

    static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// <see cref="IWorldStateKind.SharesNameWithContent"/>: a JSON object with neither <c>schemaVersion</c> nor
    /// <c>version</c> is the content catalog of the same name. Anything else (a versioned snapshot, or a file that is
    /// not a JSON object at all) is treated as a legacy sidecar and read strictly.
    /// </summary>
    static bool IsContentCatalog(string full)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(full));
            return json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                   && !json.RootElement.TryGetProperty("schemaVersion", out _)
                   && !json.RootElement.TryGetProperty("version", out _);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
