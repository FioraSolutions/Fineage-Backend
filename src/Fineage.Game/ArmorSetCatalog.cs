using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Game;

/// <summary>One skill granted while an armor set is complete (Mobius ArmorSetListener).</summary>
public readonly record struct ArmorSetSkill(int Id, int Level);

/// <summary>One Mobius armor set: chest + optional parts; skills apply while the set is complete.</summary>
public sealed record ArmorSetDefinition(
    int Id,
    int Chest,
    IReadOnlyList<int> Legs,
    IReadOnlyList<int> Head,
    IReadOnlyList<int> Gloves,
    IReadOnlyList<int> Feet,
    IReadOnlyList<int> Shield,
    IReadOnlyList<ArmorSetSkill> Skills,
    IReadOnlyList<ArmorSetSkill> ShieldSkills,
    IReadOnlyList<ArmorSetSkill> Enchant6Skills);

/// <summary>
/// Mobius <c>stats/armorsets</c> catalog (data/armor-sets.json).
/// When every required piece is worn, the set skills fold like passives into combat mods.
/// XML STR/DEX fields are intentionally unused — Mobius parses them but never applies them.
/// </summary>
public sealed class ArmorSetCatalog
{
    public const string FileName = "armor-sets.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, ArmorSetDefinition> _byChest;

    ArmorSetCatalog(Dictionary<int, ArmorSetDefinition> byChest) => _byChest = byChest;

    public static ArmorSetCatalog Empty { get; } = new([]);

    public int Count => _byChest.Count;

    public bool TryGetByChest(int chestItemId, out ArmorSetDefinition set) =>
        _byChest.TryGetValue(chestItemId, out set!);

    /// <summary>
    /// Collects skill id → level for every complete set the player is wearing.
    /// Shield skills need the matching shield; enchant6 skills need every equipped set piece at +6+.
    /// </summary>
    public Dictionary<int, int> ActiveSkills(
        int? chestItemId,
        int? legsItemId,
        int? headItemId,
        int? glovesItemId,
        int? feetItemId,
        int? shieldItemId,
        Func<int, int> enchantOf)
    {
        var skills = new Dictionary<int, int>();
        if (chestItemId is not int chestId || !_byChest.TryGetValue(chestId, out var set))
            return skills;
        if (!PartOk(set.Legs, legsItemId) || !PartOk(set.Head, headItemId)
            || !PartOk(set.Gloves, glovesItemId) || !PartOk(set.Feet, feetItemId))
            return skills;

        AddSkills(skills, set.Skills);
        if (PartOk(set.Shield, shieldItemId) && set.Shield.Count > 0)
            AddSkills(skills, set.ShieldSkills);

        if (set.Enchant6Skills.Count > 0 && Enchant6Complete(set, chestId, legsItemId, headItemId, glovesItemId, feetItemId, enchantOf))
            AddSkills(skills, set.Enchant6Skills);

        return skills;
    }

    static bool PartOk(IReadOnlyList<int> allowed, int? worn)
    {
        if (allowed.Count == 0)
            return true;
        return worn is int id && allowed.Contains(id);
    }

    static bool Enchant6Complete(
        ArmorSetDefinition set,
        int chestId,
        int? legsItemId,
        int? headItemId,
        int? glovesItemId,
        int? feetItemId,
        Func<int, int> enchantOf)
    {
        if (enchantOf(chestId) < 6)
            return false;
        if (set.Legs.Count > 0 && (legsItemId is not int legs || enchantOf(legs) < 6))
            return false;
        if (set.Head.Count > 0 && (headItemId is not int head || enchantOf(head) < 6))
            return false;
        if (set.Gloves.Count > 0 && (glovesItemId is not int gloves || enchantOf(gloves) < 6))
            return false;
        if (set.Feet.Count > 0 && (feetItemId is not int feet || enchantOf(feet) < 6))
            return false;
        return true;
    }

    static void AddSkills(Dictionary<int, int> skills, IReadOnlyList<ArmorSetSkill> list)
    {
        foreach (var s in list)
        {
            if (s.Id <= 0)
                continue;
            int level = Math.Max(1, s.Level);
            if (!skills.TryGetValue(s.Id, out int cur) || level > cur)
                skills[s.Id] = level;
        }
    }

    public static ArmorSetCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path), path);
    }

    public static ArmorSetCatalog Parse(string json, string source = FileName)
    {
        FileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not an armor-set table.", ex);
        }

        var byChest = new Dictionary<int, ArmorSetDefinition>();
        foreach (var row in file?.Sets ?? [])
        {
            if (row.Chest <= 0)
                throw new InvalidDataException($"{source} has a set without a chest item.");
            byChest[row.Chest] = new ArmorSetDefinition(
                row.Id,
                row.Chest,
                row.Legs ?? [],
                row.Head ?? [],
                row.Gloves ?? [],
                row.Feet ?? [],
                row.Shield ?? [],
                MapSkills(row.Skills),
                MapSkills(row.ShieldSkills),
                MapSkills(row.Enchant6Skills));
        }
        return new ArmorSetCatalog(byChest);
    }

    static ArmorSetSkill[] MapSkills(List<SkillDto>? rows) =>
        (rows ?? []).Where(s => s.Id > 0).Select(s => new ArmorSetSkill(s.Id, Math.Max(1, s.Level))).ToArray();

    sealed class FileDto
    {
        public List<SetDto>? Sets { get; set; }
    }

    sealed class SetDto
    {
        public int Id { get; set; }
        public int Chest { get; set; }
        public List<int>? Legs { get; set; }
        public List<int>? Head { get; set; }
        public List<int>? Gloves { get; set; }
        public List<int>? Feet { get; set; }
        public List<int>? Shield { get; set; }
        public List<SkillDto>? Skills { get; set; }
        public List<SkillDto>? ShieldSkills { get; set; }
        public List<SkillDto>? Enchant6Skills { get; set; }
    }

    sealed class SkillDto
    {
        public int Id { get; set; }
        public int Level { get; set; } = 1;
    }
}
