using System.Text.Json;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.CursedWeapons;

/// <summary>Mobius <c>data/CursedWeapons.xml</c> as <c>data/cursed-weapons.json</c>.</summary>
public sealed class CursedWeaponsCatalog
{
    public const string FileName = "cursed-weapons.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    CursedWeaponsCatalog(IReadOnlyDictionary<int, CursedWeaponDefinition> byItemId)
    {
        ByItemId = byItemId;
        Blades = byItemId.Values.OrderBy(b => b.ItemId).ToList();
    }

    public IReadOnlyDictionary<int, CursedWeaponDefinition> ByItemId { get; }
    public IReadOnlyList<CursedWeaponDefinition> Blades { get; }

    public bool TryGet(int itemId, out CursedWeaponDefinition definition) =>
        ByItemId.TryGetValue(itemId, out definition!);

    public static CursedWeaponsCatalog Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Cursed weapons catalog not found: {path}", path);
        return Read(path);
    }

    public static CursedWeaponsCatalog LoadDefault()
    {
        var directory = GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
            throw new InvalidOperationException("Could not locate data directory for cursed weapons.");
        return Load(directory);
    }

    public static CursedWeaponsCatalog From(IReadOnlyList<CursedWeaponDefinition> blades)
    {
        var byId = new Dictionary<int, CursedWeaponDefinition>(blades.Count);
        foreach (var blade in blades)
        {
            if (!byId.TryAdd(blade.ItemId, blade))
                throw new InvalidDataException($"Duplicate cursed weapon item id {blade.ItemId}.");
        }

        return new CursedWeaponsCatalog(byId);
    }

    static CursedWeaponsCatalog Read(string path)
    {
        using var stream = File.OpenRead(path);
        var dto = JsonSerializer.Deserialize<CursedWeaponsFileDto>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Empty cursed weapons file: {path}");
        if (dto.Blades == null || dto.Blades.Count == 0)
            throw new InvalidDataException("Cursed weapons catalog needs at least one blade.");
        var blades = new List<CursedWeaponDefinition>(dto.Blades.Count);
        foreach (var row in dto.Blades)
        {
            if (row.ItemId <= 0 || row.SkillId <= 0)
                throw new InvalidDataException($"Invalid cursed weapon row item={row.ItemId} skill={row.SkillId}.");
            blades.Add(new CursedWeaponDefinition(
                row.ItemId,
                row.Name ?? "",
                row.SkillId,
                row.TransformId,
                row.DisappearChance,
                row.DropRate,
                row.DurationMinutes,
                row.DurationLostMinutes,
                row.StageKills));
        }

        return From(blades);
    }

    sealed class CursedWeaponsFileDto
    {
        public List<CursedWeaponRowDto>? Blades { get; set; }
    }

    sealed class CursedWeaponRowDto
    {
        public int ItemId { get; set; }
        public string? Name { get; set; }
        public int SkillId { get; set; }
        public int TransformId { get; set; }
        public int DisappearChance { get; set; }
        public int DropRate { get; set; }
        public int DurationMinutes { get; set; }
        public int DurationLostMinutes { get; set; }
        public int StageKills { get; set; }
    }
}

public sealed record CursedWeaponDefinition(
    int ItemId,
    string Name,
    int SkillId,
    int TransformId,
    int DisappearChance,
    int DropRate,
    int DurationMinutes,
    int DurationLostMinutes,
    int StageKills);