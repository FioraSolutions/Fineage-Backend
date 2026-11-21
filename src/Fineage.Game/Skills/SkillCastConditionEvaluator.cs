using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Game.Skills;

/// <summary>
/// Mobius <c>Skill.checkCondition</c> — one evaluator for cast gates on skills (and items when wired).
/// Unknown attributes log via <see cref="SkillCastConditionContext.LogUnsupported"/> and fail closed.
/// </summary>
public static class SkillCastConditionEvaluator
{
    public static bool Met(SkillCastCondition? root, SkillCastConditionContext ctx)
    {
        if (root == null)
            return true;
        return Eval(root, ctx);
    }

    static bool Eval(SkillCastCondition node, SkillCastConditionContext ctx)
    {
        switch (node.Op)
        {
            case "and":
                foreach (var child in node.Items ?? [])
                {
                    if (!Eval(child, ctx))
                        return false;
                }
                return true;
            case "or":
                foreach (var child in node.Items ?? [])
                {
                    if (Eval(child, ctx))
                        return true;
                }
                return false;
            case "not":
                return node.Item != null && !Eval(node.Item, ctx);
            case "using":
                if (node.Slots is { Length: > 0 })
                    return ctx.UsingSlot(node.Slots);
                if (node.Kinds is { Length: > 0 })
                    return ctx.UsingKinds(node.Kinds);
                return false;
            case "player":
                return EvalPlayer(node.Player, ctx);
            case "target":
                return EvalTarget(node.Target, ctx);
            case "game":
                return EvalGame(node.Game, ctx);
            default:
                ctx.LogUnsupported($"skill-cast-condition unknown op={node.Op}");
                return false;
        }
    }

    static bool EvalPlayer(IReadOnlyDictionary<string, JsonElement>? attrs, SkillCastConditionContext ctx)
    {
        if (attrs == null || attrs.Count == 0)
            return true;
        foreach (var (key, val) in attrs)
        {
            if (!CheckPlayerAttr(key, val, attrs, ctx))
                return false;
        }
        return true;
    }

    static bool CheckPlayerAttr(
        string key,
        JsonElement val,
        IReadOnlyDictionary<string, JsonElement> attrs,
        SkillCastConditionContext ctx)
    {
        switch (key.ToLowerInvariant())
        {
            case "invsize":
                return Int(val, out int needSlots) && ctx.InventoryUsedSlots <= ctx.InventoryLimit - needSlots;
            case "weight":
                return Int(val, out int maxPct) && ctx.LoadPercent < maxPct;
            case "hp":
                return Int(val, out int hpPct) && ctx.HpPercent <= hpPct;
            case "mp":
                return Int(val, out int mpPct) && ctx.MpPercent <= mpPct;
            case "charges":
                return Int(val, out int needCharges) && ctx.SoulCharges >= needCharges;
            case "level":
                return Int(val, out int minLevel) && ctx.Level >= minLevel;
            case "levelrange":
                if (val.ValueKind != JsonValueKind.Array || val.GetArrayLength() < 2)
                    return false;
                return ctx.Level >= val[0].GetInt32() && ctx.Level <= val[1].GetInt32();
            case "sex":
                return Int(val, out int wantSex) && ctx.Sex == wantSex;
            case "races":
                if (val.ValueKind != JsonValueKind.Array)
                    return false;
                foreach (var r in val.EnumerateArray())
                {
                    if (r.GetString()?.Equals(ctx.RaceName, StringComparison.OrdinalIgnoreCase) == true)
                        return true;
                }
                return false;
            case "elementseeds":
                return Int(val, out int total) && ctx.ElementSeedTotal >= total;
            case "elementseed":
                string element = (val.GetString() ?? "").Trim();
                int need = attrs.TryGetValue("elementseedcount", out var cnt) && Int(cnt, out int c) ? c : 1;
                return ctx.ElementSeedCount(element) >= need;
            case "activeeffectid":
                return Int(val, out int effectId) &&
                       ctx.HasActiveEffect(effectId,
                           attrs.TryGetValue("activeeffectlevel", out var lv) && Int(lv, out int needLv) ? needLv : 1);
            case "activeeffectlevel":
                return true;
            case "activeskillid":
                return Int(val, out int skillId) &&
                       ctx.HasActiveSkill(skillId,
                           attrs.TryGetValue("activeskilllevel", out var slv) && Int(slv, out int plv) ? plv : 1);
            case "activeskilllevel":
                return true;
            case "insidezoneid":
                return Int(val, out int zoneId) && ctx.InsideZone(zoneId);
            case "cansweep":
                return Bool(val) && ctx.CanSweep();
            case "cansummon":
                if (!Bool(val))
                    return true;
                if (ctx.CanSummon())
                    return true;
                if (ctx.HasSummon)
                    ctx.SetRefusalMessage(SkillCastConditionRefusal.MultiplePetsOrServitors);
                return false;
            case "cansummonsiegegolem":
                return Bool(val) && ctx.CanSummonSiegeGolem();
            case "cancreatebase":
                return Bool(val) && ctx.CanCreateSiegeBase();
            case "cantakecastle":
                return Bool(val) && ctx.CanTakeCastle();
            case "canescape":
                return Bool(val) && ctx.CanEscape();
            case "canresurrect":
                return Bool(val) && ctx.CanResurrect();
            case "callpc":
                return Bool(val) && ctx.CanCallPc();
            case "siegezone":
                return Bool(val) && ctx.InSiegeZone();
            case "isridingstrider":
                return Bool(val) == ctx.RidingStrider;
            case "flying":
                return Bool(val) == ctx.Flying;
            case "resting":
                return Bool(val) == ctx.Resting;
            case "moving":
                return Bool(val) == ctx.Moving;
            case "running":
                return Bool(val) == ctx.Running;
            case "standing":
                return Bool(val) == ctx.Standing;
            case "behind":
                return Bool(val) == ctx.BehindTarget;
            case "front":
                return Bool(val) == ctx.FrontOfTarget;
            case "chaotic":
                return Bool(val) == ctx.Chaotic;
            case "clanhall":
                return Int(val, out int hallId) && ctx.ClanHallId == hallId;
            default:
                ctx.LogUnsupported($"skill-cast-condition unsupported player.{key}");
                return false;
        }
    }

    static bool EvalTarget(IReadOnlyDictionary<string, JsonElement>? attrs, SkillCastConditionContext ctx)
    {
        if (attrs == null || attrs.Count == 0)
            return true;
        foreach (var (key, val) in attrs)
        {
            switch (key.ToLowerInvariant())
            {
                case "race":
                    return string.Equals(ctx.TargetRace, (val.GetString() ?? "").Trim(),
                        StringComparison.OrdinalIgnoreCase);
                case "npcids":
                    if (val.ValueKind != JsonValueKind.Array)
                        return false;
                    foreach (var id in val.EnumerateArray())
                    {
                        if (id.GetInt32() == ctx.TargetNpcId)
                            return true;
                    }
                    return false;
                case "npctype":
                    return string.Equals(ctx.TargetNpcType, val.GetString(), StringComparison.OrdinalIgnoreCase);
                case "mypartyexceptme":
                    return Bool(val) && ctx.TargetIsPartyMateExceptSelf;
                case "activeskillid":
                    return Int(val, out int skillId) && ctx.TargetHasActiveSkill(skillId);
                default:
                    ctx.LogUnsupported($"skill-cast-condition unsupported target.{key}");
                    return false;
            }
        }
        return true;
    }

    static bool EvalGame(IReadOnlyDictionary<string, JsonElement>? attrs, SkillCastConditionContext ctx)
    {
        if (attrs == null || attrs.Count == 0)
            return true;
        foreach (var (key, val) in attrs)
        {
            switch (key.ToLowerInvariant())
            {
                case "time":
                    string t = (val.GetString() ?? "").Trim().ToLowerInvariant();
                    return t switch
                    {
                        "night" => ctx.IsNight,
                        "day" => !ctx.IsNight,
                        _ => false
                    };
                default:
                    ctx.LogUnsupported($"skill-cast-condition unsupported game.{key}");
                    return false;
            }
        }
        return true;
    }

    static bool Int(JsonElement el, out int value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value))
            return true;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out value))
            return true;
        return false;
    }

    static bool Bool(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.GetInt32() != 0,
            JsonValueKind.String => el.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            _ => false
        };
}

/// <summary>Mobius condition refusal system message ids surfaced to the zone layer.</summary>
public static class SkillCastConditionRefusal
{
    /// <summary>Mobius <c>SystemMessageId.YOU_MAY_NOT_USE_MULTIPLE_PETS_OR_SERVITORS_AT_THE_SAME_TIME</c>.</summary>
    public const int MultiplePetsOrServitors = 580;
}

/// <summary>Runtime facts for <see cref="SkillCastConditionEvaluator"/> (zone fills this per cast).</summary>
public abstract class SkillCastConditionContext
{
    public int? RefusalMessageId { get; private set; }

    public void SetRefusalMessage(int sysMsgId) => RefusalMessageId ??= sysMsgId;

    public abstract void LogUnsupported(string message);
    public abstract bool UsingKinds(string[] kinds);
    public abstract bool UsingSlot(string[] slots);
    public abstract int InventoryUsedSlots { get; }
    public abstract int InventoryLimit { get; }
    public abstract int LoadPercent { get; }
    public abstract int HpPercent { get; }
    public abstract int MpPercent { get; }
    public abstract int SoulCharges { get; }
    public abstract int Level { get; }
    public abstract int Sex { get; }
    public abstract string RaceName { get; }
    public abstract int ElementSeedTotal { get; }
    public abstract int ElementSeedCount(string element);
    public abstract bool HasActiveEffect(int skillId, int level);
    public abstract bool HasActiveSkill(int skillId, int level);
    public abstract bool InsideZone(int zoneId);
    public abstract bool CanSweep();
    public abstract bool CanSummon();
    public abstract bool HasSummon { get; }
    public abstract bool CanSummonSiegeGolem();
    public abstract bool CanCreateSiegeBase();
    public abstract bool CanTakeCastle();
    public abstract bool CanEscape();
    public abstract bool CanResurrect();
    public abstract bool CanCallPc();
    public abstract bool InSiegeZone();
    public abstract bool RidingStrider { get; }
    public abstract bool Flying { get; }
    public abstract bool Resting { get; }
    public abstract bool Moving { get; }
    public abstract bool Running { get; }
    public abstract bool Standing { get; }
    public abstract bool Chaotic { get; }
    public abstract int ClanHallId { get; }
    public abstract bool BehindTarget { get; }
    public abstract bool FrontOfTarget { get; }
    public abstract string TargetRace { get; }
    public abstract int TargetNpcId { get; }
    public abstract string TargetNpcType { get; }
    public abstract bool TargetIsPartyMateExceptSelf { get; }
    public abstract bool TargetHasActiveSkill(int skillId);
    public abstract bool IsNight { get; }
}