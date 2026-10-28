using System.Text;

namespace Fineage.Zone.NpcHtml;

/// <summary>
/// HTML dialog files under <c>data/html</c>. Keys are relative paths with forward slashes,
/// such as <c>merchant/30001.htm</c>. Lookup ignores case.
/// </summary>
public sealed class NpcHtmlCatalog
{
    public const string DirectoryName = "html";

    static readonly object CacheGate = new();
    static string? _cacheKey;
    static NpcHtmlCatalog? _cache;
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    readonly Dictionary<string, string> _files;

    NpcHtmlCatalog(Dictionary<string, string> files)
    {
        _files = files;
    }

    public static NpcHtmlCatalog Empty { get; } = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public int Count => _files.Count;

    public bool Contains(string relative) => _files.ContainsKey(Normalize(relative));

    public bool TryGet(string relative, out string html) => _files.TryGetValue(Normalize(relative), out html!);

    public static NpcHtmlCatalog FromDocuments(IReadOnlyDictionary<string, string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in documents)
        {
            var key = Normalize(pair.Key);
            if (key.Length == 0 || key.Contains("..", StringComparison.Ordinal))
                throw new ArgumentException($"HTML key '{pair.Key}' is not a relative path.", nameof(documents));
            files[key] = pair.Value ?? "";
        }

        return new NpcHtmlCatalog(files);
    }

    public static NpcHtmlCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(Path.Combine(dataDirectory, DirectoryName));
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

    public static NpcHtmlCatalog LoadDefault()
    {
        var directory = Spawns.GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? Spawns.GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
            return Empty;
        return Load(directory);
    }

    static NpcHtmlCatalog Read(string root)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!IsHtml(path))
                continue;
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            files[relative] = Decode(File.ReadAllBytes(path));
        }

        return new NpcHtmlCatalog(files);
    }

    static string Stamp(string root)
    {
        int count = 0;
        long ticks = 0;
        foreach (string path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!IsHtml(path))
                continue;
            count++;
            long write = File.GetLastWriteTimeUtc(path).Ticks;
            if (write > ticks)
                ticks = write;
        }

        return root + "|" + count + "|" + ticks;
    }

    static bool IsHtml(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".htm", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".html", StringComparison.OrdinalIgnoreCase);
    }

    static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes[3..];
        try
        {
            return Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    static string Normalize(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return "";
        return relative.Trim().Replace('\\', '/').TrimStart('/');
    }
}
