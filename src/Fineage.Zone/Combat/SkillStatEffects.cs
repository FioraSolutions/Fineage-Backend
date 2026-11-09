using System.Collections.Concurrent;
using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Combat;

/// <summary>Combat stats a buff/debuff function can touch (Mobius Stat names in parentheses).</summary>
public enum CombatStat
{
    PAtk,      // pAtk
    PDef,      // pDef
    MAtk,      // mAtk
    MDef,      // mDef
    PAtkSpd,   // pAtkSpd
    MAtkSpd,   // mAtkSpd
    Accuracy,  // accCombat
    Evasion,   // rEvas
    CritRate,  // critRate
    RunSpd,    // runSpd = Stat.MOVE_SPEED: the only speed stat; folds onto run, walk and swim bases alike
    RegHp,     // regHp (Stat.REGENERATE_HP_RATE): HP regen multiplier
    RegMp,     // regMp (Stat.REGENERATE_MP_RATE): MP regen multiplier
    WeightLimit,   // weightLimit: Creature.getMaxLoad folds it over floor(CON bonus * 69000 * AltWeightLimit)
    WeightPenalty, // weightPenalty: Creature.getBonusWeightPenalty, subtracted from the load before weightproc
    MCritRate,     // mCritRate (Stat.MCRITICAL_RATE): folded before WIT·10 → ‰
    MagicCritDmg,  // mCritPower (Stat.MAGIC_CRIT_DMG): multiplies damage on a magic crit
    CritDmg,       // critDmg (Stat.CRITICAL_DAMAGE): multiplies physical / blow crit damage
    BlowRate,      // blowRate (Stat.BLOW_RATE): folded over calcBlowSuccess base rate
    PSkillEvasion  // pSkillEvas (Stat.P_SKILL_EVASION): % chance target dodges a physical skill
}

/// <param name="Using">Mobius <c>&lt;using kind&gt;</c> condition (upper-case kinds); null = unconditional.</param>
public sealed record SkillStatFunc(CombatStat Stat, char Op, double[] Values, string[]? Using = null)
{
    public double ValueAt(int level) => Values.Length == 0 ? 0 : Values[Math.Clamp(level, 1, Values.Length) - 1];
}

public sealed record SkillStatEffect(int SkillId, string Name, int Levels, IReadOnlyList<SkillStatFunc> Funcs);

/// <summary>
/// Accumulated stat functions from active abnormals. Mobius order (<c>StatFunction.java:24-31</c>):
/// <c>SET</c> 0 → <c>DIV</c>/<c>MUL</c> 20 → <c>ADD</c>/<c>SUB</c> 30, applied to the DEX-multiplied base
/// (<c>FuncMoveSpeed</c>, order 1). <c>DIV</c> is not modelled: no Interlude skill XML uses it on a stat we collect.
/// </summary>
public sealed class CombatStatMods
{
    readonly double[] _mul = Enumerable.Repeat(1d, Enum.GetValues<CombatStat>().Length).ToArray();
    readonly double[] _add = new double[Enum.GetValues<CombatStat>().Length];
    readonly double?[] _set = new double?[Enum.GetValues<CombatStat>().Length];

    public static readonly CombatStatMods None = new();

    public bool IsEmpty { get; private set; } = true;

    public void Add(SkillStatFunc func, int level)
    {
        int i = (int)func.Stat;
        double v = func.ValueAt(level);
        switch (func.Op)
        {
            case 'm': _mul[i] *= v; break;
            case 'a': _add[i] += v; break;
            case 's': _add[i] -= v; break;
            case '=': _set[i] = v; break;
            default: return;
        }
        IsEmpty = false;
    }

    /// <summary>
    /// Folds one stat the way Mobius' <c>Calculator</c> does: <c>SET</c> replaces the value, then the product of all
    /// <c>MUL</c>s, then the sum of signed <c>ADD</c>/<c>SUB</c>s. <paramref name="value"/> is the already DEX-multiplied
    /// base for speed stats.
    /// </summary>
    public double Apply(CombatStat stat, double value)
    {
        int i = (int)stat;
        return (_set[i] ?? value) * _mul[i] + _add[i];
    }

    public int Apply(CombatStat stat, int value) => (int)Apply(stat, (double)value);
}

/// <summary>Loads <c>data/skill-stat-effects.json</c> (Mobius buff/debuff combat stat funcs).</summary>
public sealed class SkillStatEffectCatalog
{
    public const string FileName = "skill-stat-effects.json";

    static readonly Dictionary<string, CombatStat> StatNames = new(StringComparer.Ordinal)
    {
        ["pAtk"] = CombatStat.PAtk, ["pDef"] = CombatStat.PDef, ["mAtk"] = CombatStat.MAtk, ["mDef"] = CombatStat.MDef,
        ["pAtkSpd"] = CombatStat.PAtkSpd, ["mAtkSpd"] = CombatStat.MAtkSpd, ["accCombat"] = CombatStat.Accuracy,
        ["rEvas"] = CombatStat.Evasion, ["critRate"] = CombatStat.CritRate, ["runSpd"] = CombatStat.RunSpd,
        // Regeneration and load stats: used by Weight Penalty 4270 (regHp/regMp), Weight Limit 150, armor sets
        // (weightLimit/weightPenalty) and the regen masteries. Regen has no other server-side consumer yet.
        ["regHp"] = CombatStat.RegHp, ["regMp"] = CombatStat.RegMp,
        ["weightLimit"] = CombatStat.WeightLimit, ["weightPenalty"] = CombatStat.WeightPenalty,
        // Magic crit: Wild Magic etc. (mCritRate) and MAGIC_CRIT_DMG (mCritPower) from Mobius Stat.java.
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

    /// <summary>Folds every active abnormal's stat funcs. Level = stored skill level, else abnormal level clamped to the skill's levels.</summary>
    public CombatStatMods Collect(IEnumerable<ZoneAbnormalEntry> abnormals)
    {
        CombatStatMods? mods = null;
        foreach (var a in abnormals)
        {
            if (!_byId.TryGetValue(a.Id, out var effect))
            {
                if (MissingStatWarned.TryAdd(a.Id, 0))
                    ZoneLog.Warn($"skill-stat-effects missing skillId={a.Id} — abnormal has no combat mods");
                continue;
            }
            int level = a.SkillLevel > 0 ? a.SkillLevel : (effect.Levels <= 1 ? 1 : Math.Clamp(a.Value, 1, effect.Levels));
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

    public const string PassivesFileName = "skill-passives.json";

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
