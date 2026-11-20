namespace Fineage.Game.Quests;

public sealed partial class QuestMachine
{
    public IReadOnlyList<(int NpcId, byte Mark)> CollectNpcMarks(QuestJournal journal, QuestActor? actor)
    {
        var marks = new Dictionary<int, byte>();
        foreach (var quest in _catalog.Quests)
        {
            foreach (var dialog in quest.Dialogs)
            {
                if (dialog.NpcId <= 0)
                    continue;
                if (Find(quest, dialog.Hook, dialog.NpcId, dialog.Event ?? "", journal.Find(quest.Id), actor) == null)
                    continue;
                byte mark = journal.Find(quest.Id) is { Phase: QuestPhase.Started } ? (byte)2 : (byte)1;
                marks[dialog.NpcId] = (byte)Math.Max(marks.GetValueOrDefault(dialog.NpcId), mark);
            }
        }

        return marks.Select(kv => (kv.Key, kv.Value)).ToList();
    }
}