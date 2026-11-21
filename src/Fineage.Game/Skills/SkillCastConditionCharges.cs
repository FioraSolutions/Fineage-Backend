using System;

namespace Fineage.Game.Skills;

/// <summary>Mobius <c>&lt;player charges="N"/&gt;</c> inside skill cast conditions (Sonic Blaster, etc.).</summary>
public static class SkillCastConditionCharges
{
    public static int Required(SkillCastCondition? root)
    {
        if (root == null)
            return 0;
        return Walk(root);
    }

    static int Walk(SkillCastCondition node)
    {
        if (string.Equals(node.Op, "player", StringComparison.OrdinalIgnoreCase)
            && node.Player != null
            && node.Player.TryGetValue("charges", out var el))
        {
            if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetInt32(out int c))
                return c;
            if (el.ValueKind == System.Text.Json.JsonValueKind.String
                && int.TryParse(el.GetString(), out c))
                return c;
        }

        int max = 0;
        if (node.Items != null)
        {
            foreach (var child in node.Items)
                max = Math.Max(max, Walk(child));
        }

        if (node.Item != null)
            max = Math.Max(max, Walk(node.Item));

        return max;
    }
}