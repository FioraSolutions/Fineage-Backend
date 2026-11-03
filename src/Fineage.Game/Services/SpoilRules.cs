using System.Text.Json;

namespace Fineage.Game.Services;

public enum SpoilRefusal { None, InvalidTarget, AlreadySpoiled, Resisted }

public enum SweepRefusal { None, InvalidTarget, NotSpoiled, NoRights, CorpseTooOld }

/// <summary>
/// Mobius Interlude spoil/sweep rules (data/spoil.json): Spoil effect, ConditionPlayerCanSweep, checkSpoilOwner, isOldCorpse and
/// the spoiled corpse extend time. Pure decisions; ZoneWorldSpoil applies them.
/// </summary>
public sealed class SpoilRules
{
    public const string FileName = "spoil.json";

    public IReadOnlyDictionary<int, int[]> SpoilMagicLevels { get; init; } = new Dictionary<int, int[]>();
    public IReadOnlySet<int> SweepSkills { get; init; } = new HashSet<int>();
    public int CorpseTimeMs { get; init; } = 7000;
    public int SpoiledCorpseExtendMs { get; init; } = 10000;
    public int SweepAllowedBeforeDecayMs { get; init; } = 2000;
    public SpoilMessages Messages { get; init; } = new();

    public bool IsSpoilSkill(int skillId) => SpoilMagicLevels.ContainsKey(skillId);
    public bool IsSweepSkill(int skillId) => SweepSkills.Contains(skillId);

    /// <summary>Skill magicLevel for <c>calcMagicSuccess</c>; past the table → last entry.</summary>
    public int MagicLevel(int skillId, int level)
    {
        if (!SpoilMagicLevels.TryGetValue(skillId, out var table) || table.Length == 0)
            return 1;
        return table[Math.Clamp(level, 1, table.Length) - 1];
    }

    /// <summary>Mobius DecayTaskManager.add: spoiled attackables get SpoiledCorpseExtendTime on top of DefaultCorpseTime.</summary>
    public int CorpseMs(bool spoiled) => CorpseTimeMs + (spoiled ? SpoiledCorpseExtendMs : 0);

    /// <summary>Mobius Spoil.onStart order: monster alive → not yet spoiled → calcMagicSuccess roll.</summary>
    public static SpoilRefusal Spoil(bool monsterAlive, bool alreadySpoiled, Func<bool> magicSuccess)
    {
        if (!monsterAlive)
            return SpoilRefusal.InvalidTarget;
        if (alreadySpoiled)
            return SpoilRefusal.AlreadySpoiled;
        return magicSuccess() ? SpoilRefusal.None : SpoilRefusal.Resisted;
    }

    /// <summary>
    /// Mobius ConditionPlayerCanSweep: dead attackable, spoiled (else 343), spoiler or a party mate of the spoiler (else 683),
    /// not an old corpse (remaining decay ≥ CorpseConsumeSkillAllowedTimeBeforeDecay, else 1247).
    /// </summary>
    public SweepRefusal Sweep(bool deadAttackable, int spoilerId, int sweeperId, bool sameParty, long remainingDecayMs)
    {
        if (!deadAttackable)
            return SweepRefusal.InvalidTarget;
        if (spoilerId == 0)
            return SweepRefusal.NotSpoiled;
        if (spoilerId != sweeperId && !sameParty)
            return SweepRefusal.NoRights;
        if (remainingDecayMs < SweepAllowedBeforeDecayMs)
            return SweepRefusal.CorpseTooOld;
        return SweepRefusal.None;
    }

    public static SpoilRules LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName) ?? throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    public static SpoilRules Parse(string json, string source = FileName)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"{source} is empty.");
        var spoil = new Dictionary<int, int[]>();
        foreach (var pair in dto.SpoilSkills ?? [])
        {
            if (!int.TryParse(pair.Key, out int id) || pair.Value is not { Length: > 0 })
                throw new InvalidDataException($"{source} spoil skill '{pair.Key}' needs a magicLevel table.");
            spoil[id] = pair.Value;
        }

        if (spoil.Count == 0 || dto.SweepSkills is not { Length: > 0 })
            throw new InvalidDataException($"{source} needs spoilSkills and sweepSkills.");
        return new SpoilRules
        {
            SpoilMagicLevels = spoil,
            SweepSkills = dto.SweepSkills.ToHashSet(),
            CorpseTimeMs = dto.CorpseTimeMs,
            SpoiledCorpseExtendMs = dto.SpoiledCorpseExtendMs,
            SweepAllowedBeforeDecayMs = dto.SweepAllowedBeforeDecayMs,
            Messages = dto.Messages ?? new()
        };
    }

    sealed class Dto
    {
        public Dictionary<string, int[]>? SpoilSkills { get; set; }
        public int[]? SweepSkills { get; set; }
        public int CorpseTimeMs { get; set; } = 7000;
        public int SpoiledCorpseExtendMs { get; set; } = 10000;
        public int SweepAllowedBeforeDecayMs { get; set; } = 2000;
        public SpoilMessages? Messages { get; set; }
    }
}

public sealed class SpoilMessages
{
    public int InvalidTarget { get; set; } = 109;
    public int Resisted { get; set; } = 139;
    public int SweepNotSpoiled { get; set; } = 343;
    public int AlreadySpoiled { get; set; } = 357;
    public int SpoilActivated { get; set; } = 612;
    public int NoSweepRights { get; set; } = 683;
    public int CorpseTooOld { get; set; } = 1247;
}
