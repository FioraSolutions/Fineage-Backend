using Fineage.Game;
using System.Collections.Concurrent;
using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Game;

/// <summary>One abnormal/skill row used when folding combat stat funcs (Zone maps <c>ZoneAbnormalEntry</c> here).</summary>
public readonly record struct SkillStatSource(int SkillId, int SkillLevel, int AbnormalValue);

/// <summary>Loads <c>data/skill-stat-effects.json</c> / <c>skill-passives.json</c> (Mobius skill &lt;for&gt; funcs).</summary>
public sealed class SkillStatEffectCatalog
{
    public const string FileName = "skill-stat-effects.json";
    public const string PassivesFileName = "skill-passives.json";

    static readonly Dictionary<string, CombatStat> StatNames = new(StringComparer.Ordinal)
    {
        ["pAtk"] = CombatStat.PAtk, ["pDef"] = CombatStat.PDef, ["mAtk"] = CombatStat.MAtk, ["mDef"] = CombatStat.MDef,
        ["pAtkSpd"] = CombatStat.PAtkSpd, ["mAtkSpd"] = CombatStat.MAtkSpd, ["accCombat"] = CombatStat.Accuracy,
        ["rEvas"] = CombatStat.Evasion, ["critRate"] = CombatStat.CritRate, ["runSpd"] = CombatStat.RunSpd,
        ["regHp"] = CombatStat.RegHp, ["regMp"] = CombatStat.RegMp,
        ["weightLimit"] = CombatStat.WeightLimit, ["weightPenalty"] = CombatStat.WeightPenalty,
        ["mCritRate"] = CombatStat.MCritRate, ["mCritPower"] = CombatStat.MagicCritDmg,
        ["critDmg"] = CombatStat.CritDmg, ["blowRate"] = CombatStat.BlowRate,
        ["pSkillEvas"] = CombatStat.PSkillEvasion
    };

    readonly Dictionary<int, SkillStatEffect> _byId;
    static readonly ConcurrentDictionary<int, byte> MissingStatWarned = new();

    SkillStatEffectCatalog(Dictionary<int, SkillStatEffect> byId) => _byId = byId;

    public static readonly SkillStatEffectCatalog Empty = new([]);

    public int Count => _byId.Count;

    public bool TryGet(int skillId, out SkillStatEffect effect) => _byId.TryGetValue(skillId, out effect!);

    /// <summary>
    /// Folds every source's stat funcs. Level = stored skill level, else abnormal level clamped to the skill's levels.
    /// <paramref name="onMissing"/> is invoked once per unknown skill id (fail-loud hook).
    /// </summary>
    public CombatStatMods Collect(IEnumerable<SkillStatSource> sources, Action<int>? onMissing = null)
    {
        CombatStatMods? mods = null;
        foreach (var a in sources)
        {
            if (!_byId.TryGetValue(a.SkillId, out var effect))
            {
                if (onMissing != null && MissingStatWarned.TryAdd(a.SkillId, 0))
                    onMissing(a.SkillId);
                continue;
            }
            int level = a.SkillLevel > 0
                ? a.SkillLevel
                : (effect.Levels <= 1 ? 1 : Math.Clamp(a.AbnormalValue, 1, effect.Levels));
            mods ??= new CombatStatMods();
            foreach (var f in effect.Funcs)
                mods.Add(f, level);
        }
        return mods ?? CombatStatMods.None;
    }

    /// <summary>
    /// Folds passive skill funcs (Mobius operateType P) for the <paramref name="known"/> skill → level map into
    /// <paramref name="mods"/>. Funcs with a <c>using</c> condition apply only when <paramref name="wearing"/> accepts it.
    /// </summary>
    public CombatStatMods CollectPassives(IReadOnlyDictionary<int, int> known, Func<string[], bool> wearing, CombatStatMods? mods = null)
    {
        foreach (var (skillId, level) in known)
        {
            if (!_byId.TryGetValue(skillId, out var effect))
                continue;
            foreach (var f in effect.Funcs)
            {
                if (f.Using != null && !wearing(f.Using))
                    continue;
                mods ??= new CombatStatMods();
                mods.Add(f, level);
            }
        }
        return mods ?? CombatStatMods.None;
    }

    public static SkillStatEffectCatalog LoadPassives() => LoadFile(PassivesFileName);

    public static SkillStatEffectCatalog LoadDefault() => LoadFile(FileName);

    static SkillStatEffectCatalog LoadFile(string fileName)
    {
        var path = TeleportCatalog.FindFile(fileName);
        return path == null ? Empty : Parse(File.ReadAllText(path), path);
    }

    public static SkillStatEffectCatalog Parse(string json, string source = FileName)
    {
        using var doc = JsonDocument.Parse(json);
        var byId = new Dictionary<int, SkillStatEffect>();
        if (!doc.RootElement.TryGetProperty("skills", out var skills))
            throw new InvalidDataException($"{source} has no skills.");
        foreach (var row in skills.EnumerateArray())
        {
            int id = row.GetProperty("skillId").GetInt32();
            int levels = row.TryGetProperty("levels", out var lv) ? lv.GetInt32() : 1;
            string name = row.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var funcs = new List<SkillStatFunc>();
            foreach (var e in row.GetProperty("effects").EnumerateArray())
            {
                if (!StatNames.TryGetValue(e.GetProperty("stat").GetString() ?? "", out var stat))
                    continue;
                char op = (e.GetProperty("op").GetString() ?? "") switch
                {
                    "mul" => 'm', "add" => 'a', "sub" => 's', "set" => '=', _ => '?'
                };
                if (op == '?')
                    continue;
                var values = e.GetProperty("values").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                string[]? usingKinds = e.TryGetProperty("using", out var u)
                    ? u.EnumerateArray().Select(k => (k.GetString() ?? "").ToUpperInvariant()).ToArray()
                    : null;
                funcs.Add(new SkillStatFunc(stat, op, values, usingKinds));
            }
            if (funcs.Count > 0)
                byId[id] = new SkillStatEffect(id, name, Math.Max(1, levels), funcs);
        }
        return new SkillStatEffectCatalog(byId);
    }
}
