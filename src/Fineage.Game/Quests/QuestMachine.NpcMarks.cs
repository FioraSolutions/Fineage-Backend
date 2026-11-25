namespace Fineage.Game.Quests;

public sealed partial class QuestMachine
{
    public IReadOnlyList<(int NpcId, byte Mark)> CollectNpcMarks(QuestJournal journal, QuestActor? actor)
    {
        var marks = new Dictionary<int, byte>();
        foreach (var quest in _catalog.Quests)
        {
            var state = journal.Find(quest.Id);
            // One gated lookup per (hook, npc, event) key instead of one per step: a step is marked when any step of
            // its key reaches the character, exactly as before, but the lookup only walks that key's group.
            foreach (var (key, group) in quest.DialogsByKey)
            {
                if (key.NpcId <= 0)
                    continue;
                if (Find(quest, key.Hook, key.NpcId, key.Event, state, actor) == null)
                    continue;
                foreach (var dialog in group)
                {
                    byte mark = state switch
                    {
                        { Phase: QuestPhase.Started } started when DialogCompletes(quest, dialog, started) => (byte)3,
                        { Phase: QuestPhase.Started } => (byte)2,
                        _ => (byte)1
                    };
                    marks[key.NpcId] = (byte)Math.Max(marks.GetValueOrDefault(key.NpcId), mark);
                }
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