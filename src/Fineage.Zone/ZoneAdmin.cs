using System.Text.Json;
using Fineage.Game;

namespace Fineage.Zone;

/// <summary>
/// Debug admin gate: <c>FINEAGE_ZONE_ADMIN</c> must be on, and the character name must be in
/// <c>data/admin-characters.json</c> (default: KuK). A lone <c>*</c> entry allows any name
/// (local/dev). Used for Mobius-style <c>admin_*</c> bypasses.
/// </summary>
public static class ZoneAdmin
{
    public const string EnvVar = "FINEAGE_ZONE_ADMIN";
    public const string CharactersFileName = "admin-characters.json";
    public const string AnyCharacter = "*";

    static readonly object Gate = new();
    static HashSet<string>? _names;
    static string? _loadedFrom;

    public static bool Enabled => IsEnabled(Environment.GetEnvironmentVariable(EnvVar));

    public static bool IsEnabled(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var token = text.Trim();
        return token == "1"
            || token.Equals("true", StringComparison.OrdinalIgnoreCase)
            || token.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when env admin is on and <paramref name="characterName"/> is allowlisted (or <c>*</c>).</summary>
    public static bool IsAdminCharacter(string? characterName)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(characterName))
            return false;
        EnsureNamesLoaded();
        if (_names!.Contains(AnyCharacter))
            return true;
        return _names.Contains(characterName.Trim());
    }

    /// <summary>Test seam: replace allowlist (null restores file load).</summary>
    internal static void SetNamesForTest(IEnumerable<string>? names)
    {
        lock (Gate)
        {
            if (names == null)
            {
                _names = null;
                _loadedFrom = null;
                return;
            }

            _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    _names.Add(name.Trim());
            }

            _loadedFrom = "<test>";
        }
    }

    static void EnsureNamesLoaded()
    {
        lock (Gate)
        {
            if (_names != null)
                return;

            _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "KuK" };
            string? dataDir = GameData.FindDataDirectory(AppContext.BaseDirectory)
                ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory());
            if (dataDir == null)
                return;

            string path = Path.Combine(dataDir, CharactersFileName);
            if (!File.Exists(path))
                return;

            try
            {
                var dto = JsonSerializer.Deserialize<FileDto>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });
                if (dto?.Names == null || dto.Names.Count == 0)
                    return;

                var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in dto.Names)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        loaded.Add(name.Trim());
                }

                if (loaded.Count > 0)
                {
                    _names = loaded;
                    _loadedFrom = path;
                }
            }
            catch
            {
                // Keep default KuK allowlist.
            }
        }
    }

    sealed class FileDto
    {
        public List<string>? Names { get; set; }
    }
}
