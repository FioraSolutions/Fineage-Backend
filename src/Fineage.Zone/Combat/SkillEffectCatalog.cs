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

    /// <summary>
    /// One Mobius <c>&lt;effect&gt;</c> row. <paramref name="Power"/> and <paramref name="Ticks"/> are the effect's own
    /// <c>&lt;param power&gt;</c> / <c>ticks</c>: the toggle MP drain reads them
    /// (<c>power · ticks · EffectTickRatio / 1000</c>).
    /// </summary>
    public readonly record struct Effect(string Name, string Slot, int Rate, int Max, double Power = 0, int Ticks = 0)
    {
        /// <summary>
        /// Mobius <c>&lt;param dispel="SLOT,count;SLOT,count"/&gt;</c> per skill level (tables like 1012's
        /// <c>#dispelAbnormals</c> are level lists, so one row carries them all). Empty when the effect has none.
        /// </summary>
        public IReadOnlyList<string> Dispel { get; init; } = EmptyDispel;

        /// <summary>The slot list for one cast level: the row's own value, or the level's entry of a table.</summary>
        public string DispelAt(int level) =>
            Dispel.Count == 0 ? "" : Dispel[Math.Clamp(Math.Max(1, level), 1, Dispel.Count) - 1];

        /// <summary>
        /// Per-level <c>power</c> table (Life Scavenge <c>#drain</c>, Wrath <c>#power</c>). Empty = use <see cref="Power"/>.
        /// </summary>
        public IReadOnlyList<double> Powers { get; init; } = EmptyPowers;

        /// <summary>Effect power at the skill level, falling back to the scalar <see cref="Power"/>.</summary>
        public double PowerAt(int level) =>
            Powers.Count == 0 ? Power : Powers[Math.Clamp(Math.Max(1, level), 1, Powers.Count) - 1];

        public int Chance => ReadInt("chance");
        public int SkillId => ReadInt("skillId");
        public int SkillLevel => ReadInt("skillLevel", 1);
        public int MinAttackerLevel => ReadInt("minAttackerLevel", 1);
        public int MaxAttackerLevel => ReadInt("maxAttackerLevel", 100);
        public int MinDamage => ReadInt("minDamage", 1);
        public string AttackerType => Read("attackerType");

        int ReadInt(string key, int fallback = 0) => Extra.TryGetValue(key, out string? v) &&
            int.TryParse(v, out int n) ? n : fallback;

        /// <summary>Same as <see cref="ReadInt"/> but public for the zone's skill handlers (signet <c>npcId</c>).</summary>
        public int ReadExtraInt(string key, int fallback = 0) => ReadInt(key, fallback);

        /// <summary>Raw extra param (GetAgro <c>affectPlayers</c>, etc.).</summary>
        public string ReadExtra(string key) => Read(key);

        string Read(string key) => Extra.TryGetValue(key, out string? v) ? v : "";

        /// <summary>Remaining <c>&lt;param&gt;</c> values (chance, trigger skillId/level, attacker bounds) for effect lookups.</summary>
        public IReadOnlyDictionary<string, string> Extra { get; init; } = EmptyExtra;
    }

    static readonly Dictionary<string, string> EmptyExtra = new();

    static readonly string[] EmptyDispel = [];

    static readonly double[] EmptyPowers = [];


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

    /// <summary>Mobius <c>DispelBySlot</c> rows, in datapack order (a skill may list several).</summary>
    public bool TryDispelBySlot(int skillId, out List<Effect> rows)
    {
        rows = [];
        if (!_byId.TryGetValue(skillId, out var effects))
            return false;
        foreach (var e in effects)
        {
            if (e.Name.Equals("DispelBySlot", StringComparison.OrdinalIgnoreCase))
                rows.Add(e);
        }
        return rows.Count > 0;
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
                n.Equals("CpHealPercent", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("ManaHeal", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("ManaHealPercent", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("ManaHealByLevel", StringComparison.OrdinalIgnoreCase))
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
                int ticks = ReadInt(e, "ticks", 0);
                double power = ReadDouble(e, "power");
                var powers = ReadDoubles(e, "power");
                if (powers.Length == 1)
                    power = powers[0];
                var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name is "name" or "slot" or "rate" or "max" or "ticks" or "power" or "dispel")
                        continue;
                    extra[p.Name] = p.Value.ValueKind == JsonValueKind.String
                        ? p.Value.GetString() ?? ""
                        : p.Value.ToString();
                }
                list.Add(new Effect(name, slot, rate, max, power, ticks)
                {
                    Extra = extra,
                    Dispel = ReadDispel(e),
                    Powers = powers
                });
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

    /// <summary>
    /// <c>&lt;param dispel&gt;</c> values, one per skill level when the datapack used a table
    /// (<c>#dispelAbnormals</c> expands into "POISON,3" "POISON,7" …). A single value carries one entry.
    /// </summary>
    static string[] ReadDispel(JsonElement e)
    {
        if (!e.TryGetProperty("dispel", out var v))
            return EmptyDispel;
        if (v.ValueKind == JsonValueKind.String)
            return [v.GetString() ?? ""];
        if (v.ValueKind != JsonValueKind.Array)
            return EmptyDispel;
        var rows = new List<string>();
        foreach (var item in v.EnumerateArray())
            rows.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString());
        return rows.Count > 0 ? rows.ToArray() : EmptyDispel;
    }

    static double[] ReadDoubles(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v))
            return EmptyPowers;
        if (v.ValueKind == JsonValueKind.Array)
        {
            var list = new List<double>();
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double d))
                    list.Add(d);
                else if (item.ValueKind == JsonValueKind.String &&
                    double.TryParse(item.GetString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out d))
                    list.Add(d);
            }
            return list.Count > 0 ? list.ToArray() : EmptyPowers;
        }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double one))
            return [one];
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out one))
            return [one];
        return EmptyPowers;
    }

    static double ReadDouble(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v))
            return 0d;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d))
            return d;
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out d))
            return d;
        return 0d;
    }
}
