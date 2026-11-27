namespace Fineage.Game.Quests;

public sealed partial class QuestMachine
{
    /// <summary>
    /// Start marks of every quest the journal does not hold, per actor. With no journal row and no inventory the gates
    /// read only the dialog data and the actor (<see cref="Reaches"/>), so the answer is the same for every character
    /// of that race, level, and class. The catalog is immutable. Every enter used to walk the whole catalog (about 400
    /// quests) on the simulation thread. Under reconnect churn the enter's quest-client-fx request took up to 210 ms of
    /// a 200 ms step in Debug (#1116).
    /// </summary>
    readonly System.Collections.Concurrent.ConcurrentDictionary<(bool HasActor, QuestActor Actor), (int QuestId, int NpcId)[]> _unstartedMarks = new();

    public IReadOnlyList<(int NpcId, byte Mark)> CollectNpcMarks(QuestJournal journal, QuestActor? actor)
    {
        var marks = new Dictionary<int, byte>();
        foreach (var (questId, npcId) in UnstartedMarks(actor))
        {
            if (journal.Find(questId) == null)
                marks[npcId] = Math.Max(marks.GetValueOrDefault(npcId), (byte)1);
        }

        foreach (var row in journal.All())
        {
            if (_catalog.Find(row.QuestId) is not { } quest || journal.Find(quest.Id) is not { } state)
                continue;
            CollectQuestMarks(quest, state, actor, marks);
        }

        return marks.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    (int QuestId, int NpcId)[] UnstartedMarks(QuestActor? actor)
    {
        // The probe never rolls, so the actor's random source is not part of the answer (and must not grow the cache).
        var key = actor is { } a ? (true, a with { Roll = null }) : (false, default(QuestActor));
        return _unstartedMarks.GetOrAdd(key, static (k, catalog) =>
        {
            var found = new List<(int QuestId, int NpcId)>();
            QuestActor? probe = k.HasActor ? k.Actor : null;
            foreach (var quest in catalog.Quests)
            {
                foreach (var (dialogKey, group) in quest.DialogsByKey)
                {
                    if (dialogKey.NpcId <= 0 || group.Count == 0)
                        continue;
                    if (Find(quest, dialogKey.Hook, dialogKey.NpcId, dialogKey.Event, null, probe) != null)
                        found.Add((quest.Id, dialogKey.NpcId));
                }
            }

            return found.ToArray();
        }, _catalog);
    }

    void CollectQuestMarks(QuestDefinition quest, QuestProgress state, QuestActor? actor, Dictionary<int, byte> marks)
    {
        // One gated lookup per (hook, npc, event) key instead of one per step: a step is marked when any step of
        // its key reaches the character, but the lookup only walks that key's group.
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