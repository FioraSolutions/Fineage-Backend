using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Manor;

/// <summary>
/// One castle seed (Mobius <c>Seeds.xml</c> crop row): the seed sown in that castle's territory, the crop it bears,
/// the mature crop the castle gets, the two reward items a crop trades for, its level and period limits.
/// Reference prices are the seed / crop item prices.
/// </summary>
public sealed record ManorSeed(
    int SeedId,
    int CropId,
    int MatureId,
    int Reward1,
    int Reward2,
    int CastleId,
    bool Alternative,
    int Level,
    int LimitSeed,
    int LimitCrop,
    int SeedPrice,
    int CropPrice)
{
    public int Reward(int type) => type == 1 ? Reward1 : Reward2;

    /// <summary>Mobius <c>Seed.getSeedMinPrice</c> / <c>getSeedMaxPrice</c>: 60% .. 1000% of the reference.</summary>
    public int SeedMinPrice => (int)(SeedPrice * 0.6);
    public int SeedMaxPrice => SeedPrice * 10;
    public int CropMinPrice => (int)(CropPrice * 0.6);
    public int CropMaxPrice => CropPrice * 10;
}

/// <summary>data/manor.json (tools/import_manor.py): seeds per castle, manor manager npcs, sowable monsters.</summary>
public sealed class ManorData
{
    public const string FileName = "manor.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    static readonly Lazy<ManorData> Shared = new(Load);

    readonly Dictionary<int, ManorSeed> _bySeed = [];
    readonly Dictionary<int, int> _managerCastle = [];
    readonly HashSet<int> _sowable = [];

    public ManorData(IEnumerable<ManorSeed> seeds, IEnumerable<(int NpcId, int CastleId)> managers, IEnumerable<int> sowable)
    {
        foreach (var seed in seeds)
            _bySeed[seed.SeedId] = seed;
        foreach (var (npcId, castleId) in managers)
            _managerCastle[npcId] = castleId;
        _sowable.UnionWith(sowable);
    }

    public static readonly ManorData Empty = new([], [], []);

    public static ManorData Default => Shared.Value;

    public IEnumerable<ManorSeed> Seeds => _bySeed.Values;

    public int SowableCount => _sowable.Count;

    public ManorSeed? Seed(int seedId) => _bySeed.TryGetValue(seedId, out var seed) ? seed : null;

    public IEnumerable<ManorSeed> SeedsFor(int castleId) =>
        _bySeed.Values.Where(s => s.CastleId == castleId).OrderBy(s => s.Level).ThenBy(s => s.Alternative).ThenBy(s => s.SeedId);

    /// <summary>Mobius <c>getSeedByCrop(cropId, castleId)</c>.</summary>
    public ManorSeed? SeedByCrop(int cropId, int castleId) =>
        _bySeed.Values.FirstOrDefault(s => s.CropId == cropId && s.CastleId == castleId);

    /// <summary>Mobius <c>getSeedByCrop(cropId)</c>: any castle (mature id and rewards are per crop).</summary>
    public ManorSeed? SeedByCrop(int cropId) => _bySeed.Values.FirstOrDefault(s => s.CropId == cropId);

    public bool IsCrop(int itemId) => _bySeed.Values.Any(s => s.CropId == itemId);

    /// <summary>One row per distinct crop (Mobius <c>getCrops</c>).</summary>
    public IEnumerable<ManorSeed> Crops() => _bySeed.Values.GroupBy(s => s.CropId).Select(g => g.First()).OrderBy(s => s.Level).ThenBy(s => s.CropId);

    /// <summary>Castle whose seeds the manor manager sells (Mobius npc parameter <c>manor_id</c>), or 0.</summary>
    public int ManagerCastle(int npcId) => _managerCastle.TryGetValue(npcId, out int id) ? id : 0;

    public IEnumerable<int> ManagerNpcIds => _managerCastle.Keys;

    /// <summary>Mobius npc <c>status canBeSown</c>.</summary>
    public bool CanBeSown(int npcId) => _sowable.Contains(npcId);

    static ManorData Load()
    {
        string? path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path));
    }

    public static ManorData Parse(string json)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions) ?? new FileDto();
        var seeds = (file.Seeds ?? []).Where(s => s.SeedId > 0 && s.CropId > 0 && s.CastleId > 0)
            .Select(s => new ManorSeed(s.SeedId, s.CropId, s.MatureId, s.Reward1, s.Reward2, s.CastleId, s.Alternative, s.Level,
                s.LimitSeed, s.LimitCrop, Math.Max(1, s.SeedPrice), Math.Max(1, s.CropPrice)));
        var managers = (file.Managers ?? []).Where(m => m.NpcId > 0 && m.CastleId > 0).Select(m => (m.NpcId, m.CastleId));
        return new ManorData(seeds, managers, file.SowableNpcIds ?? []);
    }

    sealed class FileDto
    {
        public List<SeedDto>? Seeds { get; set; }
        public List<ManagerDto>? Managers { get; set; }
        public int[]? SowableNpcIds { get; set; }
    }

    sealed class SeedDto
    {
        public int SeedId { get; set; }
        public int CropId { get; set; }
        public int MatureId { get; set; }
        public int Reward1 { get; set; }
        public int Reward2 { get; set; }
        public int CastleId { get; set; }
        public bool Alternative { get; set; }
        public int Level { get; set; }
        public int LimitSeed { get; set; }
        public int LimitCrop { get; set; }
        public int SeedPrice { get; set; }
        public int CropPrice { get; set; }
    }

    sealed class ManagerDto
    {
        public int NpcId { get; set; }
        public int CastleId { get; set; }
    }
}
