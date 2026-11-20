using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>One GM <c>//town</c> destination from <c>data/gm-teleports.json</c> (Mobius gatekeeper coords).</summary>
public sealed record GmTownTeleport(string PrimaryKey, string DisplayName, int X, int Y, int Z);

/// <summary>Named town teleports for GM chat (<c>//town</c> / <c>//towns</c>).</summary>
public sealed class GmTeleportCatalog
{
    public const string FileName = "gm-teleports.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<string, GmTownTeleport> _byKey;

    GmTeleportCatalog(Dictionary<string, GmTownTeleport> byKey, IReadOnlyList<GmTownTeleport> towns)
    {
        _byKey = byKey;
        Towns = towns;
    }

    public IReadOnlyList<GmTownTeleport> Towns { get; }

    public static GmTeleportCatalog LoadDefault() => LoadFile(FileName);

    public static GmTeleportCatalog LoadFile(string fileName)
    {
        string? dataDir = GameData.FindDataDirectory(AppContext.BaseDirectory)
            ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory());
        if (dataDir == null)
            return Empty;
        string path = Path.Combine(dataDir, fileName);
        if (!File.Exists(path))
            return Empty;
        return Parse(File.ReadAllText(path), path);
    }

    public static GmTeleportCatalog Parse(string json, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        TeleportFileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<TeleportFileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not a GM teleport catalog.", ex);
        }

        var byKey = new Dictionary<string, GmTownTeleport>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<GmTownTeleport>();
        if (file?.Towns != null)
        {
            foreach (var row in file.Towns)
            {
                if (row.Keys == null || row.Keys.Count == 0)
                    continue;
                string primary = row.Keys[0].Trim();
                string display = string.IsNullOrWhiteSpace(row.Name)
                    ? primary
                    : row.Name.Trim();
                var town = new GmTownTeleport(primary, display, row.X, row.Y, row.Z);
                unique.Add(town);
                foreach (string key in row.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                        byKey[key.Trim()] = town;
                }
            }
        }

        return new GmTeleportCatalog(byKey, unique);
    }

    public static GmTeleportCatalog Empty { get; } = new(new Dictionary<string, GmTownTeleport>(), []);

    public GmTownTeleport? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : _byKey.GetValueOrDefault(key.Trim());

    sealed class TeleportFileDto
    {
        public List<TownRowDto>? Towns { get; set; }
    }

    sealed class TownRowDto
    {
        public List<string>? Keys { get; set; }
        public string? Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }
}