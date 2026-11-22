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

    public static IReadOnlyList<string> RequiredRegionNames(bool required)
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
        {
            if (required)
            {
                throw new InvalidOperationException(
                    $"[geo] {RequiredVariable}=1 but data/{RegionsManifestFile} was not found and {RequiredRegionsVariable} is unset.");
            }

            ZoneLog.Warn($"[geo] data/{RegionsManifestFile} was not found; region list is not checked.");
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            if (doc.RootElement.TryGetProperty("regions", out var regions) && regions.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var el in regions.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.String || el.GetString() is not { Length: > 0 } name)
                        throw new InvalidDataException($"[geo] {manifest} regions must be non-empty strings.");
                    list.Add(name.Trim());
                }

                if (list.Count == 0)
                    throw new InvalidDataException($"[geo] {manifest} regions is empty.");
                return list;
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"[geo] could not read {manifest}: {ex.Message}", ex);
        }

        throw new InvalidDataException($"[geo] {manifest} needs a regions array.");
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
            _ => throw new InvalidOperationException(
                $"{name} must be 1/0, true/false, yes/no, or on/off."),
        };
    }
}