using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Combat;

/// <summary>
/// Mobius skill <c>&lt;effect&gt;</c> rows from <c>data/skill-effects.json</c>.
/// Routes A1 skills that are neither buffs nor combat nukes (Cancel, Mana Burn, …).
/// </summary>
public sealed class SkillEffectCatalog
{
    public const string FileName = "skill-effects.json";

    public readonly record struct Effect(string Name, string Slot, int Rate, int Max);

    readonly Dictionary<int, Effect[]> _byId;

    SkillEffectCatalog(Dictionary<int, Effect[]> byId) => _byId = byId;

    public static readonly SkillEffectCatalog Empty = new([]);

    public int Count => _byId.Count;

    public bool TryGet(int skillId, out Effect[] effects) => _byId.TryGetValue(skillId, out effects!);

    public bool TryDispelByCategory(int skillId, out Effect effect)
    {
        effect = default;
        if (!_byId.TryGetValue(skillId, out var rows))
            return false;
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].Name.Equals("DispelByCategory", StringComparison.OrdinalIgnoreCase))
            {
                effect = rows[i];
                return true;
            }
        }
        return false;
    }

    public bool HasMagicalAttackMp(int skillId)
    {
        if (!_byId.TryGetValue(skillId, out var rows))
            return false;
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].Name.Equals("MagicalAttackMp", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public bool HasHeal(int skillId)
    {
        if (!_byId.TryGetValue(skillId, out var rows))
            return false;
        for (int i = 0; i < rows.Length; i++)
        {
            string n = rows[i].Name;
            if (n.Equals("Heal", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("HealPercent", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("CpHeal", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("ManaHeal", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public bool HasResurrection(int skillId)
    {
        if (!_byId.TryGetValue(skillId, out var rows))
            return false;
        for (int i = 0; i < rows.Length; i++)
        {
            string n = rows[i].Name;
            if (n.Equals("Resurrection", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("ResurrectionSpecial", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static SkillEffectCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName)
            ?? throw new FileNotFoundException($"Missing datapack {FileName} (skill effect rows).");
        return Parse(File.ReadAllText(path), path);
    }

    public static SkillEffectCatalog Parse(string json, string source = FileName)
    {
        using var doc = JsonDocument.Parse(json);
        var byId = new Dictionary<int, Effect[]>();
        if (!doc.RootElement.TryGetProperty("skills", out var skills))
            throw new InvalidDataException($"{source} has no skills.");
        foreach (var row in skills.EnumerateArray())
        {
            int id = row.GetProperty("skillId").GetInt32();
            if (id <= 0 || !row.TryGetProperty("effects", out var effectsEl))
                continue;
            var list = new List<Effect>();
            foreach (var e in effectsEl.EnumerateArray())
            {
                string name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.Length == 0)
                    continue;
                string slot = e.TryGetProperty("slot", out var s) ? s.GetString() ?? "" : "";
                int rate = ReadInt(e, "rate", 100);
                int max = ReadInt(e, "max", 1);
                list.Add(new Effect(name, slot, rate, max));
            }
            if (list.Count > 0)
                byId[id] = list.ToArray();
        }
        return new SkillEffectCatalog(byId);
    }

    static int ReadInt(JsonElement e, string key, int fallback)
    {
        if (!e.TryGetProperty(key, out var v))
            return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n))
            return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n))
            return n;
        return fallback;
    }
}
