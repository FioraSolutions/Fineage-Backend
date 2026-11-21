using System.Text.Json;

namespace Fineage.Game.Tests;

/// <summary>Mobius npc-ai-support skill ids may be merged into skills.json without snapshot rows.</summary>
static class SkillParityTestHelpers
{
    public static HashSet<int> NpcAiSupportSkillIds()
    {
        var path = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "npc-ai-support.json"));
        if (!File.Exists(path))
            return [];
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var ids = new HashSet<int>();
        if (!doc.RootElement.TryGetProperty("npcs", out var npcs) || npcs.ValueKind != JsonValueKind.Array)
            return ids;
        foreach (var npc in npcs.EnumerateArray())
        {
            foreach (string scope in new[] { "heal", "buff", "debuff", "immobilize", "silence", "suicide" })
            {
                if (!npc.TryGetProperty(scope, out var arr) || arr.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var row in arr.EnumerateArray())
                {
                    if (row.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int sid) && sid > 0)
                        ids.Add(sid);
                }
            }
        }

        return ids;
    }
}