using System.Text;

namespace Fineage.Game.Quests;

public sealed partial class QuestMachine
{
    static bool WriteJournal(QuestJournal journal, int questId, Plan plan)
    {
        var current = journal.Find(questId);
        if (plan.Delete || !plan.Present)
        {
            if (current == null)
                return false;
            journal.Remove(questId);
            return true;
        }

        if (current == null)
        {
            var created = journal.Add(questId, plan.Phase);
            created.Replace(plan.Phase, plan.Vars);
            return true;
        }

        bool same = current.Phase == plan.Phase && SameVars(current.Vars, plan.Vars);
        current.Replace(plan.Phase, plan.Vars);
        return !same;
    }

    static bool SameVars(IReadOnlyDictionary<string, string> left, Dictionary<string, string> right)
    {
        if (left.Count != right.Count)
            return false;
        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || value != pair.Value)
                return false;
        }

        return true;
    }

    static void WipeQuestItems(QuestDefinition quest, PlayerInventory inventory, Plan plan)
    {
        foreach (int itemId in quest.QuestItems)
        {
            int have = inventory.CountOf(itemId) + plan.Delta.GetValueOrDefault(itemId);
            if (have > 0)
                plan.Delta[itemId] = plan.Delta.GetValueOrDefault(itemId) - have;
        }
    }

    static bool AddDelta(PlayerInventory inventory, Plan plan, int itemId, int delta)
    {
        int have = inventory.CountOf(itemId) + plan.Delta.GetValueOrDefault(itemId);
        if (delta < 0 && have < -delta)
            return false;
        if (delta > 0 && have > int.MaxValue - delta)
            return false;
        plan.Delta[itemId] = plan.Delta.GetValueOrDefault(itemId) + delta;
        return true;
    }

    static bool Started(Plan plan) => plan.Present && !plan.Delete && plan.Phase == QuestPhase.Started;

    static int CondOf(Plan plan) =>
        plan.Vars.TryGetValue(QuestProgress.CondVar, out var text) && int.TryParse(text, out int cond) ? cond : 0;

    QuestOutcome Default(int npcId, string npcName, QuestJournal journal, QuestActor? actor)
    {
        var links = new List<(int Id, string Name)>();
        foreach (var quest in _catalog.Quests)
        {
            if (Find(quest, QuestHook.Talk, npcId, "", journal.Find(quest.Id), actor) != null)
                links.Add((quest.Id, quest.Name));
        }

        return new QuestOutcome
        {
            Html = DefaultHtml(npcName, links),
            Hook = "default"
        };
    }
}
