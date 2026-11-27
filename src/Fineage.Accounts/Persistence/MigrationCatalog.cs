using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Fineage.Accounts;

/// <summary>
/// One <c>.sql</c> migration file. The id is the file name, which is also the primary key in
/// <c>schema_migrations</c>; the checksum is over the normalized file contents.
/// </summary>
public sealed record MigrationScript(string Id, string Prefix, string Path, string Checksum, string Sql)
{
    /// <summary>
    /// SHA-256 of the migration text with line endings normalized to LF and a UTF-8 BOM removed.
    /// Line-ending normalization keeps Windows checkouts from reporting a false tamper.
    /// </summary>
    public static string ComputeChecksum(string sql)
    {
        string normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (normalized.Length > 0 && normalized[0] == '\uFEFF')
            normalized = normalized[1..];
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>A migration file/folder invariant violation: bad name, duplicate prefix, missing file.</summary>
public class MigrationException : InvalidOperationException
{
    public MigrationException(string message) : base(message)
    {
    }
}

/// <summary>
/// An applied migration's recorded checksum no longer matches the file on disk. This is always fatal:
/// the database may not match the schema the code expects, and the forward-only policy forbids editing
/// an applied migration in place.
/// </summary>
public sealed class MigrationTamperException : MigrationException
{
    public MigrationTamperException(string id, string recordedChecksum, string fileChecksum, string path)
        : base($"Migration checksum mismatch for '{id}' ({path}): database recorded {recordedChecksum}, " +
               $"file is {fileChecksum}. Applied migrations are immutable under the forward-only policy; " +
               "restore the original file or add a new migration instead of editing an applied one.")
    {
        MigrationId = id;
        RecordedChecksum = recordedChecksum;
        FileChecksum = fileChecksum;
    }

    public string MigrationId { get; }

    public string RecordedChecksum { get; }

    public string FileChecksum { get; }
}

/// <summary>
/// The versioned SQL set shipped with the binaries. Ordered by file name, which keeps the
/// existing numeric prefixes applied in order.
/// </summary>
public sealed class MigrationCatalog
{
    static readonly Regex FileNamePattern = new(
        @"^(?<prefix>\d{3,})_[A-Za-z0-9_]+\.sql$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Historical collisions that are already recorded in live databases. They must keep their
    /// current file names (ids) because renaming an applied migration would break upgrade compatibility.
    /// No new duplicate prefix is allowed.
    /// </summary>
    public static readonly IReadOnlyCollection<string> LegacyDuplicatePrefixes =
        new HashSet<string>(StringComparer.Ordinal) { "018", "020" };

    MigrationCatalog(string directory, IReadOnlyList<MigrationScript> scripts)
    {
        Directory = directory;
        Scripts = scripts;
    }

    public string Directory { get; }

    public IReadOnlyList<MigrationScript> Scripts { get; }

    public static MigrationCatalog Load(string directory, IReadOnlyCollection<string>? legacyDuplicatePrefixes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!System.IO.Directory.Exists(directory))
            throw new MigrationException($"Migration directory not found: {directory}");

        var allowedLegacy = legacyDuplicatePrefixes ?? LegacyDuplicatePrefixes;
        var files = System.IO.Directory.GetFiles(directory, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
            throw new MigrationException($"Migration directory contains no .sql files: {directory}");

        var byPrefix = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            Match match = FileNamePattern.Match(name);
            if (!match.Success)
                throw new MigrationException(
                    $"Migration file '{name}' does not follow NNN_name.sql (3+ digit prefix, lowercase name, .sql).");

            string prefix = match.Groups["prefix"].Value;
            if (!byPrefix.TryGetValue(prefix, out var names))
            {
                names = [];
                byPrefix.Add(prefix, names);
            }

            names.Add(name);
        }

        List<string> duplicates = byPrefix
            .Where(pair => pair.Value.Count > 1 && !allowedLegacy.Contains(pair.Key))
            .Select(pair => $"prefix {pair.Key}: {string.Join(", ", pair.Value)}")
            .ToList();
        if (duplicates.Count > 0)
            throw new MigrationException(
                "Duplicate migration prefixes are not allowed (they make ordering and ids ambiguous): " +
                string.Join("; ", duplicates) +
                ". Use the next free prefix instead of reusing one. Historical collisions " +
                string.Join(", ", allowedLegacy) + " remain allowed for upgrade compatibility.");

        var scripts = new List<MigrationScript>(files.Count);
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            string sql = File.ReadAllText(file);
            scripts.Add(new MigrationScript(
                name,
                FileNamePattern.Match(name).Groups["prefix"].Value,
                file,
                MigrationScript.ComputeChecksum(sql),
                sql));
        }

        return new MigrationCatalog(directory, scripts);
    }

    /// <summary>
    /// Locates the migrations that were copied next to the binaries (publish/dev output), then walks
    /// up to the repository layout so tests and local runs find <c>Persistence/Migrations</c>.
    /// </summary>
    public static string FindApplicationMigrationsDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "Persistence", "Migrations");
            if (System.IO.Directory.Exists(candidate))
                return candidate;
            candidate = Path.Combine(dir, "migrations");
            if (System.IO.Directory.Exists(candidate))
                return candidate;
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }

        string src = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Persistence", "Migrations"));
        if (System.IO.Directory.Exists(src))
            return src;

        throw new DirectoryNotFoundException("Could not locate Persistence/Migrations SQL folder.");
    }
}
