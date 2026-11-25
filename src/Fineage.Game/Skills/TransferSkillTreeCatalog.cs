using System.Text.Json;

namespace Fineage.Game.Skills;

/// <summary>Mobius <c>transferSkillTree.xml</c>: third-class healer cross-path skills bought with Holy Pomanders.</summary>
public sealed class TransferSkillTreeCatalog
{
    public const string FileName = "skill-transfer-trees.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, TransferClassTree> _byClass;
    readonly Dictionary<int, int> _certificateClass;

    TransferSkillTreeCatalog(
        IReadOnlyList<TransferClassTree> trees,
        IReadOnlyDictionary<int, int> certificateClass)
    {
        Trees = trees;
        _byClass = new Dictionary<int, TransferClassTree>(trees.Count);
        foreach (var tree in trees)
            _byClass.Add(tree.ClassId, tree);
        _certificateClass = new Dictionary<int, int>(certificateClass);
    }

    public IReadOnlyList<TransferClassTree> Trees { get; }

    public TransferClassTree? FindClass(int classId) => _byClass.TryGetValue(classId, out var tree) ? tree : null;

    public bool TryCertificateClass(int itemId, out int classId) =>
        _certificateClass.TryGetValue(itemId, out classId);

    public bool IsKnownCertificate(int itemId) => _certificateClass.ContainsKey(itemId);

    public static TransferSkillTreeCatalog Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Transfer skill datapack not found: {path}", path);
        try
        {
            return Parse(File.ReadAllText(path), path);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }
    }

    public static TransferSkillTreeCatalog Empty { get; } = Parse("{\"trees\":[],\"certificates\":[]}", "empty");

    public static TransferSkillTreeCatalog Parse(string json, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        string where = source ?? FileName;
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"{where} was empty.");

        var certMap = new Dictionary<int, int>();
        foreach (var row in file.Certificates ?? [])
        {
            if (row.ItemId <= 0 || row.ClassId < 0)
                throw new InvalidDataException($"{where} has a bad certificate row.");
            certMap[row.ItemId] = row.ClassId;
        }

        var trees = new List<TransferClassTree>();
        var seenClass = new HashSet<int>();
        foreach (var tree in file.Trees ?? [])
        {
            if (tree.ClassId < 0)
                throw new InvalidDataException($"{where} has a negative class id.");
            if (!seenClass.Add(tree.ClassId))
                throw new InvalidDataException($"{where} repeats class id {tree.ClassId}.");

            var entries = new List<TransferSkillEntry>();
            var seenSkill = new HashSet<(int, int)>();
            foreach (var skill in tree.Skills ?? [])
            {
                if (skill.SkillId <= 0 || skill.Level <= 0 || skill.GetLevel < 1)
                    throw new InvalidDataException($"{where} class {tree.ClassId} has an invalid skill row.");
                if (skill.CertificateItemId <= 0 || skill.Count <= 0)
                    throw new InvalidDataException($"{where} class {tree.ClassId} skill {skill.SkillId} has no certificate.");
                if (!seenSkill.Add((skill.SkillId, skill.Level)))
                    continue;
                entries.Add(new TransferSkillEntry(
                    skill.SkillId,
                    skill.Level,
                    skill.GetLevel,
                    skill.CertificateItemId,
                    skill.Count));
                certMap.TryAdd(skill.CertificateItemId, tree.ClassId);
            }

            trees.Add(new TransferClassTree(tree.ClassId, entries));
        }

        return new TransferSkillTreeCatalog(trees, certMap);
    }

    sealed class FileDto
    {
        public List<CertificateDto>? Certificates { get; set; }
        public List<TreeDto>? Trees { get; set; }
    }

    sealed class CertificateDto
    {
        public int ItemId { get; set; }
        public int ClassId { get; set; }
    }

    sealed class TreeDto
    {
        public int ClassId { get; set; }
        public List<SkillDto>? Skills { get; set; }
    }

    sealed class SkillDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public int GetLevel { get; set; }
        public int CertificateItemId { get; set; }
        public int Count { get; set; } = 1;
    }
}

public sealed class TransferClassTree
{
    public TransferClassTree(int classId, IReadOnlyList<TransferSkillEntry> skills)
    {
        ClassId = classId;
        Skills = skills;
    }

    public int ClassId { get; }

    public IReadOnlyList<TransferSkillEntry> Skills { get; }
}

public readonly record struct TransferSkillEntry(
    int SkillId,
    int Level,
    int GetLevel,
    int CertificateItemId,
    int Count = 1);