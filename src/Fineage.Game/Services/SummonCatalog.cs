using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>One level of a servitor summon skill (Mobius effect <c>Summon</c> + the skill's item cost).</summary>
public sealed record SummonLevel(
    int Level,
    int NpcId,
    int ItemId,
    int ItemCount,
    int UpkeepItemId,
    int UpkeepCount,
    int UpkeepIntervalSec,
    int LifeTimeSec,
    double ExpMultiplier);

public sealed record SummonSkill(int SkillId, string Name, bool Corpse, IReadOnlyList<SummonLevel> Levels)
{
    public SummonLevel? At(int level) =>
        Levels.Count == 0 ? null : Levels[Math.Clamp(level, 1, Levels.Count) - 1];
}

/// <summary>Servitor summon skills (data/summons.json, transcribed from Mobius CT 0 stats/skills).</summary>
public sealed class SummonCatalog
{
    public const string FileName = "summons.json";
    /// <summary>Mobius Summon effect: upkeep every 240 s unless the skill sets its own interval (siege weapons 60 s).</summary>
    public const int DefaultUpkeepIntervalSec = 240;
    public const int SiegeUpkeepIntervalSec = 60;

    readonly Dictionary<int, SummonSkill> _bySkill;
    readonly Dictionary<int, IReadOnlyList<(int SkillId, int Level)>> _npcSkills;

    SummonCatalog(Dictionary<int, SummonSkill> bySkill, Dictionary<int, IReadOnlyList<(int SkillId, int Level)>> npcSkills)
    {
        _bySkill = bySkill;
        _npcSkills = npcSkills;
    }

    public static SummonCatalog Empty { get; } = new([], []);

    /// <summary>Attack skills the servitor can be ordered to use (Mobius NPC skillList, rollable by skill-combat.json).</summary>
    public IReadOnlyList<(int SkillId, int Level)> SkillsOf(int npcId) =>
        _npcSkills.TryGetValue(npcId, out var rows) ? rows : [];

    public SummonSkill? Find(int skillId) => _bySkill.TryGetValue(skillId, out var s) ? s : null;

    public bool IsSummonSkill(int skillId) => _bySkill.ContainsKey(skillId);

    public IReadOnlyCollection<SummonSkill> All => _bySkill.Values;

    sealed class FileDto
    {
        public List<SummonSkill>? Summons { get; set; }
        public Dictionary<string, List<int[]>>? NpcSkills { get; set; }
    }

    public static SummonCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName) ?? throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    public static SummonCatalog Parse(string json, string source = FileName)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var file = JsonSerializer.Deserialize<FileDto>(json, options) ?? throw new InvalidDataException($"{source} was empty.");
        var map = new Dictionary<int, SummonSkill>();
        foreach (var skill in file.Summons ?? [])
        {
            if (skill.SkillId <= 0 || skill.Levels is not { Count: > 0 })
                throw new InvalidDataException($"{source} has a summon skill without id or levels.");
            foreach (var level in skill.Levels)
            {
                if (level.NpcId <= 0 || level.LifeTimeSec <= 0 || level.ExpMultiplier is < 0 or > 1 || level.ItemCount < 0 || level.UpkeepCount < 0)
                    throw new InvalidDataException($"{source} skill {skill.SkillId} level {level.Level} is invalid.");
            }

            if (!map.TryAdd(skill.SkillId, skill))
                throw new InvalidDataException($"{source} repeats skill {skill.SkillId}.");
        }

        var skills = new Dictionary<int, IReadOnlyList<(int SkillId, int Level)>>();
        foreach (var (key, rows) in file.NpcSkills ?? [])
        {
            if (!int.TryParse(key, out int npcId) || rows.Any(static r => r.Length != 2 || r[0] <= 0 || r[1] <= 0))
                throw new InvalidDataException($"{source} npcSkills row {key} is invalid.");
            skills[npcId] = rows.Select(static r => (r[0], r[1])).ToArray();
        }

        return new SummonCatalog(map, skills);
    }
}
