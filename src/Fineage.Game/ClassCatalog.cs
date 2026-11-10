using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Game;

public sealed partial class ClassCatalog
{
    public const string ClassesFileName = "classes.json";
    public const string ExperienceFileName = "experience.json";
    public const string StatBonusesFileName = "stat-bonuses.json";
    public const int LevelCount = 80;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, ClassTemplate> _byId;

    ClassCatalog(
        IReadOnlyList<ClassTemplate> all,
        Dictionary<int, ClassTemplate> byId,
        ExperienceTable experience,
        StatBonusTable statBonuses,
        IReadOnlyList<int> baseClassIds,
        IReadOnlyList<int> baseRaces)
    {
        All = all;
        _byId = byId;
        Experience = experience;
        StatBonuses = statBonuses;
        BaseClassIds = baseClassIds;
        BaseRaces = baseRaces;
        BaseClassIdSet = new HashSet<int>(baseClassIds);
        BaseRaceSet = new HashSet<int>(baseRaces);
    }

    public IReadOnlyList<ClassTemplate> All { get; }

    public ExperienceTable Experience { get; }

    public StatBonusTable StatBonuses { get; }

    /// <summary>Root classes, sorted by id.</summary>
    public IReadOnlyList<int> BaseClassIds { get; }

    public IReadOnlySet<int> BaseClassIdSet { get; }

    /// <summary>Races that have a root class, sorted.</summary>
    public IReadOnlyList<int> BaseRaces { get; }

    public IReadOnlySet<int> BaseRaceSet { get; }

    public bool TryGet(int classId, out ClassTemplate? template)
    {
        if (_byId.TryGetValue(classId, out var found))
        {
            template = found;
            return true;
        }

        template = null;
        return false;
    }

    public ClassTemplate Get(int classId) =>
        TryGet(classId, out var template) && template != null
            ? template
            : throw new ArgumentOutOfRangeException(nameof(classId), classId, "Unknown class id.");

    /// <summary>Root class first, this class last.</summary>
    public IReadOnlyList<int> ParentChain(int classId)
    {
        var chain = new List<int>();
        var seen = new HashSet<int>();
        int current = classId;
        while (true)
        {
            if (!seen.Add(current))
                throw new InvalidOperationException($"Class {classId} parent chain loops.");
            chain.Add(current);
            int parent = Get(current).ParentClassId;
            if (parent < 0)
                break;
            current = parent;
        }

        chain.Reverse();
        return chain;
    }

    public int ModifiedHp(int classId, int level) => Get(classId).ModifiedHp(level);

    public int ModifiedMp(int classId, int level) => Get(classId).ModifiedMp(level);

    public int ModifiedCp(int classId, int level) => Get(classId).ModifiedCp(level);

    public MapPoint Preview(int classId) => Get(classId).Spawns[0];

    public MapPoint Pick(int classId, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var points = Get(classId).Spawns;
        return points[random.Next(points.Count)];
    }

    /// <summary>Every id must be a base class in this catalog.</summary>
    public void EnsureCreatable(IReadOnlyCollection<int> classIds)
    {
        ArgumentNullException.ThrowIfNull(classIds);
        foreach (int classId in classIds)
        {
            if (!TryGet(classId, out var template) || template == null || !template.IsBase)
                throw new InvalidDataException($"Class {classId} cannot be created. Only a base class from the datapack is allowed.");
        }
    }

    /// <summary>True when <paramref name="classIds"/> is exactly the base-class set.</summary>
    public bool CreationListMatchesBaseClasses(IReadOnlySet<int> classIds)
    {
        ArgumentNullException.ThrowIfNull(classIds);
        return classIds.Count == BaseClassIdSet.Count && BaseClassIdSet.All(classIds.Contains);
    }
}
