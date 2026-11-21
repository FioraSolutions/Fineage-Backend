using System;
using System.Text.Json;

namespace Fineage.Game.Skills;

/// <summary>Mobius <c>&lt;player charges="N"/&gt;</c> inside skill cast conditions (Sonic Blaster, etc.).</summary>
public static class SkillCastConditionCharges
{
    public static int Required(SkillCastCondition? root, int skillLevel = 1)
    {
        if (root == null)
            return 0;
        return Walk(root, skillLevel);
    }

    static int Walk(SkillCastCondition node, int skillLevel)
    {
        if (string.Equals(node.Op, "player", StringComparison.OrdinalIgnoreCase)
            && node.Player != null)
        {
            if (node.Player.TryGetValue("chargesByLevel", out var byLv)
                && byLv.ValueKind == JsonValueKind.Array
                && byLv.GetArrayLength() > 0)
            {
                int idx = Math.Clamp(skillLevel - 1, 0, byLv.GetArrayLength() - 1);
                return byLv[idx].GetInt32();
            }

            if (node.Player.TryGetValue("charges", out var el))
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int c))
                    return c;
                if (el.ValueKind == JsonValueKind.String
                    && int.TryParse(el.GetString(), out c))
                    return c;
            }
        }

        int max = 0;
        if (node.Items != null)
        {
            foreach (var child in node.Items)
                max = Math.Max(max, Walk(child, skillLevel));
        }

        if (node.Item != null)
            max = Math.Max(max, Walk(node.Item, skillLevel));

        return max;
    }
}