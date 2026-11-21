using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>Mobius <c>Ride</c> mountType: 0 none, 1 strider, 2 wyvern.</summary>
public enum PetMountKind : byte
{
    None = 0,
    Strider = 1,
    Wyvern = 2,
}

/// <summary>One level row of a pet (Mobius <c>stats/pets</c> <c>&lt;stat level&gt;</c>).</summary>
public sealed record PetLevel(
    int Level,
    long Exp,
    int OwnerExpTaken,
    int MaxMeal,
    int MealBattle,
    int MealNormal,
    double Hp,
    double Mp,
    double PAtk,
    double PDef,
    double MAtk,
    double MDef,
    double HpRegen,
    double MpRegen);

public sealed record PetFood(int ItemId, int Feed);

public sealed record PetSkillRow(int MinLevel, int SkillId, int SkillLevel);

/// <summary>A pet: npc, collar item (control item), food, hunger limit, load and its level table.</summary>
public sealed record PetTemplate(
    int NpcId,
    int ItemId,
    int Index,
    int SummonSkillId,
    int SummonCastMs,
    IReadOnlyList<PetFood> Food,
    int HungryLimit,
    int Load,
    bool SyncLevel,
    bool Mountable,
    PetMountKind MountKind,
    IReadOnlyList<PetSkillRow> Skills,
    IReadOnlyList<PetLevel> Levels)
{
    public int MinLevel => Levels[0].Level;

    /// <summary>Mobius <c>PetStat.addLevel</c> keeps the pet one level under the last table row.</summary>
    public int MaxLevel => Math.Max(MinLevel, Levels[^1].Level - 1);

    public PetLevel At(int level)
    {
        int index = Math.Clamp(level, MinLevel, Levels[^1].Level) - MinLevel;
        return Levels[Math.Clamp(index, 0, Levels.Count - 1)];
    }

    /// <summary>Exp at the start of <paramref name="level"/> (Mobius <c>getExpForLevel</c>).</summary>
    public long ExpForLevel(int level) => At(level).Exp;

    public PetFood? FoodFor(int itemId) => Food.FirstOrDefault(f => f.ItemId == itemId);
}

/// <summary>Pets from <c>data/pets.json</c> (tools/import_pets.py over Mobius CT 0 stats/pets).</summary>
public sealed class PetCatalog
{
    public const string FileName = "pets.json";

    readonly Dictionary<int, PetTemplate> _byItem;
    readonly Dictionary<int, PetTemplate> _byNpc;

    PetCatalog(Dictionary<int, PetTemplate> byItem)
    {
        _byItem = byItem;
        _byNpc = byItem.Values.GroupBy(static p => p.NpcId).ToDictionary(static g => g.Key, static g => g.First());
    }

    public static PetCatalog Empty { get; } = new([]);

    public IReadOnlyCollection<PetTemplate> All => _byItem.Values;

    public PetTemplate? ByItem(int itemId) => _byItem.TryGetValue(itemId, out var p) ? p : null;

    public PetTemplate? ByNpc(int npcId) => _byNpc.TryGetValue(npcId, out var p) ? p : null;

    public bool IsCollar(int itemId) => _byItem.ContainsKey(itemId);

    sealed class FileDto
    {
        public List<PetDto>? Pets { get; set; }
    }

    sealed class PetDto
    {
        public int NpcId { get; set; }
        public int ItemId { get; set; }
        public int Index { get; set; }
        public int SummonSkillId { get; set; }
        public int SummonCastMs { get; set; }
        public List<PetFood>? Food { get; set; }
        public int HungryLimit { get; set; }
        public int Load { get; set; }
        public bool SyncLevel { get; set; }
        public bool Mountable { get; set; }
        public byte MountKind { get; set; }
        public List<PetSkillRow>? Skills { get; set; }
        public List<PetLevel>? Levels { get; set; }
    }

    public static PetCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName) ?? throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    public static PetCatalog Parse(string json, string source = FileName)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var file = JsonSerializer.Deserialize<FileDto>(json, options) ?? throw new InvalidDataException($"{source} was empty.");
        var map = new Dictionary<int, PetTemplate>();
        foreach (var dto in file.Pets ?? [])
        {
            if (dto.NpcId <= 0 || dto.ItemId <= 0 || dto.Levels is not { Count: > 1 })
                throw new InvalidDataException($"{source} has a pet without npc, item or levels.");
            var levels = dto.Levels.OrderBy(static l => l.Level).ToArray();
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i].Level != levels[0].Level + i || levels[i].MaxMeal < 0 || levels[i].OwnerExpTaken is < 0 or > 100 ||
                    (i > 0 && levels[i].Exp < levels[i - 1].Exp))
                    throw new InvalidDataException($"{source} pet {dto.NpcId} level row {levels[i].Level} is invalid.");
            }

            bool mountable = dto.Mountable;
            var mountKind = dto.MountKind is > 0 and <= 2 ? (PetMountKind)dto.MountKind
                : mountable ? PetMountKind.Strider : PetMountKind.None;
            var pet = new PetTemplate(dto.NpcId, dto.ItemId, dto.Index, dto.SummonSkillId,
                dto.SummonCastMs > 0 ? dto.SummonCastMs : 5000, dto.Food ?? [], Math.Clamp(dto.HungryLimit, 0, 100),
                Math.Max(0, dto.Load), dto.SyncLevel, mountable, mountKind, dto.Skills ?? [], levels);
            if (!map.TryAdd(pet.ItemId, pet))
                throw new InvalidDataException($"{source} repeats collar item {pet.ItemId}.");
        }

        return new PetCatalog(map);
    }
}
