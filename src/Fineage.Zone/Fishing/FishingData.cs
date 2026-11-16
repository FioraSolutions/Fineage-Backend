using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Fishing;

/// <summary>One fish (Mobius <c>stats/fishing/fishes.xml</c> row). Group / grade are the Mobius ordinals.</summary>
public sealed record FishRow(
    int FishId,
    int ItemId,
    string Name,
    int Group,
    int Grade,
    int Level,
    double Guts,
    int Hp,
    double HpRegen,
    int StartCombatTime,
    int CombatDuration,
    int GutsCheckTime);

/// <summary>Mobius <c>fishingRods.xml</c>: the rod's level drives the grade bonus, its damage the pump/reel base.</summary>
public sealed record FishingRodRow(int ItemId, string Name, int Level, double Damage);

/// <summary>Mobius <c>fishingMonsters.xml</c>: a winning pull spawns this monster at <c>probability</c> percent.</summary>
public sealed record FishingMonsterRow(int MinLevel, int MaxLevel, int NpcId, int Probability);

/// <summary>Fishing guild skill tree row (Mobius <c>fishingSkillTree.xml</c>, 1312–1315).</summary>
public sealed record FishingSkillRow(int SkillId, int Level, int GetLevel, IReadOnlyList<(int ItemId, int Count)> Items);

/// <summary>A Mobius <c>FishingZone</c> polygon.</summary>
public sealed record FishingZone(string Name, int MinZ, int MaxZ, (int X, int Y)[] Nodes, int MinX, int MaxX, int MinY, int MaxY)
{
    /// <summary>Mobius <c>ZoneManager.getZones(x, y)</c>: the polygon test only (the zone's own top Z is used).</summary>
    public bool Contains(int x, int y)
    {
        if (x < MinX || x > MaxX || y < MinY || y > MaxY)
            return false;
        bool inside = false;
        for (int i = 0, j = Nodes.Length - 1; i < Nodes.Length; j = i++)
        {
            var (xi, yi) = Nodes[i];
            var (xj, yj) = Nodes[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (double)(y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }
}

/// <summary>data/fishing.json (tools/import_fishing.py): fish, rods, monsters, skill tree, fishing zones, skill power.</summary>
public sealed class FishingData
{
    public const string FileName = "fishing.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    static readonly Lazy<FishingData> Shared = new(Load);

    readonly Dictionary<int, FishingRodRow> _rods = [];
    readonly List<FishRow> _fish;
    readonly List<FishingMonsterRow> _monsters;
    readonly List<FishingSkillRow> _tree;
    readonly List<FishingZone> _zones;
    readonly double[] _power;

    public FishingData(IEnumerable<FishRow> fish, IEnumerable<FishingRodRow> rods, IEnumerable<FishingMonsterRow> monsters,
        IEnumerable<FishingSkillRow> tree, IEnumerable<FishingZone> zones, IReadOnlyList<double> skillPower)
    {
        _fish = [.. fish];
        foreach (var rod in rods)
            _rods[rod.ItemId] = rod;
        _monsters = [.. monsters];
        _tree = [.. tree];
        _zones = [.. zones];
        _power = [.. skillPower];
    }

    public static readonly FishingData Empty = new([], [], [], [], [], []);

    public static FishingData Default => Shared.Value;

    public IReadOnlyList<FishRow> Fish => _fish;
    public IReadOnlyList<FishingMonsterRow> Monsters => _monsters;
    public IReadOnlyList<FishingSkillRow> SkillTree => _tree;
    public IReadOnlyList<FishingZone> Zones => _zones;
    public IEnumerable<FishingRodRow> Rods => _rods.Values;

    public FishingRodRow? Rod(int itemId) => _rods.TryGetValue(itemId, out var rod) ? rod : null;

    /// <summary>Mobius <c>FishData.getFish(level, group, grade)</c>.</summary>
    public IReadOnlyList<FishRow> FishFor(int level, int group, int grade) =>
        _fish.Where(f => f.Level == level && f.Group == group && f.Grade == grade).ToList();

    /// <summary>Mobius <c>FishingMonstersData.getFishingMonster(level)</c>: the first band holding the level.</summary>
    public FishingMonsterRow? MonsterFor(int playerLevel) =>
        _monsters.FirstOrDefault(m => playerLevel >= m.MinLevel && playerLevel <= m.MaxLevel);

    /// <summary>Pumping / Reeling / Fishing Expertise <c>#power</c> table, 1-based skill level.</summary>
    public double Power(int level) => _power.Length == 0 ? 0 : _power[Math.Clamp(level, 1, _power.Length) - 1];

    public bool InFishingZone(int x, int y) => _zones.Any(z => z.Contains(x, y));

    static FishingData Load()
    {
        string? path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path));
    }

    public static FishingData Parse(string json)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions) ?? new FileDto();
        var fish = (file.Fish ?? []).Where(f => f.ItemId > 0)
            .Select(f => new FishRow(f.FishId, f.ItemId, f.Name ?? "", f.Group, f.Grade, f.Level, f.Guts, f.Hp, f.HpRegen,
                f.StartCombatTime, f.CombatDuration, f.GutsCheckTime));
        var rods = (file.Rods ?? []).Where(r => r.ItemId > 0).Select(r => new FishingRodRow(r.ItemId, r.Name ?? "", r.Level, r.Damage));
        var monsters = (file.Monsters ?? []).Where(m => m.NpcId > 0)
            .Select(m => new FishingMonsterRow(m.MinLevel, m.MaxLevel, m.NpcId, m.Probability));
        var tree = (file.SkillTree ?? []).Where(r => r.SkillId > 0 && r.Level > 0)
            .Select(r => new FishingSkillRow(r.SkillId, r.Level, r.GetLevel,
                (r.Items ?? []).Where(i => i.ItemId > 0 && i.Count > 0).Select(i => (i.ItemId, i.Count)).ToList()));
        var zones = new List<FishingZone>();
        foreach (var z in file.Zones ?? [])
        {
            var nodes = (z.Nodes ?? []).Where(n => n.Length >= 2).Select(n => (n[0], n[1])).ToArray();
            if (nodes.Length < 3)
                continue;
            zones.Add(new FishingZone(z.Name ?? "", z.MinZ, z.MaxZ, nodes, nodes.Min(n => n.Item1), nodes.Max(n => n.Item1),
                nodes.Min(n => n.Item2), nodes.Max(n => n.Item2)));
        }

        return new FishingData(fish, rods, monsters, tree, zones, file.SkillPower ?? []);
    }

    sealed class FileDto
    {
        public double[]? SkillPower { get; set; }
        public List<RodDto>? Rods { get; set; }
        public List<MonsterDto>? Monsters { get; set; }
        public List<TreeDto>? SkillTree { get; set; }
        public List<ZoneDto>? Zones { get; set; }
        public List<FishDto>? Fish { get; set; }
    }

    sealed class RodDto
    {
        public int ItemId { get; set; }
        public string? Name { get; set; }
        public int Level { get; set; }
        public double Damage { get; set; }
    }

    sealed class MonsterDto
    {
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int NpcId { get; set; }
        public int Probability { get; set; }
    }

    sealed class TreeDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public int GetLevel { get; set; }
        public List<ItemDto>? Items { get; set; }
    }

    sealed class ItemDto
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    sealed class ZoneDto
    {
        public string? Name { get; set; }
        public int MinZ { get; set; }
        public int MaxZ { get; set; }
        public int[][]? Nodes { get; set; }
    }

    sealed class FishDto
    {
        public int FishId { get; set; }
        public int ItemId { get; set; }
        public string? Name { get; set; }
        public int Group { get; set; }
        public int Grade { get; set; }
        public int Level { get; set; }
        public double Guts { get; set; }
        public int Hp { get; set; }
        public double HpRegen { get; set; }
        public int StartCombatTime { get; set; }
        public int CombatDuration { get; set; }
        public int GutsCheckTime { get; set; }
    }
}
