using System.Text;

namespace Fineage.Zone.CommunityBoard;

/// <summary>
/// HTML under <c>data/html/CommunityBoard</c>. Keys are relative paths with forward slashes
/// (e.g. <c>home.html</c>). Lookup ignores case.
/// </summary>
public sealed class CommunityBoardCatalog
{
    public const string DirectoryName = "CommunityBoard";

    static readonly object CacheGate = new();
    static string? _cacheKey;
    static CommunityBoardCatalog? _cache;
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    readonly Dictionary<string, string> _files;

    CommunityBoardCatalog(Dictionary<string, string> files) => _files = files;

    public static CommunityBoardCatalog Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public int Count => _files.Count;

    public bool TryGet(string relative, out string html) =>
        _files.TryGetValue(Normalize(relative), out html!);

    public static CommunityBoardCatalog FromDocuments(IReadOnlyDictionary<string, string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in documents)
        {
            var key = Normalize(pair.Key);
            if (key.Length == 0 || key.Contains("..", StringComparison.Ordinal))
                throw new ArgumentException($"Community board key '{pair.Key}' is not a relative path.", nameof(documents));
            files[key] = pair.Value ?? "";
        }

        return new CommunityBoardCatalog(files);
    }

    public static CommunityBoardCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(Path.Combine(dataDirectory, "html", DirectoryName));
        if (!Directory.Exists(root))
            return Empty;

        var key = Stamp(root);
        lock (CacheGate)
        {
            if (_cache != null && _cacheKey == key)
                return _cache;
            var loaded = Read(root);
            _cache = loaded;
            _cacheKey = key;
            return loaded;
        }
    }

    public static CommunityBoardCatalog LoadDefault()
    {
        var directory = Fineage.Zone.Spawns.GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? Fineage.Zone.Spawns.GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        return directory == null ? Empty : Load(directory);
    }

    static CommunityBoardCatalog Read(string root)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(path);
            if (!ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
                continue;
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            files[Normalize(relative)] = Utf8.GetString(File.ReadAllBytes(path));
        }

        return new CommunityBoardCatalog(files);
    }

    static string Normalize(string relative) =>
        (relative ?? "").Trim().Replace('\\', '/').TrimStart('/');

    static string Stamp(string root)
    {
        try
        {
            var newest = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            return root + "|" + newest.Ticks;
        }
        catch
        {
            return root + "|missing";
        }
    }
}
