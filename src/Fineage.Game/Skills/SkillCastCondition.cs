using System.Text.Json;

namespace Fineage.Game.Skills;

/// <summary>
/// Mobius skill <c>&lt;conditions&gt;</c> tree imported from datapack XML (and/or/not, using, player, target, game).
/// </summary>
public sealed class SkillCastCondition
{
    public required string Op { get; init; }
    public IReadOnlyList<SkillCastCondition>? Items { get; init; }
    public SkillCastCondition? Item { get; init; }
    public string[]? Kinds { get; init; }
    public string[]? Slots { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Player { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Target { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Game { get; init; }

    public static SkillCastCondition? Parse(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } el)
            return null;
        if (!el.TryGetProperty("op", out var opEl))
            return null;
        string op = (opEl.GetString() ?? "").Trim().ToLowerInvariant();
        if (op.Length == 0)
            return null;

        if (op is "and" or "or")
        {
            if (!el.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<SkillCastCondition>();
            foreach (var child in items.EnumerateArray())
            {
                if (Parse(child) is { } c)
                    list.Add(c);
            }
            if (list.Count == 0)
                return null;
            return new SkillCastCondition { Op = op, Items = list };
        }

        if (op == "not")
        {
            if (!el.TryGetProperty("item", out var item) || Parse(item) is not { } inner)
                return null;
            return new SkillCastCondition { Op = op, Item = inner };
        }

        if (op == "using")
        {
            string[]? kinds = null;
            if (el.TryGetProperty("kinds", out var k) && k.ValueKind == JsonValueKind.Array)
                kinds = k.EnumerateArray().Select(x => (x.GetString() ?? "").Trim().ToUpperInvariant())
                    .Where(x => x.Length > 0).ToArray();
            string[]? slots = null;
            if (el.TryGetProperty("slots", out var s) && s.ValueKind == JsonValueKind.Array)
                slots = s.EnumerateArray().Select(x => (x.GetString() ?? "").Trim().ToLowerInvariant())
                    .Where(x => x.Length > 0).ToArray();
            if ((kinds == null || kinds.Length == 0) && (slots == null || slots.Length == 0))
                return null;
            return new SkillCastCondition { Op = op, Kinds = kinds, Slots = slots };
        }

        if (op == "player")
            return WithAttrs(op, el, dict => new SkillCastCondition { Op = op, Player = dict });

        if (op == "target")
            return WithAttrs(op, el, dict => new SkillCastCondition { Op = op, Target = dict });

        if (op == "game")
            return WithAttrs(op, el, dict => new SkillCastCondition { Op = op, Game = dict });

        return null;
    }

    static SkillCastCondition? WithAttrs(
        string op,
        JsonElement el,
        Func<IReadOnlyDictionary<string, JsonElement>, SkillCastCondition> build)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in el.EnumerateObject())
        {
            if (prop.NameEquals("op"))
                continue;
            dict[prop.Name] = prop.Value.Clone();
        }
        return dict.Count == 0 ? new SkillCastCondition { Op = op } : build(dict);
    }
}