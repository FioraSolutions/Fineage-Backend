using System.Text.Json;

namespace Fineage.Game.Services;

public enum HerbKind { HpPercent, MpPercent, Buff }

public sealed record HerbSkill(int SkillId, string Name, HerbKind Kind, int[] Amount, int AbnormalLevel, int DurationSeconds)
{
    /// <summary>Percent for HealPercent/ManaHealPercent at this skill level (1-based).</summary>
    public int AmountAt(int level) => Amount.Length == 0 ? 0 : Amount[Math.Clamp(level, 1, Amount.Length) - 1];
}

/// <summary>
/// Mobius Interlude herbs (data/herbs.json): items with immediate_effect that are consumed on pickup through ItemSkills.
/// Rules: HP/MP percent restore capped at max, nothing on a dead player; buffs are timed abnormals.
/// </summary>
public sealed class HerbCatalog
{
    public const string FileName = "herbs.json";
    readonly Dictionary<int, (int SkillId, int Level)[]> _items;
    readonly Dictionary<int, HerbSkill> _skills;

    HerbCatalog(Dictionary<int, (int, int)[]> items, Dictionary<int, HerbSkill> skills, int hpMsg, int mpMsg)
    {
        _items = items;
        _skills = skills;
        HpRestoredMessage = hpMsg;
        MpRestoredMessage = mpMsg;
    }

    public int HpRestoredMessage { get; }
    public int MpRestoredMessage { get; }
    public IEnumerable<int> ItemIds => _items.Keys;

    public bool IsHerb(int itemId) => _items.ContainsKey(itemId);

    public IReadOnlyList<(HerbSkill Skill, int Level)> EffectsOf(int itemId) =>
        _items.TryGetValue(itemId, out var rows)
            ? rows.Where(r => _skills.ContainsKey(r.SkillId)).Select(r => (_skills[r.SkillId], r.Level)).ToList()
            : [];

    /// <summary>Mobius HealPercent: amount = max × power / 100, capped so cur never exceeds max. Returns the restored amount.</summary>
    public static int RestorePercent(int current, int max, int percent)
    {
        if (max <= 0 || percent <= 0)
            return 0;
        int amount = (int)Math.Min((long)max * percent / 100, max - (long)Math.Min(current, max));
        return Math.Max(0, amount);
    }

    public static HerbCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName) ?? throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    public static HerbCatalog Parse(string json, string source = FileName)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"{source} is empty.");
        var skills = new Dictionary<int, HerbSkill>();
        foreach (var s in dto.Skills ?? [])
        {
            var kind = s.Kind switch
            {
                "hpPercent" => HerbKind.HpPercent,
                "mpPercent" => HerbKind.MpPercent,
                "buff" => HerbKind.Buff,
                _ => throw new InvalidDataException($"{source} herb skill {s.SkillId} kind '{s.Kind}' is unknown.")
            };
            if (kind == HerbKind.Buff && s.DurationSeconds <= 0)
                throw new InvalidDataException($"{source} herb buff {s.SkillId} needs durationSeconds.");
            skills[s.SkillId] = new HerbSkill(s.SkillId, s.Name ?? "", kind, s.Amount ?? [], Math.Max(1, s.AbnormalLevel), s.DurationSeconds);
        }

        var items = new Dictionary<int, (int, int)[]>();
        foreach (var item in dto.Items ?? [])
        {
            var rows = (item.Skills ?? []).Select(r => (r.SkillId, Math.Max(1, r.Level))).ToArray();
            if (rows.Length == 0 || rows.Any(r => !skills.ContainsKey(r.SkillId)))
                throw new InvalidDataException($"{source} herb item {item.ItemId} references an unknown skill.");
            items[item.ItemId] = rows;
        }

        return new HerbCatalog(items, skills, dto.HpRestoredMessage, dto.MpRestoredMessage);
    }

    sealed class Dto
    {
        public int HpRestoredMessage { get; set; } = 1066;
        public int MpRestoredMessage { get; set; } = 1068;
        public List<ItemDto>? Items { get; set; }
        public List<SkillDto>? Skills { get; set; }
    }

    sealed class ItemDto
    {
        public int ItemId { get; set; }
        public List<RefDto>? Skills { get; set; }
    }

    sealed class RefDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
    }

    sealed class SkillDto
    {
        public int SkillId { get; set; }
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public int[]? Amount { get; set; }
        public int AbnormalLevel { get; set; }
        public int DurationSeconds { get; set; }
    }
}
