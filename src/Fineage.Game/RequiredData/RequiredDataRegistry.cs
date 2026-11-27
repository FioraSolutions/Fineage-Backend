using System.Collections.Frozen;
using System.Text.Json;

namespace Fineage.Game.RequiredData;

/// <summary>
/// Versioned list of game-data kinds that must fail loudly when missing.
/// Source of truth: <c>data/required-data-registry.json</c>.
/// </summary>
public sealed class RequiredDataRegistry
{
    public const string FileName = "required-data-registry.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly FrozenDictionary<RequiredDataKind, RequiredDataEntry> _byKind;

    RequiredDataRegistry(IReadOnlyList<RequiredDataEntry> entries)
    {
        Entries = entries;
        _byKind = entries.ToFrozenDictionary(static e => e.Kind);
    }

    public int Version { get; private init; }

    public IReadOnlyList<RequiredDataEntry> Entries { get; }

    public RequiredDataEntry Get(RequiredDataKind kind) =>
        _byKind.TryGetValue(kind, out var entry)
            ? entry
            : throw new InvalidOperationException($"required-data registry has no entry for kind {kind}.");

    public static RequiredDataRegistry LoadDefault()
    {
        string path = FindFile(FileName)
            ?? throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Load(path);
    }

    public static RequiredDataRegistry Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required-data registry not found: {path}", path);
        return Parse(File.ReadAllText(path), path);
    }

    public static RequiredDataRegistry Parse(string json, string source = FileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        RegistryDto? file;
        try
        {
            file = JsonSerializer.Deserialize<RegistryDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not a required-data registry.", ex);
        }

        if (file?.Kinds is null || file.Kinds.Count == 0)
            throw new InvalidDataException($"{source} has no kinds.");

        var entries = new List<RequiredDataEntry>(file.Kinds.Count);
        var seen = new HashSet<RequiredDataKind>();
        foreach (var row in file.Kinds)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
                throw new InvalidDataException($"{source} has a kind without id.");
            if (string.IsNullOrWhiteSpace(row.File))
                throw new InvalidDataException($"{source} kind '{row.Id}' is missing file.");
            if (string.IsNullOrWhiteSpace(row.KeyName))
                throw new InvalidDataException($"{source} kind '{row.Id}' is missing keyName.");
            if (string.IsNullOrWhiteSpace(row.Policy))
                throw new InvalidDataException($"{source} kind '{row.Id}' is missing policy.");

            var kind = RequiredDataKindIds.Parse(row.Id.Trim());
            if (!seen.Add(kind))
                throw new InvalidDataException($"{source} repeats kind '{row.Id}'.");

            var policy = row.Policy.Trim() switch
            {
                "fail-fast-boot" => RequiredDataPolicy.FailFastBoot,
                "refuse-action" => RequiredDataPolicy.RefuseAction,
                _ => throw new InvalidDataException(
                    $"{source} kind '{row.Id}' has unknown policy '{row.Policy}' (use fail-fast-boot or refuse-action).")
            };

            entries.Add(new RequiredDataEntry(
                kind,
                row.Id.Trim(),
                row.File.Trim(),
                policy,
                row.KeyName.Trim(),
                row.Note?.Trim() ?? ""));
        }

        // Every RequiredDataKind must appear so new enum members cannot ship without a registry row.
        foreach (RequiredDataKind kind in Enum.GetValues<RequiredDataKind>())
        {
            if (!seen.Contains(kind))
                throw new InvalidDataException(
                    $"{source} is missing kind '{kind.ToId()}'; every RequiredDataKind must be listed.");
        }

        return new RequiredDataRegistry(entries) { Version = file.Version };
    }

    /// <summary>
    /// Fail-fast boot: each <see cref="RequiredDataPolicy.FailFastBoot"/> entry's file/dir must exist under
    /// <paramref name="dataDir"/>. Missing packs throw naming the kind and path.
    /// </summary>
    public void AssertBootPacksPresent(string dataDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);
        foreach (var entry in Entries)
        {
            if (entry.Policy != RequiredDataPolicy.FailFastBoot)
                continue;

            string path = Path.Combine(dataDir, entry.File.TrimEnd('/', '\\'));
            bool ok = entry.File.EndsWith('/') || entry.File.EndsWith('\\')
                ? Directory.Exists(path)
                : File.Exists(path);
            if (!ok)
            {
                throw new RequiredDataException(
                    entry.Kind,
                    entry.KeyName,
                    entry.File,
                    $"fail-fast boot pack missing at {path}");
            }
        }
    }

    public static string? FindFile(string fileName)
    {
        foreach (string? candidate in CandidateDataDirs())
        {
            if (candidate is null)
                continue;
            string path = Path.Combine(candidate, fileName);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    static IEnumerable<string?> CandidateDataDirs()
    {
        yield return Environment.GetEnvironmentVariable("FINEAGE_DATA_DIR");
        yield return Path.Combine(AppContext.BaseDirectory, "data");
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            yield return Path.Combine(dir, "data");
            dir = Directory.GetParent(dir)?.FullName;
        }
    }

    sealed class RegistryDto
    {
        public int Version { get; set; }
        public List<KindDto>? Kinds { get; set; }
    }

    sealed class KindDto
    {
        public string? Id { get; set; }
        public string? File { get; set; }
        public string? Policy { get; set; }
        public string? KeyName { get; set; }
        public string? Note { get; set; }
    }
}
