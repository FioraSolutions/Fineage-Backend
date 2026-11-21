using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Fineage.Game.L10n;

/// <summary>
/// One lookup for every player-facing string: key + locale, then <see cref="PlayerLocale.Default"/>.
/// A miss in the requested locale logs a warning that names the key and the locale, then serves English.
/// A miss in English too logs an error that names the key and returns the key.
/// HTML, system strings, and names are the same table. Locales are folders under <c>data/l10n</c>.
/// </summary>
public sealed class LocalizationCatalog
{
    readonly IReadOnlyDictionary<string, Dictionary<string, string>> _tables;
    readonly ILocalizationLog _log;

    LocalizationCatalog(IReadOnlyDictionary<string, Dictionary<string, string>> tables, ILocalizationLog log)
    {
        _tables = tables;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public static LocalizationCatalog Empty(ILocalizationLog log) =>
        new(new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal), log);

    public static LocalizationCatalog FromTables(
        ILocalizationLog log,
        params (string Locale, IReadOnlyDictionary<string, string> Entries)[] tables)
    {
        ArgumentNullException.ThrowIfNull(log);
        var built = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (locale, entries) in tables)
        {
            if (!PlayerLocale.TryNormalize(locale, out string tag))
                throw new ArgumentException($"'{locale}' is not a locale tag.", nameof(tables));
            var table = Table(built, tag);
            foreach (var pair in entries)
                table[pair.Key] = pair.Value ?? "";
        }

        return new LocalizationCatalog(built, log);
    }

    /// <summary>
    /// Loads <c>data/l10n/&lt;locale&gt;/</c>. Shipped English NPC HTML stays in <c>data/html</c>
    /// and is read by <c>NpcHtmlCatalog</c>. A file under <c>data/l10n/en/html</c> overrides that tree
    /// when the HTML reader asks this catalog first.
    /// </summary>
    public static LocalizationCatalog Load(string dataDirectory, ILocalizationLog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(log);
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        string root = Path.GetFullPath(dataDirectory);
        string l10nRoot = Path.Combine(root, "l10n");
        if (Directory.Exists(l10nRoot))
        {
            foreach (string dir in Directory.EnumerateDirectories(l10nRoot))
            {
                string folder = Path.GetFileName(dir);
                if (!PlayerLocale.TryNormalize(folder, out string locale))
                    continue;
                if (!folder.Equals(locale, StringComparison.OrdinalIgnoreCase))
                    continue;

                var table = Table(tables, locale);
                LoadStringFile(table, Path.Combine(dir, "strings.json"));
                LoadHtmlTree(table, Path.Combine(dir, "html"), "html", overwrite: true);
                LoadNames(table, Path.Combine(dir, "names"));
            }
        }

        return new LocalizationCatalog(tables, log);
    }

    public bool TryExact(string? locale, string key, out string value)
    {
        value = "";
        if (string.IsNullOrEmpty(key) || !PlayerLocale.TryNormalize(locale, out string tag))
            return false;
        return _tables.TryGetValue(tag, out var table) && table.TryGetValue(key, out value!);
    }

    /// <summary>
    /// The string for <paramref name="key"/> in <paramref name="locale"/>.
    /// Missing in that locale: warning, then English. Missing in English too: error, and the key itself.
    /// </summary>
    public string Get(string key, string? locale) => Resolve(key, locale, english: null);

    /// <summary>
    /// Same as <see cref="Get"/>, but <paramref name="english"/> is the shipped English text when the
    /// catalog has no <c>en</c> row yet (a datapack name, or HTML already read from the English file).
    /// A non-English miss still logs the fallback. English is not logged as missing when this text is present.
    /// </summary>
    public string Resolve(string key, string? locale, string? english)
    {
        if (string.IsNullOrEmpty(key))
        {
            _log.Missing(key ?? "");
            return english ?? "";
        }

        string tag = PlayerLocale.Normalize(locale);
        if (TryExact(tag, key, out string hit))
            return hit;

        if (!PlayerLocale.IsDefault(tag))
        {
            _log.Fallback(key, tag);
            if (TryExact(PlayerLocale.Default, key, out string fallback))
                return fallback;
            if (!string.IsNullOrEmpty(english))
                return english;
        }
        else if (!string.IsNullOrEmpty(english))
        {
            return english;
        }

        _log.Missing(key);
        return key;
    }

    public string Format(string key, string? locale, params object?[] args)
    {
        string template = Get(key, locale);
        if (args == null || args.Length == 0)
            return template;
        return string.Format(CultureInfo.InvariantCulture, template, args);
    }

    static Dictionary<string, string> Table(Dictionary<string, Dictionary<string, string>> tables, string locale)
    {
        if (!tables.TryGetValue(locale, out var table))
        {
            table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            tables[locale] = table;
        }

        return table;
    }

    static void LoadStringFile(Dictionary<string, string> table, string path)
    {
        if (!File.Exists(path))
            return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{path} must be a JSON object of key to string.");
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"{path} key '{property.Name}' must be a string.");
            table[property.Name] = property.Value.GetString() ?? "";
        }
    }

    static void LoadNames(Dictionary<string, string> table, string directory)
    {
        if (!Directory.Exists(directory))
            return;
        LoadNameFile(table, Path.Combine(directory, "items.json"), "item");
        LoadNameFile(table, Path.Combine(directory, "skills.json"), "skill");
        LoadNameFile(table, Path.Combine(directory, "npcs.json"), "npc");
    }

    static void LoadNameFile(Dictionary<string, string> table, string path, string kind)
    {
        if (!File.Exists(path))
            return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{path} must be a JSON object of id to name.");
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"{path} key '{property.Name}' must be a string.");
            if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
                throw new InvalidDataException($"{path} key '{property.Name}' must be a positive id.");
            table[$"name.{kind}.{id}"] = property.Value.GetString() ?? "";
        }
    }

    static void LoadHtmlTree(Dictionary<string, string> table, string directory, string keyPrefix, bool overwrite)
    {
        if (!Directory.Exists(directory))
            return;
        foreach (string path in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (!IsHtml(path))
                continue;
            string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (relative.Contains("..", StringComparison.Ordinal))
                continue;
            string key = keyPrefix.TrimEnd('/') + "/" + relative;
            if (!overwrite && table.ContainsKey(key))
                continue;
            table[key] = Decode(File.ReadAllBytes(path));
        }
    }

    /// <summary>UTF-8, or Latin-1 when the file is not UTF-8. Same rule as the NPC HTML catalog.</summary>
    static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes[3..];
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    static bool IsHtml(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".htm", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".html", StringComparison.OrdinalIgnoreCase);
    }
}
