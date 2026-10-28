using System.Text.Json;

namespace Fineage.Game.Services;

public enum NewbieBuffRefusal
{
    None = 0,
    NotNewbie = 1,
    LevelLow = 2,
    LevelHigh = 3,
    ThirdClass = 4,
    NoneAvailable = 5
}

/// <summary>One supplemental-magic row. Level is inclusive on both ends.</summary>
public sealed record NewbieBuff(
    int SkillId,
    int SkillLevel,
    string Name,
    int MinLevel,
    int MaxLevel,
    int AbnormalLevel,
    int DurationSeconds);

public sealed record BlessingBuff(int SkillId, int SkillLevel, int AbnormalLevel, int DurationSeconds, int MaxLevel, int RefusedClassLevel);

/// <summary>
/// Newbie Helper buff gates. Mobius <c>SupportMagic</c> serves levels 8 through 25.
/// Above 25 and below 8 are refused before the per-buff window. A character whose class
/// level is 3 is refused. Only the first character on an account is a newbie.
/// </summary>
public sealed class NewbieBuffCatalog
{
    public const string FileName = "newbie-buffs.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly HashSet<int> _mages;
    readonly NewbieBuff[] _fighter;
    readonly NewbieBuff[] _mage;

    NewbieBuffCatalog(
        int lowestLevel,
        int highestLevel,
        HashSet<int> mages,
        NewbieBuff[] fighter,
        NewbieBuff[] mage,
        BlessingBuff blessing)
    {
        LowestLevel = lowestLevel;
        HighestLevel = highestLevel;
        _mages = mages;
        _fighter = fighter;
        _mage = mage;
        Blessing = blessing;
    }

    public int LowestLevel { get; }

    public int HighestLevel { get; }

    public BlessingBuff Blessing { get; }

    public bool IsMage(int classId) => _mages.Contains(classId);

    public bool CanBless(int level, int classLevel) =>
        level <= Blessing.MaxLevel && classLevel < Blessing.RefusedClassLevel;

    public NewbieBuffRefusal Evaluate(bool newbie, int classId, int classLevel, int level, out IReadOnlyList<NewbieBuff> buffs)
    {
        buffs = [];
        if (!newbie)
            return NewbieBuffRefusal.NotNewbie;
        if (level > HighestLevel)
            return NewbieBuffRefusal.LevelHigh;
        if (level < LowestLevel)
            return NewbieBuffRefusal.LevelLow;
        if (classLevel == 3)
            return NewbieBuffRefusal.ThirdClass;

        var set = IsMage(classId) ? _mage : _fighter;
        var chosen = new List<NewbieBuff>();
        foreach (var buff in set)
        {
            if (level >= buff.MinLevel && level <= buff.MaxLevel)
                chosen.Add(buff);
        }

        buffs = chosen;
        return chosen.Count == 0 ? NewbieBuffRefusal.NoneAvailable : NewbieBuffRefusal.None;
    }

    public static NewbieBuffCatalog Parse(string json, string source)
    {
        NewbieFileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<NewbieFileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not a newbie buff catalog.", ex);
        }

        if (file == null || file.LowestLevel < 1 || file.HighestLevel < file.LowestLevel)
            throw new InvalidDataException($"{source} has an invalid level window.");
        if (file.Blessing == null || file.Blessing.SkillId <= 0 || file.Blessing.DurationSeconds < 1)
            throw new InvalidDataException($"{source} needs a blessing.");

        return new NewbieBuffCatalog(
            file.LowestLevel,
            file.HighestLevel,
            new HashSet<int>(file.MageClassIds ?? []),
            ReadSet(file.Fighter, source, "fighter"),
            ReadSet(file.Mage, source, "mage"),
            new BlessingBuff(
                file.Blessing.SkillId,
                Math.Max(1, file.Blessing.SkillLevel),
                Math.Max(1, file.Blessing.AbnormalLevel),
                file.Blessing.DurationSeconds,
                file.Blessing.MaxLevel,
                file.Blessing.RefusedClassLevel));
    }

    public static NewbieBuffCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        if (path == null)
            throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    static NewbieBuff[] ReadSet(List<BuffDto>? rows, string source, string set)
    {
        if (rows == null || rows.Count == 0)
            throw new InvalidDataException($"{source} {set} set is empty.");
        var buffs = new NewbieBuff[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.SkillId <= 0 || row.MinLevel < 1 || row.MaxLevel < row.MinLevel || row.DurationSeconds < 1)
                throw new InvalidDataException($"{source} {set} buff {row.SkillId} is invalid.");
            buffs[i] = new NewbieBuff(
                row.SkillId,
                Math.Max(1, row.SkillLevel),
                row.Name ?? "",
                row.MinLevel,
                row.MaxLevel,
                Math.Max(1, row.AbnormalLevel),
                row.DurationSeconds);
        }

        return buffs;
    }

    sealed class NewbieFileDto
    {
        public int LowestLevel { get; set; }
        public int HighestLevel { get; set; }
        public List<int>? MageClassIds { get; set; }
        public BlessingDto? Blessing { get; set; }
        public List<BuffDto>? Fighter { get; set; }
        public List<BuffDto>? Mage { get; set; }
    }

    sealed class BlessingDto
    {
        public int SkillId { get; set; }
        public int SkillLevel { get; set; } = 1;
        public int AbnormalLevel { get; set; } = 1;
        public int DurationSeconds { get; set; }
        public int MaxLevel { get; set; } = 39;
        public int RefusedClassLevel { get; set; } = 2;
    }

    sealed class BuffDto
    {
        public int SkillId { get; set; }
        public int SkillLevel { get; set; } = 1;
        public string? Name { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int AbnormalLevel { get; set; } = 1;
        public int DurationSeconds { get; set; }
    }
}
