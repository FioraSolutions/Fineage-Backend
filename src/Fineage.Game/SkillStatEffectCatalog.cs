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
        ["pAtk"] = CombatStat.PAtk,
        ["pDef"] = CombatStat.PDef,
        ["mAtk"] = CombatStat.MAtk,
        ["mDef"] = CombatStat.MDef,
        ["pAtkSpd"] = CombatStat.PAtkSpd,
        ["mAtkSpd"] = CombatStat.MAtkSpd,
        ["accCombat"] = CombatStat.Accuracy,
        ["rEvas"] = CombatStat.Evasion,
        ["critRate"] = CombatStat.CritRate,
        ["runSpd"] = CombatStat.RunSpd,
        ["regHp"] = CombatStat.RegHp,
        ["regMp"] = CombatStat.RegMp,
        ["regCp"] = CombatStat.RegCp,
        ["weightLimit"] = CombatStat.WeightLimit,
        ["weightPenalty"] = CombatStat.WeightPenalty,
        ["mCritRate"] = CombatStat.MCritRate,
        ["mCritPower"] = CombatStat.MagicCritDmg,
        ["critDmg"] = CombatStat.CritDmg,
        ["critDmgAdd"] = CombatStat.CritDmgAdd,
        ["blowRate"] = CombatStat.BlowRate,
        ["pSkillEvas"] = CombatStat.PSkillEvasion,
        // Mobius positional stats (Focus Chance 356 / Focus Power 357 / Focus Death 355 / Assassin Servitor 1348).
        ["critRatePos"] = CombatStat.CritRatePos,
        ["critDmgPos"] = CombatStat.CritDmgPos,
        ["maxHp"] = CombatStat.MaxHp,
        ["maxMp"] = CombatStat.MaxMp,
        ["maxCp"] = CombatStat.MaxCp,
        ["healEffect"] = CombatStat.HealEffect,
        ["pReuse"] = CombatStat.PReuse,
        ["mReuse"] = CombatStat.MReuse,
        ["dReuse"] = CombatStat.DReuse,
        ["sDef"] = CombatStat.SDef,
        ["rShld"] = CombatStat.RShld,
        ["reflectDam"] = CombatStat.ReflectDam,
        ["reflectSkillPhysic"] = CombatStat.ReflectSkillPhysic,
        ["reflectSkillMagic"] = CombatStat.ReflectSkillMagic,
        ["vengeancePdam"] = CombatStat.VengeancePdam,
        ["vengeanceMdam"] = CombatStat.VengeanceMdam,
        ["absorbDam"] = CombatStat.AbsorbDam,
        ["physicalMpConsumeRate"] = CombatStat.PhysicalMpConsumeRate,
        ["magicalMpConsumeRate"] = CombatStat.MagicalMpConsumeRate,
        ["danceMpConsumeRate"] = CombatStat.DanceMpConsumeRate,
        ["shieldDefAngle"] = CombatStat.ShieldDefAngle,
        ["skillCritical"] = CombatStat.SkillCritical,
        ["skillCriticalProbability"] = CombatStat.SkillCriticalProbability,
        ["atkCountMax"] = CombatStat.AtkCountMax,
        ["pAtkRange"] = CombatStat.PAtkRange,
        ["breath"] = CombatStat.Breath,
        ["fall"] = CombatStat.Fall,
        ["cancelVuln"] = CombatStat.CancelVuln,
        ["debuffVuln"] = CombatStat.DebuffVuln,
        ["holyPower"] = CombatStat.HolyPower,
        ["firePower"] = CombatStat.FirePower,
        ["darkRes"] = CombatStat.DarkRes,
        ["holyRes"] = CombatStat.HolyRes,
        ["fireRes"] = CombatStat.FireRes,
        ["waterRes"] = CombatStat.WaterRes,
        ["windRes"] = CombatStat.WindRes,
        ["earthRes"] = CombatStat.EarthRes,
        ["transDam"] = CombatStat.TransDam,
        ["cancel"] = CombatStat.Cancel,
        ["damageZoneVuln"] = CombatStat.DamageZoneVuln
    };

    static readonly Dictionary<string, SkillPosition> PositionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["front"] = SkillPosition.Front,
        ["side"] = SkillPosition.Side,
        ["behind"] = SkillPosition.Behind
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
    /// <paramref name="position"/> is the owner's current attack side for Mobius positional funcs
    /// (<c>&lt;player front|behind&gt;</c>, Focus Chance / Focus Power); <see cref="SkillPosition.None"/> keeps them out.
    /// </summary>
    public CombatStatMods Collect(
        IEnumerable<SkillStatSource> sources,
        Action<int>? onMissing = null,
        SkillPosition position = SkillPosition.None,
        Func<string[], bool>? wearing = null,
        Func<string[], bool>? wearingSlot = null,
        double hpPercent = 100d)
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
            {
                if (f.Position != SkillPosition.None && f.Position != position)
                    continue;
                // Mobius skill-level <cond><using kind> (Focus Chance / Focus Power require a dagger in hand).
                if (UsingGateFails(f, wearing, wearingSlot))
                    continue;
                if (f.HpBelowPercent is int maxHpPct && hpPercent > maxHpPct)
                    continue;
                mods.Add(f, level);
            }
        }
        return mods ?? CombatStatMods.None;
    }


    static bool UsingGateFails(SkillStatFunc f, Func<string[], bool>? wearing, Func<string[], bool>? wearingSlot = null)
    {
        // No inventory context (unit tests that omit wearing) — leave kind/slot gates off.
        if (wearing == null && wearingSlot == null)
            return false;
        if (f.Using != null && (wearing == null || !wearing(f.Using)))
            return true;
        if (f.NotUsing != null && wearing != null && wearing(f.NotUsing))
            return true;
        // Fail closed when a slot gate is present but the caller gave inventory kinds without a slot checker.
        if (f.UsingSlot != null && (wearingSlot == null || !wearingSlot(f.UsingSlot)))
            return true;
        return false;
    }

    /// <summary>
    /// Folds passive skill funcs (Mobius operateType P) for the <paramref name="known"/> skill → level map into
    /// <paramref name="mods"/>. Funcs with a <c>using</c> / <c>notUsing</c> condition apply only when <paramref name="wearing"/> matches (or does not match);
    /// <c>resting</c> funcs (Vital Force) apply only when <paramref name="resting"/> says the owner is seated;
    /// <c>hpBelow</c> funcs (Final Frenzy / Final Fortress) apply only when current HP is at or below that percent.
    /// <c>moving</c> / <c>standing</c> funcs (Clear Mind) apply while the owner is moving, or standing still (not seated).
    /// </summary>
    public CombatStatMods CollectPassives(
        IReadOnlyDictionary<int, int> known,
        Func<string[], bool> wearing,
        CombatStatMods? mods = null,
        bool resting = false,
        double hpPercent = 100d,
        bool moving = false,
        Func<string[], bool>? wearingSlot = null)
    {
        foreach (var (skillId, level) in known.OrderBy(static p => p.Key))
        {
            if (!_byId.TryGetValue(skillId, out var effect))
                continue;
            foreach (var f in effect.Funcs)
            {
                if (UsingGateFails(f, wearing, wearingSlot))
                    continue;
                if (f.Resting && !resting)
                    continue;
                if (f.Moving && !moving)
                    continue;
                if (f.Standing && (moving || resting))
                    continue;
                if (f.HpBelowPercent is int maxHpPct && hpPercent > maxHpPct)
                    continue;
                mods ??= new CombatStatMods();
                mods.Add(f, level);
            }
        }
        return mods ?? CombatStatMods.None;
    }

    /// <summary>
    /// Folds only the functions gated on an attack side (<c>&lt;player front|behind&gt;</c>). Mobius resolves those in
    /// <c>calcStat(stat, …, target, skill)</c>, so they must not sit on the cached player stat line — they are folded per
    /// swing. Returns <see cref="CombatStatMods.None"/> when no active abnormal has positional funcs.
    /// </summary>
    public CombatStatMods CollectPositional(
        IEnumerable<SkillStatSource> sources,
        SkillPosition position,
        Func<string[], bool>? wearing = null,
        Func<string[], bool>? wearingSlot = null)
    {
        if (position == SkillPosition.None)
            return CombatStatMods.None;
        CombatStatMods? mods = null;
        foreach (var a in sources)
        {
            if (!_byId.TryGetValue(a.SkillId, out var effect))
                continue;
            int level = a.SkillLevel > 0
                ? a.SkillLevel
                : (effect.Levels <= 1 ? 1 : Math.Clamp(a.AbnormalValue, 1, effect.Levels));
            foreach (var f in effect.Funcs)
            {
                if (f.Position == SkillPosition.None || f.Position != position)
                    continue;
                if (UsingGateFails(f, wearing, wearingSlot))
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
                    "mul" => 'm',
                    "add" => 'a',
                    "sub" => 's',
                    "set" => '=',
                    _ => '?'
                };
                if (op == '?')
                    continue;
                var values = e.GetProperty("values").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                string[]? usingKinds = e.TryGetProperty("using", out var u)
                    ? u.EnumerateArray().Select(k => (k.GetString() ?? "").ToUpperInvariant()).ToArray()
                    : null;
                bool resting = e.TryGetProperty("resting", out var r) && r.GetBoolean();
                SkillPosition position = e.TryGetProperty("position", out var p) &&
                    PositionNames.TryGetValue(p.GetString() ?? "", out var parsedPosition)
                        ? parsedPosition
                        : SkillPosition.None;
                int? hpBelow = e.TryGetProperty("hpBelow", out var hp) ? hp.GetInt32() : null;
                bool moving = e.TryGetProperty("moving", out var mv) && mv.GetBoolean();
                bool standing = e.TryGetProperty("standing", out var st) && st.GetBoolean();
                string[]? notUsing = e.TryGetProperty("notUsing", out var nu)
                    ? nu.EnumerateArray().Select(k => (k.GetString() ?? "").ToUpperInvariant()).ToArray()
                    : null;
                string[]? usingSlot = e.TryGetProperty("usingSlot", out var us)
                    ? us.EnumerateArray().Select(k => (k.GetString() ?? "").ToLowerInvariant()).ToArray()
                    : null;
                funcs.Add(new SkillStatFunc(stat, op, values, usingKinds, resting, position, hpBelow, moving, standing, notUsing, usingSlot));
            }
            if (funcs.Count > 0)
            {
                // Skill-level <cond><using kind> applies to the skill itself (cast refusal), not only to its funcs.
                string[]? skillUsing = row.TryGetProperty("using", out var su)
                    ? su.EnumerateArray().Select(k => (k.GetString() ?? "").ToUpperInvariant()).ToArray()
                    : null;
                byId[id] = new SkillStatEffect(id, name, Math.Max(1, levels), funcs) { Using = skillUsing };
            }
        }
        return new SkillStatEffectCatalog(byId);
    }
}
