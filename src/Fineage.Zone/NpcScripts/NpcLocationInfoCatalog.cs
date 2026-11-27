using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.NpcScripts;

/// <summary>Mobius <c>custom/NpcLocationInfo</c> guide + radar NPC id tables.</summary>
public sealed class NpcLocationInfoCatalog
{
    public const string FileName = "npc-location-info.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    readonly HashSet<int> _guides;
    readonly HashSet<int> _radar;

    NpcLocationInfoCatalog(HashSet<int> guides, HashSet<int> radar)
    {
        _guides = guides;
        _radar = radar;
    }

    public bool IsGuide(int npcId) => _guides.Contains(npcId);
    public bool IsRadarTarget(int npcId) => _radar.Contains(npcId);

    public static NpcLocationInfoCatalog LoadDefault()
    {
        string? path = TeleportCatalog.FindFile(FileName);
        if (path == null)
            throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    public static NpcLocationInfoCatalog Parse(string json, string source = "inline")
    {
        var dto = JsonSerializer.Deserialize<FileDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"{source}: empty npc-location-info");
        var guides = new HashSet<int>(dto.GuideNpcIds ?? []);
        var radar = new HashSet<int>(dto.RadarNpcIds ?? []);
        if (guides.Count == 0)
            throw new InvalidDataException($"{source}: guideNpcIds missing");
        if (radar.Count == 0)
            throw new InvalidDataException($"{source}: radarNpcIds missing");
        return new NpcLocationInfoCatalog(guides, radar);
    }

    sealed class FileDto
    {
        public List<int>? GuideNpcIds { get; set; }
        public List<int>? RadarNpcIds { get; set; }
    }
}
