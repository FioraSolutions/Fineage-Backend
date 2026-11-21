using System.Text.Json;

namespace Fineage.Zone.Geo;

/// <summary>
/// Geodata directory and production-mode guard (<c>FINEAGE_GEODATA_REQUIRED</c>).
/// </summary>
public static class GeoSettings
{
    public const string DirVariable = "FINEAGE_GEODATA_DIR";
    public const string RequiredVariable = "FINEAGE_GEODATA_REQUIRED";
    public const string RequiredRegionsVariable = "FINEAGE_GEODATA_REQUIRED_REGIONS";
    public const string RegionsManifestFile = "geodata-regions.json";

    public static bool Required => ReadBool(RequiredVariable, defaultValue: false);

    public static IReadOnlyList<string> RequiredRegionNames()
    {
        string? text = Environment.GetEnvironmentVariable(RequiredRegionsVariable);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static r => r.Length > 0)
                .ToArray();
        }

        string? manifest = FindRegionsManifestPath();
        if (manifest == null)
            return [];

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            if (doc.RootElement.TryGetProperty("regions", out var regions) && regions.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var el in regions.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is { } name && name.Length > 0)
                        list.Add(name.Trim());
                }

                return list;
            }
        }
        catch (Exception ex)
        {
            ZoneLog.Warn($"[geo] could not read {manifest}: {ex.Message}");
        }

        return [];
    }

    static string? FindRegionsManifestPath()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(Path.GetFullPath(start)); d != null; d = d.Parent)
            {
                string p = Path.Combine(d.FullName, "data", RegionsManifestFile);
                if (File.Exists(p))
                    return p;
            }
        }

        return null;
    }

    static bool ReadBool(string name, bool defaultValue)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;
        return text.Trim() switch
        {
            "1" or "true" or "TRUE" or "yes" or "YES" or "on" or "ON" => true,
            "0" or "false" or "FALSE" or "no" or "NO" or "off" or "OFF" => false,
            _ => defaultValue,
        };
    }
}