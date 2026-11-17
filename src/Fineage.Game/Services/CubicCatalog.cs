using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>One action skill a cubic may use (Mobius <c>CubicSkill</c>).</summary>
public sealed record CubicSkillRef(int SkillId, int Level, int TriggerRate = 0, bool Priority = false);

/// <summary>A cubic template (Mobius <c>stats/cubics</c> <c>&lt;cubic id level&gt;</c>).</summary>
public sealed record CubicTemplate(
    int Id,
    int Level,
    int DurationSec,
    int DelaySec,
    int MaxCount,
    bool UseUp,
    double Power,
    string TargetType,
    string Selection,
    IReadOnlyList<CubicSkillRef> Skills)
{
    public bool Heals => TargetType.Equals("HEAL", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One level of a SummonCubic skill: cubic power override and the crystals the cast spends.</summary>
public sealed record CubicSummonLevel(int Level, double Power, int ItemId, int ItemCount, int CubicLevel);

/// <summary>A skill whose effect is Mobius <c>SummonCubic</c>.</summary>
public sealed record CubicSummonSkill(
    int SkillId,
    string Name,
    int CubicId,
    bool Party,
    int DelaySec,
    int DurationSec,
    int MaxCount,
    int Chance,
    IReadOnlyList<CubicSummonLevel> Levels)
{
    public CubicSummonLevel? At(int level) =>
        Levels.Count == 0 ? null : Levels[Math.Clamp(level, 1, Levels.Count) - 1];
}

public sealed record CubicActionStat(string Stat, string Op, double Value);

public sealed record CubicActionLevel(int Level, double Power, int MagicLevel, int AbnormalLevel, double Dot = 0, double Heal = 0);

/// <summary>A cubic action skill (Cubic DD, Drain, Heal, Poison, debuffs, Paralysis, Shock, Hate, Hold).</summary>
public sealed record CubicAction(
    int SkillId,
    string Name,
    string Kind,
    bool Debuff,
    string AbnormalType,
    int AbnormalTimeSec,
    int ActivateRate,
    string BasicProperty,
    string Trait,
    int LvlBonusRate,
    int DotTicks,
    double DrainRatio,
    int AggroChance,
    IReadOnlyList<CubicActionStat> Stats,
    IReadOnlyList<CubicActionLevel> Levels)
{
    public CubicActionLevel At(int level) => Levels[Math.Clamp(level, 1, Levels.Count) - 1];
}

/// <summary>Cubics from <c>data/cubics.json</c> (tools/import_cubics.py over Mobius CT 0 stats/cubics and skills).</summary>
public sealed class CubicCatalog
{
    public const string FileName = "cubics.json";

    readonly Dictionary<(int Id, int Level), CubicTemplate> _templates;
    readonly Dictionary<int, int> _maxLevel;
    readonly Dictionary<int, CubicSummonSkill> _summons;
    readonly Dictionary<int, CubicAction> _actions;
    readonly Dictionary<int, int[]> _mastery;

    CubicCatalog(Dictionary<(int, int), CubicTemplate> templates, Dictionary<int, CubicSummonSkill> summons,
        Dictionary<int, CubicAction> actions, Dictionary<int, int[]> mastery)
    {
        _templates = templates;
        _maxLevel = templates.Keys.GroupBy(static k => k.Item1).ToDictionary(static g => g.Key, static g => g.Max(static k => k.Item2));
        _summons = summons;
        _actions = actions;
        _mastery = mastery;
    }

    public static CubicCatalog Empty { get; } = new([], [], [], []);

    public IReadOnlyCollection<CubicSummonSkill> SummonSkills => _summons.Values;

    public CubicSummonSkill? SummonSkill(int skillId) => _summons.TryGetValue(skillId, out var s) ? s : null;

    /// <summary>Template for a cubic id at a level; a level past the table uses the highest row.</summary>
    public CubicTemplate? Template(int id, int level)
    {
        if (_templates.TryGetValue((id, level), out var t))
            return t;
        if (_maxLevel.TryGetValue(id, out int max) && level > max)
            return _templates[(id, max)];
        return null;
    }

    public CubicAction? Action(int skillId) => _actions.TryGetValue(skillId, out var a) ? a : null;

    public IReadOnlyCollection<CubicAction> Actions => _actions.Values;

    /// <summary>Mobius <c>CubicMastery</c>: max cubics for a known mastery skill level; null when not a mastery skill.</summary>
    public int? MasteryCount(int skillId, int level) =>
        _mastery.TryGetValue(skillId, out var counts) && counts.Length > 0 ? counts[Math.Clamp(level, 1, counts.Length) - 1] : null;

    public IEnumerable<int> MasterySkillIds => _mastery.Keys;

    sealed class FileDto
    {
        public List<CubicDto>? Cubics { get; set; }
        public List<SummonDto>? SummonSkills { get; set; }
        public List<CubicAction>? Actions { get; set; }
        public List<MasteryDto>? Mastery { get; set; }
    }

    sealed class CubicDto
    {
        public int Id { get; set; }
        public int Level { get; set; }
        public int DurationSec { get; set; }
        public int DelaySec { get; set; }
        public int MaxCount { get; set; } = -1;
        public bool UseUp { get; set; }
        public double Power { get; set; }
        public string? TargetType { get; set; }
        public string? Selection { get; set; }
        public List<CubicSkillRef>? Skills { get; set; }
    }

    sealed class SummonDto
    {
        public int SkillId { get; set; }
        public string? Name { get; set; }
        public int CubicId { get; set; }
        public bool Party { get; set; }
        public int DelaySec { get; set; }
        public int DurationSec { get; set; }
        public int MaxCount { get; set; } = -1;
        public int Chance { get; set; }
        public List<CubicSummonLevel>? Levels { get; set; }
    }

    sealed class MasteryDto
    {
        public int SkillId { get; set; }
        public int[]? Counts { get; set; }
    }

    public static CubicCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName) ?? throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    public static CubicCatalog Parse(string json, string source = FileName)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var file = JsonSerializer.Deserialize<FileDto>(json, options) ?? throw new InvalidDataException($"{source} was empty.");
        var templates = new Dictionary<(int, int), CubicTemplate>();
        foreach (var c in file.Cubics ?? [])
        {
            if (c.Id <= 0 || c.Level <= 0 || c.DelaySec <= 0 || c.DurationSec <= 0)
                throw new InvalidDataException($"{source} has an invalid cubic {c.Id} level {c.Level}.");
            var t = new CubicTemplate(c.Id, c.Level, c.DurationSec, c.DelaySec, c.MaxCount, c.UseUp, c.Power,
                c.TargetType ?? "TARGET", c.Selection ?? "RANDOM", c.Skills ?? []);
            if (!templates.TryAdd((c.Id, c.Level), t))
                throw new InvalidDataException($"{source} repeats cubic {c.Id} level {c.Level}.");
        }

        var summons = new Dictionary<int, CubicSummonSkill>();
        foreach (var s in file.SummonSkills ?? [])
        {
            if (s.SkillId <= 0 || s.CubicId <= 0 || s.Levels is not { Count: > 0 })
                throw new InvalidDataException($"{source} has an invalid summon skill {s.SkillId}.");
            if (!templates.Keys.Any(k => k.Item1 == s.CubicId))
                throw new InvalidDataException($"{source} summon skill {s.SkillId} names unknown cubic {s.CubicId}.");
            summons[s.SkillId] = new CubicSummonSkill(s.SkillId, s.Name ?? "", s.CubicId, s.Party, s.DelaySec, s.DurationSec,
                s.MaxCount, s.Chance, s.Levels.OrderBy(static l => l.Level).ToArray());
        }

        var actions = new Dictionary<int, CubicAction>();
        foreach (var a in file.Actions ?? [])
        {
            if (a.SkillId <= 0 || a.Levels is not { Count: > 0 })
                throw new InvalidDataException($"{source} has an invalid cubic action {a.SkillId}.");
            actions[a.SkillId] = a with { Stats = a.Stats ?? [], AbnormalType = a.AbnormalType ?? "", BasicProperty = a.BasicProperty ?? "NONE", Trait = a.Trait ?? "" };
        }

        foreach (var t in templates.Values)
        {
            foreach (var k in t.Skills)
            {
                if (!actions.ContainsKey(k.SkillId))
                    throw new InvalidDataException($"{source} cubic {t.Id} uses unknown action {k.SkillId}.");
            }
        }

        var mastery = new Dictionary<int, int[]>();
        foreach (var m in file.Mastery ?? [])
            mastery[m.SkillId] = m.Counts ?? [];
        return new CubicCatalog(templates, summons, actions, mastery);
    }
}
