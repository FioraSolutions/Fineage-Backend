using System.Text.Json;

namespace Fineage.Game.Skills;

/// <summary>Mobius class skill trees (auto-get grants on enter / level-up).</summary>
public sealed class SkillTreeCatalog
{
    public const string FileName = "skill-trees.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, ClassSkillTree> _byClass;

    SkillTreeCatalog(IReadOnlyList<ClassSkillTree> trees)
    {
        Trees = trees;
        _byClass = new Dictionary<int, ClassSkillTree>(trees.Count);
        foreach (var tree in trees)
            _byClass.Add(tree.ClassId, tree);
    }

    public IReadOnlyList<ClassSkillTree> Trees { get; }

    public ClassSkillTree? FindClass(int classId) => _byClass.TryGetValue(classId, out var tree) ? tree : null;

    public static SkillTreeCatalog Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Skill tree datapack not found: {path}", path);
        try
        {
            return Parse(File.ReadAllText(path), path);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }
    }

    public static SkillTreeCatalog Parse(string json, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        string where = source ?? FileName;
        var file = JsonSerializer.Deserialize<TreeFileDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"{where} was empty.");

        var trees = new List<ClassSkillTree>();
        var seen = new HashSet<int>();
        foreach (var row in file.Trees ?? [])
        {
            if (row.ClassId < 0)
                throw new InvalidDataException($"{where} has a negative class id.");
            if (!seen.Add(row.ClassId))
                throw new InvalidDataException($"{where} repeats class id {row.ClassId}.");

            var entries = new List<SkillTreeEntry>();
            foreach (var skill in row.Skills ?? [])
            {
                if (skill.SkillId <= 0 || skill.Level <= 0 || skill.GetLevel < 1)
                    throw new InvalidDataException($"{where} class {row.ClassId} has an invalid skill row.");
                entries.Add(new SkillTreeEntry(
                    skill.SkillId,
                    skill.Level,
                    skill.GetLevel,
                    skill.AutoGet,
                    skill.LearnedByNpc,
                    Math.Max(0, skill.LevelUpSp),
                    ParseItems(where, row.ClassId, skill),
                    ParsePrerequisites(where, row.ClassId, skill)));
            }

            trees.Add(new ClassSkillTree(row.ClassId, entries));
        }

        return new SkillTreeCatalog(trees);
    }

    static IReadOnlyList<SkillLearnItem>? ParseItems(string where, int classId, EntryDto skill)
    {
        if (skill.RequiredItems is not { Count: > 0 } rows)
            return null;
        var items = new List<SkillLearnItem>(rows.Count);
        foreach (var row in rows)
        {
            if (row.ItemId <= 0 || row.Count <= 0)
                throw new InvalidDataException($"{where} class {classId} skill {skill.SkillId} has a bad required item.");
            items.Add(new SkillLearnItem(row.ItemId, row.Count));
        }

        return items;
    }

    static IReadOnlyList<SkillLearnPrerequisite>? ParsePrerequisites(string where, int classId, EntryDto skill)
    {
        if (skill.PreReqSkills is not { Count: > 0 } rows)
            return null;
        var prereqs = new List<SkillLearnPrerequisite>(rows.Count);
        foreach (var row in rows)
        {
            if (row.SkillId <= 0 || row.Level <= 0)
                throw new InvalidDataException($"{where} class {classId} skill {skill.SkillId} has a bad prerequisite.");
            prereqs.Add(new SkillLearnPrerequisite(row.SkillId, row.Level));
        }

        return prereqs;
    }

    sealed class TreeFileDto
    {
        public List<TreeDto>? Trees { get; set; }
    }

    sealed class TreeDto
    {
        public int ClassId { get; set; }
        public List<EntryDto>? Skills { get; set; }
    }

    sealed class EntryDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public int GetLevel { get; set; }
        public bool AutoGet { get; set; }
        public bool LearnedByNpc { get; set; }
        public int LevelUpSp { get; set; }
        public List<ItemDto>? RequiredItems { get; set; }
        public List<PrereqDto>? PreReqSkills { get; set; }
    }

    sealed class ItemDto
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    sealed class PrereqDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
    }
}

public sealed class ClassSkillTree
{
    public ClassSkillTree(int classId, IReadOnlyList<SkillTreeEntry> skills)
    {
        ClassId = classId;
        Skills = skills;
    }

    public int ClassId { get; }

    public IReadOnlyList<SkillTreeEntry> Skills { get; }
}

/// <summary>One item a Mobius learn row consumes (skill tree <c>&lt;item id count/&gt;</c>).</summary>
public readonly record struct SkillLearnItem(int ItemId, int Count);

/// <summary>One skill that must already be known (skill tree <c>&lt;preRequisiteSkill id lvl/&gt;</c>).</summary>
public readonly record struct SkillLearnPrerequisite(int SkillId, int Level);

public readonly record struct SkillTreeEntry(
    int SkillId,
    int Level,
    int GetLevel,
    bool AutoGet,
    bool LearnedByNpc,
    int LevelUpSp = 0,
    IReadOnlyList<SkillLearnItem>? RequiredItems = null,
    IReadOnlyList<SkillLearnPrerequisite>? PreReqSkills = null)
{
    /// <summary>Mobius <c>AcquireSkillInfo</c> requirement rows: the books a class skill consumes.</summary>
    public IReadOnlyList<SkillLearnItem> Items => RequiredItems ?? [];

    /// <summary>Mobius <c>checkPlayerSkill</c>: every listed prerequisite must already be known at that level.</summary>
    public IReadOnlyList<SkillLearnPrerequisite> Prerequisites => PreReqSkills ?? [];
}
