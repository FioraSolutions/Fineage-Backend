using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Combat;

public enum SkillDamageKind { Physical, Magical, Blow, Energy }

public enum BlowEffect { FatalBlow, Backstab }

/// <summary>One Mobius attack skill: per-level power/MP, base hit/cool/reuse timing.</summary>
public sealed record SkillCombatData(
    int SkillId, string Name, int Levels, SkillDamageKind Kind, bool Magic,
    double[] Power, int[] MpConsume, int[] MpInitialConsume,
    int HitTimeMs, int CoolTimeMs, int ReuseDelayMs, int CastRange, int BaseCritRate)
{
    /// <summary>Mobius blow effect: FatalBlow (any side) or Backstab (not from the front); null = not a modelled blow.</summary>
    public BlowEffect? Blow { get; init; }
    /// <summary>Mobius <c>blowChance</c> (%), scaled by DEX bonus and position.</summary>
    public int BlowChance { get; init; }
    /// <summary>Mobius Lethal effect: fullLethal / halfLethal %.</summary>
    public int FullLethal { get; init; }
    public int HalfLethal { get; init; }
    public int[] MagicLevel { get; init; } = [];
    /// <summary>Mobius <c>&lt;conditions&gt;&lt;using kind=..&gt;</c> (weapon/armor kinds, any matches); empty = no condition.</summary>
    public string[] UsingKinds { get; init; } = [];
    public int MagicLevelAt(int level) => MagicLevel.Length == 0 ? 0 : At(MagicLevel, level);

    static T At<T>(T[] a, int level) => a[Math.Clamp(level, 1, a.Length) - 1];
    public double PowerAt(int level) => Power.Length == 0 ? 0 : At(Power, level);
    public int MpAt(int level) => MpConsume.Length == 0 ? 0 : At(MpConsume, level);
    public int MpInitialAt(int level) => MpInitialConsume.Length == 0 ? 0 : At(MpInitialConsume, level);
    public bool IsPhysical => Kind != SkillDamageKind.Magical;
}

/// <summary>Loads <c>data/skill-combat.json</c> (Mobius attack skills).</summary>
public sealed class SkillCombatCatalog
{
    public const string FileName = "skill-combat.json";
    readonly Dictionary<int, SkillCombatData> _byId;
    SkillCombatCatalog(Dictionary<int, SkillCombatData> byId) => _byId = byId;
    public static readonly SkillCombatCatalog Empty = new([]);
    public int Count => _byId.Count;
    public bool TryGet(int skillId, out SkillCombatData data) => _byId.TryGetValue(skillId, out data!);

    public static SkillCombatCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path));
    }

    public static SkillCombatCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var byId = new Dictionary<int, SkillCombatData>();
        foreach (var r in doc.RootElement.GetProperty("skills").EnumerateArray())
        {
            var kind = (r.GetProperty("kind").GetString() ?? "") switch
            {
                "magical" => SkillDamageKind.Magical,
                "blow" => SkillDamageKind.Blow,
                "energy" => SkillDamageKind.Energy,
                _ => SkillDamageKind.Physical
            };
            int id = r.GetProperty("skillId").GetInt32();
            byId[id] = new SkillCombatData(
                id,
                r.GetProperty("name").GetString() ?? "",
                r.GetProperty("levels").GetInt32(),
                kind,
                r.GetProperty("magic").GetBoolean(),
                r.GetProperty("power").EnumerateArray().Select(v => v.GetDouble()).ToArray(),
                r.GetProperty("mpConsume").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
                r.GetProperty("mpInitialConsume").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
                r.GetProperty("hitTimeMs").GetInt32(),
                r.GetProperty("coolTimeMs").GetInt32(),
                r.GetProperty("reuseDelayMs").GetInt32(),
                r.GetProperty("castRange").GetInt32(),
                r.GetProperty("baseCritRate").GetInt32())
            {
                Blow = r.TryGetProperty("blowEffect", out var be)
                    ? be.GetString() == "backstab" ? BlowEffect.Backstab : BlowEffect.FatalBlow
                    : null,
                BlowChance = r.TryGetProperty("blowChance", out var bc) ? bc.GetInt32() : 0,
                FullLethal = r.TryGetProperty("fullLethal", out var fl) ? fl.GetInt32() : 0,
                HalfLethal = r.TryGetProperty("halfLethal", out var hl) ? hl.GetInt32() : 0,
                UsingKinds = r.TryGetProperty("usingKinds", out var uk) ? uk.EnumerateArray().Select(v => v.GetString() ?? "").ToArray() : [],
                MagicLevel = r.TryGetProperty("magicLevel", out var ml) ? ml.EnumerateArray().Select(v => v.GetInt32()).ToArray() : []
            };
        }
        return new SkillCombatCatalog(byId);
    }
}
