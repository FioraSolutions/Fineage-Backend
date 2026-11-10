using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Game;

public sealed partial class ClassCatalog
{
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
}
