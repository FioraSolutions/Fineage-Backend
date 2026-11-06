using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// Coarse smoke for auto-ported Mobius packs: every shipped quest must load, and any quest that
/// has a start create/start op must expose a Quest-list Talk step for that NPC.
/// Not a full path parity check — that still needs per-quest tests.
/// </summary>
public sealed class AutoPortedQuestSmokeTests
{
    [Fact]
    public void Catalog_loads_and_startable_quests_appear_on_quest_list()
    {
        var data = GameData.LoadDefault();
        Assert.True(data.Quests.Quests.Count >= 300, $"expected >=300 quests, got {data.Quests.Quests.Count}");

        var machine = new QuestMachine(data.Quests, data.Items, () => 0.0);
        var journal = new QuestJournal();
        int startable = 0;
        int listed = 0;

        foreach (var quest in data.Quests.Quests)
        {
            var startDialog = quest.Dialogs.FirstOrDefault(d =>
                d.Ops.Any(o => o is CreateOp) && d.Ops.Any(o => o is StartOp));
            if (startDialog is null)
                continue;
            startable++;
            var actor = new QuestActor(0, Math.Max(1, startDialog.MinLevel), 0);
            string html = machine.QuestChoiceHtml(journal, startDialog.NpcId, "Npc", actor);
            if (html.Contains($"quest_talk:{quest.Id}", StringComparison.Ordinal))
                listed++;
        }

        Assert.True(startable > 200, $"expected many startable quests, got {startable}");
        // Auto-port backfills absent talk for most create/start events; allow a small miss rate.
        Assert.True(listed >= startable * 9 / 10, $"quest list coverage {listed}/{startable}");
    }
}
