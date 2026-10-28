using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Game;

public enum StatSlot
{
    Str,
    Dex,
    Con,
    Int,
    Wit,
    Men
}

/// <summary>STR/DEX/CON/INT/WIT/MEN multipliers. Index is the stat value, 0 through 99.</summary>
public sealed class StatBonusTable
{
    readonly double[] _str;
    readonly double[] _dex;
    readonly double[] _con;
    readonly double[] _int;
    readonly double[] _wit;
    readonly double[] _men;

    internal StatBonusTable(double[] str, double[] dex, double[] con, double[] intel, double[] wit, double[] men)
    {
        _str = str;
        _dex = dex;
        _con = con;
        _int = intel;
        _wit = wit;
        _men = men;
    }

    public double Bonus(StatSlot stat, int value)
    {
        var table = stat switch
        {
            StatSlot.Str => _str,
            StatSlot.Dex => _dex,
            StatSlot.Con => _con,
            StatSlot.Int => _int,
            StatSlot.Wit => _wit,
            StatSlot.Men => _men,
            _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "Unknown stat.")
        };
        if (value < 0 || value >= table.Length)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Stat value is outside the bonus table.");
        return table[value];
    }
}

/// <summary>Experience required to be each level. <see cref="ExpToBeLevel"/> is cumulative.</summary>
public sealed class ExperienceTable
{
    readonly long[] _toLevel;

    internal ExperienceTable(int maxLevel, int maxPetLevel, long[] toLevel)
    {
        MaxLevel = maxLevel;
        MaxPetLevel = maxPetLevel;
        _toLevel = toLevel;
    }

    public int MaxLevel { get; }

    public int MaxPetLevel { get; }

    public int HighestRow => _toLevel.Length - 1;

    public long ExpToBeLevel(int level)
    {
        if (level < 1 || level >= _toLevel.Length)
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Level must be 1..{HighestRow}.");
        return _toLevel[level];
    }

    /// <summary>
    /// Adds experience and SP. Level is the highest row whose cumulative exp is still &lt;= the new total,
    /// capped at <see cref="MaxLevel"/>. Exp does not grow past the max-level threshold.
    /// </summary>
    public LevelProgress Add(long exp, int level, int sp, long addExp, int addSp)
    {
        if (addExp < 0)
            throw new ArgumentOutOfRangeException(nameof(addExp));
        if (addSp < 0)
            throw new ArgumentOutOfRangeException(nameof(addSp));

        long nextExp = exp < 0 ? 0 : exp;
        if (addExp > 0)
            nextExp = addExp > long.MaxValue - nextExp ? long.MaxValue : nextExp + addExp;

        int cap = Math.Min(MaxLevel, HighestRow);
        if (cap >= 1 && nextExp > _toLevel[cap])
            nextExp = _toLevel[cap];

        int nextLevel = level < 1 ? 1 : level;
        if (addExp > 0)
        {
            nextLevel = 1;
            for (int candidate = 2; candidate <= cap; candidate++)
            {
                if (nextExp < _toLevel[candidate])
                    break;
                nextLevel = candidate;
            }
        }

        int nextSp = sp < 0 ? 0 : sp;
        if (addSp > 0)
            nextSp = addSp > int.MaxValue - nextSp ? int.MaxValue : nextSp + addSp;

        return new LevelProgress(nextExp, nextLevel, nextSp);
    }
}

/// <summary>Experience, level, and SP after a quest reward.</summary>
public readonly record struct LevelProgress(long Exp, int Level, int Sp);

/// <summary>
/// Interlude classes, experience, and stat bonuses loaded from the datapack.
/// Query by class id. Creation is limited to classes whose parent is absent.
/// </summary>
public sealed class ClassCatalog
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

    public static ClassCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var classesPath = Path.Combine(dataDirectory, ClassesFileName);
        var experiencePath = Path.Combine(dataDirectory, ExperienceFileName);
        var bonusPath = Path.Combine(dataDirectory, StatBonusesFileName);
        var file = Read<ClassFileDto>(classesPath);
        var experience = ReadExperience(experiencePath);
        var bonuses = ReadBonuses(bonusPath);
        if (file.Classes == null || file.Classes.Count == 0)
            throw new InvalidDataException($"{classesPath} has no classes.");

        var built = new List<ClassTemplate>(file.Classes.Count);
        var byId = new Dictionary<int, ClassTemplate>(file.Classes.Count);
        foreach (var row in file.Classes)
        {
            var template = Build(row, classesPath, bonuses);
            if (!byId.TryAdd(template.ClassId, template))
                throw new InvalidDataException($"{classesPath} repeats class {template.ClassId}.");
            built.Add(template);
        }

        built.Sort((a, b) => a.ClassId.CompareTo(b.ClassId));
        foreach (var template in built)
        {
            if (template.ParentClassId >= 0 && !byId.ContainsKey(template.ParentClassId))
                throw new InvalidDataException($"{classesPath} class {template.ClassId} parent {template.ParentClassId} is missing.");
            template.Finish(Depth(template.ClassId, byId, classesPath), bonuses.Bonus(StatSlot.Con, template.Con), bonuses.Bonus(StatSlot.Men, template.Men));
        }

        var baseIds = new List<int>();
        var races = new HashSet<int>();
        foreach (var template in built)
        {
            if (!template.IsBase)
                continue;
            baseIds.Add(template.ClassId);
            races.Add(template.Race);
        }

        if (baseIds.Count == 0)
            throw new InvalidDataException($"{classesPath} has no base class.");

        var raceList = races.ToList();
        raceList.Sort();
        return new ClassCatalog(built, byId, experience, bonuses, baseIds, raceList);
    }

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

    static int Depth(int classId, Dictionary<int, ClassTemplate> byId, string path)
    {
        int depth = 0;
        int current = classId;
        var seen = new HashSet<int>();
        while (byId[current].ParentClassId >= 0)
        {
            if (!seen.Add(current))
                throw new InvalidDataException($"{path} class {classId} parent chain loops.");
            current = byId[current].ParentClassId;
            depth++;
            if (depth > 8)
                throw new InvalidDataException($"{path} class {classId} parent chain is too long.");
        }

        return depth;
    }

    static ClassTemplate Build(ClassDto row, string path, StatBonusTable bonuses)
    {
        if (row.ClassId < 0)
            throw new InvalidDataException($"{path} has a negative class id.");
        if (string.IsNullOrWhiteSpace(row.Name))
            throw new InvalidDataException($"{path} class {row.ClassId} needs a name.");
        if (row.Race is < 0 or > 4)
            throw new InvalidDataException($"{path} class {row.ClassId} has race {row.Race}.");
        RequireStat(path, row.ClassId, "str", row.Str);
        RequireStat(path, row.ClassId, "dex", row.Dex);
        RequireStat(path, row.ClassId, "con", row.Con);
        RequireStat(path, row.ClassId, "int", row.IntStat);
        RequireStat(path, row.ClassId, "wit", row.Wit);
        RequireStat(path, row.ClassId, "men", row.Men);
        if (row.PAtk <= 0 || row.MAtk <= 0 || row.PDef <= 0 || row.MDef <= 0)
            throw new InvalidDataException($"{path} class {row.ClassId} has a non-positive attack or defense.");
        if (row.Crit <= 0 || row.PAtkSpd <= 0 || row.MAtkSpd <= 0 || row.RunSpeed <= 0 || row.WalkSpeed <= 0)
            throw new InvalidDataException($"{path} class {row.ClassId} has a non-positive speed or crit.");
        int accuracy = (int)(Math.Sqrt(row.Dex) * 6d + 1d);
        if (row.Accuracy != accuracy)
            throw new InvalidDataException($"{path} class {row.ClassId} accuracy {row.Accuracy} does not match sqrt(DEX)*6+1 ({accuracy}).");

        var hp = RequireLevels(row.Hp, path, row.ClassId, "hp");
        var mp = RequireLevels(row.Mp, path, row.ClassId, "mp");
        var cp = RequireLevels(row.Cp, path, row.ClassId, "cp");
        var male = RequireCollision(row.CollisionMale, path, row.ClassId, "male");
        var female = RequireCollision(row.CollisionFemale, path, row.ClassId, "female");
        if (row.Spawns == null || row.Spawns.Count == 0)
            throw new InvalidDataException($"{path} class {row.ClassId} has no spawn points.");
        var spawns = new MapPoint[row.Spawns.Count];
        for (int i = 0; i < row.Spawns.Count; i++)
            spawns[i] = new MapPoint(row.Spawns[i].X, row.Spawns[i].Y, row.Spawns[i].Z);

        // Touch the bonus tables so a class stat outside 0..99 fails at load.
        bonuses.Bonus(StatSlot.Con, row.Con);
        bonuses.Bonus(StatSlot.Men, row.Men);

        return new ClassTemplate(
            row.ClassId,
            row.Name.Trim(),
            row.ParentClassId,
            row.Race,
            row.Str,
            row.Dex,
            row.Con,
            row.IntStat,
            row.Wit,
            row.Men,
            row.PAtk,
            row.MAtk,
            row.PDef,
            row.MDef,
            row.Accuracy,
            row.Crit,
            row.PAtkSpd,
            row.MAtkSpd,
            row.RunSpeed,
            row.WalkSpeed,
            male,
            female,
            spawns,
            hp,
            mp,
            cp);
    }

    static void RequireStat(string path, int classId, string name, int value)
    {
        if (value is < 1 or > 99)
            throw new InvalidDataException($"{path} class {classId} {name} {value} is outside 1..99.");
    }

    static double[] RequireLevels(List<double>? values, string path, int classId, string name)
    {
        if (values == null || values.Count != LevelCount)
            throw new InvalidDataException($"{path} class {classId} {name} must have {LevelCount} levels.");
        var table = new double[LevelCount];
        for (int i = 0; i < LevelCount; i++)
        {
            if (values[i] <= 0)
                throw new InvalidDataException($"{path} class {classId} {name} level {i + 1} is not positive.");
            table[i] = values[i];
        }

        return table;
    }

    static CollisionBox RequireCollision(CollisionDto? box, string path, int classId, string sex)
    {
        if (box == null || box.Radius <= 0 || box.Height <= 0)
            throw new InvalidDataException($"{path} class {classId} needs a positive {sex} collision.");
        return new CollisionBox(box.Radius, box.Height);
    }

    static ExperienceTable ReadExperience(string path)
    {
        var file = Read<ExperienceFileDto>(path);
        if (file.MaxLevel < 1)
            throw new InvalidDataException($"{path} maxLevel must be positive.");
        if (file.Levels == null || file.Levels.Count == 0)
            throw new InvalidDataException($"{path} has no levels.");

        int highest = file.Levels.Count;
        var toLevel = new long[highest + 1];
        for (int i = 0; i < file.Levels.Count; i++)
        {
            var row = file.Levels[i];
            int expected = i + 1;
            if (row.Level != expected)
                throw new InvalidDataException($"{path} level {row.Level} is out of order. Expected {expected}.");
            if (i > 0 && row.ToLevel <= file.Levels[i - 1].ToLevel)
                throw new InvalidDataException($"{path} experience does not increase at level {row.Level}.");
            if (i == 0 && row.ToLevel != 0)
                throw new InvalidDataException($"{path} level 1 experience must be 0.");
            toLevel[row.Level] = row.ToLevel;
        }

        if (highest < file.MaxLevel)
            throw new InvalidDataException($"{path} stops at level {highest}, below maxLevel {file.MaxLevel}.");
        return new ExperienceTable(file.MaxLevel, file.MaxPetLevel, toLevel);
    }

    static StatBonusTable ReadBonuses(string path)
    {
        var file = Read<BonusFileDto>(path);
        return new StatBonusTable(
            Column(file.Str, path, "str"),
            Column(file.Dex, path, "dex"),
            Column(file.Con, path, "con"),
            Column(file.IntStat, path, "int"),
            Column(file.Wit, path, "wit"),
            Column(file.Men, path, "men"));
    }

    static double[] Column(List<double>? values, string path, string name)
    {
        if (values == null || values.Count != 100)
            throw new InvalidDataException($"{path} {name} must list values 0 through 99.");
        var table = new double[100];
        for (int i = 0; i < 100; i++)
        {
            if (values[i] <= 0)
                throw new InvalidDataException($"{path} {name} value {i} is not positive.");
            table[i] = values[i];
        }

        return table;
    }

    static T Read<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Game data file not found: {path}", path);
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidDataException($"{path} was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }
    }

    sealed class ClassFileDto
    {
        public List<ClassDto>? Classes { get; set; }
    }

    sealed class ClassDto
    {
        public int ClassId { get; set; }
        public string? Name { get; set; }
        public int ParentClassId { get; set; } = -1;
        public int Race { get; set; }
        public int Str { get; set; }
        public int Dex { get; set; }
        public int Con { get; set; }
        [JsonPropertyName("int")] public int IntStat { get; set; }
        public int Wit { get; set; }
        public int Men { get; set; }
        public int PAtk { get; set; }
        public int MAtk { get; set; }
        public int PDef { get; set; }
        public int MDef { get; set; }
        public int Accuracy { get; set; }
        public int Crit { get; set; }
        public int PAtkSpd { get; set; }
        public int MAtkSpd { get; set; }
        public int RunSpeed { get; set; }
        public int WalkSpeed { get; set; }
        public CollisionDto? CollisionMale { get; set; }
        public CollisionDto? CollisionFemale { get; set; }
        public List<SpawnDto>? Spawns { get; set; }
        public List<double>? Hp { get; set; }
        public List<double>? Mp { get; set; }
        public List<double>? Cp { get; set; }
    }

    sealed class CollisionDto
    {
        public double Radius { get; set; }
        public double Height { get; set; }
    }

    sealed class SpawnDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    sealed class ExperienceFileDto
    {
        public int MaxLevel { get; set; }
        public int MaxPetLevel { get; set; }
        public List<ExperienceRowDto>? Levels { get; set; }
    }

    sealed class ExperienceRowDto
    {
        public int Level { get; set; }
        public long ToLevel { get; set; }
    }

    sealed class BonusFileDto
    {
        public List<double>? Str { get; set; }
        public List<double>? Dex { get; set; }
        public List<double>? Con { get; set; }
        [JsonPropertyName("int")] public List<double>? IntStat { get; set; }
        public List<double>? Wit { get; set; }
        public List<double>? Men { get; set; }
    }
}
