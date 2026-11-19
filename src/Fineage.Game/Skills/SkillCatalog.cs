using System.Text.Json;

namespace Fineage.Game.Skills;

public sealed class SkillCatalog
{
    public const string FileName = "skills.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, SkillDefinition> _byId;

    SkillCatalog(IReadOnlyList<SkillDefinition> skills)
    {
        Skills = skills;
        _byId = new Dictionary<int, SkillDefinition>(skills.Count);
        foreach (var skill in skills)
            _byId.Add(skill.SkillId, skill);
    }

    public IReadOnlyList<SkillDefinition> Skills { get; }

    public SkillDefinition? Find(int skillId) => _byId.TryGetValue(skillId, out var skill) ? skill : null;

    public static SkillCatalog Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Skill datapack not found: {path}", path);
        try
        {
            return Parse(File.ReadAllText(path), path);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }
    }

    public static SkillCatalog Parse(string json, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        string where = source ?? FileName;
        var file = JsonSerializer.Deserialize<SkillFileDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"{where} was empty.");

        var skills = new List<SkillDefinition>();
        var seen = new HashSet<int>();
        foreach (var row in file.Skills ?? [])
        {
            if (row.SkillId <= 0)
                throw new InvalidDataException($"{where} has a skill id that is not positive.");
            if (!seen.Add(row.SkillId))
                throw new InvalidDataException($"{where} repeats skill id {row.SkillId}.");
            if (string.IsNullOrWhiteSpace(row.Name))
                throw new InvalidDataException($"{where} skill {row.SkillId} needs a name.");
            if (row.LevelData == null || row.LevelData.Count == 0)
                throw new InvalidDataException($"{where} skill {row.SkillId} has no levelData.");

            var levels = new SkillLevelData[row.LevelData.Count];            for (int i = 0; i < row.LevelData.Count; i++)
            {
                var levelRow = row.LevelData[i];
                int level = levelRow.Level == 0 ? i + 1 : levelRow.Level;
                int abnormalLevel = levelRow.AbnormalLevel ?? 1;
                levels[i] = new SkillLevelData(
                    level,
                    Math.Max(0, levelRow.MpConsume),
                    Math.Max(0, levelRow.Power),
                    abnormalLevel,
                    Math.Max(0, levelRow.MagicLevel),
                    Math.Max(0, levelRow.MpInitialConsume),
                    Math.Max(0, levelRow.ItemConsumeId),
                    Math.Max(0, levelRow.ItemConsumeCount))
                {
                    ReuseDelayMs = levelRow.ReuseDelayMs,
                    Extractable = (levelRow.Extractable ?? [])
                        .Select(g => new ExtractableGroup(
                            (g.Items ?? [])
                                .Where(it => it.Id > 0 && it.Count > 0)
                                .Select(it => new ExtractableItem(it.Id, it.Count))
                                .ToList(),
                            Math.Max(0, g.Chance)))
                        .Where(g => g.Items.Count > 0)
                        .ToList()
                };
            }

            skills.Add(new SkillDefinition(
                row.SkillId,
                row.Name.Trim(),
                row.Passive,
                (row.OperateType ?? "A1").Trim(),
                (row.TargetType ?? "ONE").Trim(),
                Math.Max(0, row.CastRange),
                Math.Max(0, row.HitTimeMs),
                Math.Max(0, row.ReuseDelayMs),
                Math.Max(0, row.AbnormalTimeSeconds),
                row.IsDebuff,
                levels)
            {
                MagicType = Math.Clamp(row.MagicType, 0, 2),
                EffectRange = Math.Max(0, row.EffectRange),
                AbnormalType = (row.AbnormalType ?? "").Trim(),
                AbnormalVisualEffect = (row.AbnormalVisualEffect ?? "").Trim(),
                Trait = (row.Trait ?? "").Trim().ToUpperInvariant(),
                ActivateRate = row.ActivateRate,
                LvlBonusRate = row.LvlBonusRate <= 0 ? 1 : row.LvlBonusRate,
                BasicProperty = (row.BasicProperty ?? "").Trim().ToUpperInvariant(),
                EffectPoints = row.EffectPoint is { Count: > 0 } ep ? ep : [],
                UsingKinds = (row.UsingKinds ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim().ToUpperInvariant()).ToArray()
            });
        }

        return new SkillCatalog(skills);
    }

    sealed class SkillFileDto
    {
        public List<SkillDto>? Skills { get; set; }
    }

    sealed class SkillDto
    {
        public int SkillId { get; set; }
        public string? Name { get; set; }
        public bool Passive { get; set; }
        public string? OperateType { get; set; }
        public string? TargetType { get; set; }
        public int CastRange { get; set; }
        public int HitTimeMs { get; set; }
        public int ReuseDelayMs { get; set; }
        public int AbnormalTimeSeconds { get; set; }
        public bool IsDebuff { get; set; }
        public string? AbnormalType { get; set; }
        public string? AbnormalVisualEffect { get; set; }
        public string? Trait { get; set; }
        public int ActivateRate { get; set; }
        public int LvlBonusRate { get; set; } = 1;
        public string? BasicProperty { get; set; }
        public int MagicType { get; set; }
        public int EffectRange { get; set; }
        /// <summary>Mobius <c>effectPoint</c>: a scalar row, or the per-level table expanded to a list.</summary>
        public List<int>? EffectPoint { get; set; }
        /// <summary>Mobius <c>&lt;cond msgId="113"&gt;&lt;using kind=".."/&gt;</c> weapon gate.</summary>
        public List<string>? UsingKinds { get; set; }
        public List<LevelDto>? LevelData { get; set; }
    }

    sealed class LevelDto
    {
        public int Level { get; set; }
        public int MpConsume { get; set; }
        public int Power { get; set; }
        public int? AbnormalLevel { get; set; }
        public int MagicLevel { get; set; }
        public int MpInitialConsume { get; set; }
        public int ItemConsumeId { get; set; }
        public int ItemConsumeCount { get; set; }
        public int? ReuseDelayMs { get; set; }
        public List<ExtractableDto>? Extractable { get; set; }
    }

    sealed class ExtractableDto
    {
        public List<ExtractableItemDto>? Items { get; set; }
        public double Chance { get; set; }
    }

    sealed class ExtractableItemDto
    {
        public int Id { get; set; }
        public int Count { get; set; }
    }
}
