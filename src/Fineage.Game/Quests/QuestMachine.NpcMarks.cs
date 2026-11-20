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
                byte mark = journal.Find(quest.Id) switch
                {
                    { Phase: QuestPhase.Started } started when DialogCompletes(quest, dialog, started) => (byte)3,
                    { Phase: QuestPhase.Started } => (byte)2,
                    _ => (byte)1
                };
                marks[dialog.NpcId] = (byte)Math.Max(marks.GetValueOrDefault(dialog.NpcId), mark);
            }
        }

        return marks.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    static bool DialogCompletes(QuestDefinition quest, QuestDialog dialog, QuestProgress progress)
    {
        if (dialog.When != QuestWhen.Started)
            return false;
        if (dialog.Cond is int cond && cond != progress.Cond)
            return false;
        foreach (QuestOp op in dialog.Ops)
        {
            if (op is CompleteOp)
                return true;
        }
        return false;
    }
}