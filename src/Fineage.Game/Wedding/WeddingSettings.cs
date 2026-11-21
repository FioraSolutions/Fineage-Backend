using System.Text.Json;

namespace Fineage.Game.Wedding;

/// <summary>Mobius <c>config/Custom/Wedding.ini</c> loaded from <c>data/wedding.json</c>.</summary>
public sealed class WeddingSettings
{
    public const string FileName = "wedding.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public bool AllowWedding { get; init; } = false;
    public int ManagerNpcId { get; init; } = 50007;
    public int FormalWearItemId { get; init; } = 6408;
    public long WeddingPrice { get; init; } = 250_000_000;
    public bool PunishInfidelity { get; init; } = true;
    public bool TeleportEnabled { get; init; } = true;
    public long TeleportPrice { get; init; } = 50_000;
    public int TeleportDurationSeconds { get; init; } = 60;
    public bool AllowSameSex { get; init; }
    public bool RequireFormalWear { get; init; } = true;
    public int DivorceCostPercent { get; init; } = 20;
    public int WeddingMarchSkillId { get; init; } = 2230;
    public int LargeFireworkSkillId { get; init; } = 2025;

    public static WeddingSettings Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
            return new WeddingSettings();
        var json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<WeddingSettings>(json, JsonOptions);
        if (loaded == null)
            throw new InvalidOperationException($"Failed to parse {path}");
        if (loaded.DivorceCostPercent is < 0 or > 100)
            throw new InvalidOperationException($"wedding.json divorceCostPercent must be 0..100, got {loaded.DivorceCostPercent}");
        if (loaded.TeleportDurationSeconds < 0)
            throw new InvalidOperationException($"wedding.json teleportDurationSeconds must be >= 0");
        return loaded;
    }

    public static WeddingSettings LoadDefault()
    {
        var directory = Fineage.Game.GameData.FindDataDirectory(AppContext.BaseDirectory)
            ?? Fineage.Game.GameData.FindDataDirectory(Directory.GetCurrentDirectory());
        return directory == null ? new WeddingSettings() : Load(directory);
    }
}