using System.Text;

namespace Fineage.Game.Quests;

/// <summary>
/// Runs datapack steps. Rules follow Mobius Interlude quest state, not its classes:
/// created / started / completed, cond and string variables, one active cap,
/// one-time completion kept, repeatable completion and abort deleted,
/// quest items removed on exit.
/// </summary>
public sealed partial class QuestMachine : IQuestKillHook
{
    readonly QuestCatalog _catalog;
    readonly ItemCatalog _items;

    readonly Func<double> _random;

    public QuestMachine(QuestCatalog catalog, ItemCatalog items, Func<double>? random = null)
    {
        _random = random ?? Random.Shared.NextDouble;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public int MaxActive => _catalog.MaxActive;

    public QuestOutcome FirstTalk(QuestJournal journal, PlayerInventory inventory, int npcId, string npcName, QuestActor? actor = null)
    {
        foreach (var quest in _catalog.Quests)
        {
            var dialog = Find(quest, QuestHook.FirstTalk, npcId, "", journal.Find(quest.Id), actor, inventory);
            if (dialog == null)
                continue;
            return Apply(journal, inventory, quest, dialog, "first_talk");
        }

        return Default(npcId, npcName, journal, actor);
    }

    /// <summary>Quest-list page for a Quest bypass with no script name. Same links as a default talk.</summary>
    public string QuestChoiceHtml(QuestJournal journal, int npcId, string npcName, QuestActor? actor = null) =>
        Default(npcId, npcName, journal, actor).Html;

    public QuestOutcome Talk(QuestJournal journal, PlayerInventory inventory, int questId, int npcId, QuestActor? actor = null)
    {
        if (_catalog.Find(questId) is not { } quest)
            return Fail(QuestFailure.Unknown, "That quest does not exist.", questId, "talk");
        var dialog = Find(quest, QuestHook.Talk, npcId, "", journal.Find(quest.Id), actor, inventory);
        if (dialog == null)
            return Fail(QuestFailure.State, "That quest event does not apply.", questId, "talk");
        return Apply(journal, inventory, quest, dialog, "talk");
    }

    public QuestOutcome AdvEvent(QuestJournal journal, PlayerInventory inventory, int questId, int npcId, string eventName, QuestActor? actor = null)
    {
        if (_catalog.Find(questId) is not { } quest)
            return Fail(QuestFailure.Unknown, "That quest does not exist.", questId, "event", eventName);
        var dialog = Find(quest, QuestHook.Event, npcId, eventName, journal.Find(quest.Id), actor, inventory);
        if (dialog == null)
            return Fail(QuestFailure.State, "That quest event does not apply.", questId, "event", eventName);
        return Apply(journal, inventory, quest, dialog, "event");
    }

    public QuestOutcome Abort(QuestJournal journal, PlayerInventory inventory, int questId)
    {
        if (_catalog.Find(questId) is not { } quest)
            return Fail(QuestFailure.Unknown, "That quest does not exist.", questId, "abort");
        var row = journal.Find(questId);
        if (row == null || row.Phase != QuestPhase.Started)
            return Fail(QuestFailure.State, "That quest has not been started.", questId, "abort");
        if (!quest.Abortable)
        {
            return Fail(
                QuestFailure.Abort,
                "That quest cannot be aborted.",
                questId,
                "abort",
                lines: [Line(QuestNotice.NotAbortable, $"{quest.Name} cannot be aborted.", quest.Name)]);
        }

        var plan = new Plan(row);
        plan.Delete = true;
        plan.Sounds.Add(QuestSounds.GiveUp);
        plan.Lines.Add(Line(QuestNotice.Cancelled, $"{quest.Name} has been cancelled.", quest.Name));
        WipeQuestItems(quest, inventory, plan);
        return Commit(journal, inventory, quest, plan, "abort", "");
    }

    public QuestOutcome OnKill(QuestJournal journal, PlayerInventory inventory, int npcId) => OnKill(journal, inventory, npcId, null);

    public QuestOutcome OnKill(QuestJournal journal, PlayerInventory inventory, int npcId, QuestActor? actor)
    {
        QuestOutcome? last = null;
        var sounds = new List<string>();
        var lines = new List<QuestLine>();
        var items = new List<BagTouch>();
        long exp = 0;
        int sp = 0;
        bool changed = false;
        foreach (var quest in _catalog.Quests)
        {
            var dialog = Find(quest, QuestHook.Kill, npcId, "", journal.Find(quest.Id), actor, inventory);
            if (dialog == null || dialog.Party)
                continue;
            var outcome = Apply(journal, inventory, quest, dialog, "kill");
            if (!outcome.Ok)
                return outcome;
            sounds.AddRange(outcome.Sounds);
            lines.AddRange(outcome.Lines);
            items.AddRange(outcome.Items);
            exp += outcome.Exp;
            sp += outcome.Sp;
            changed |= outcome.JournalChanged;
            last = outcome;
        }

        if (last == null)
        {
            return new QuestOutcome { Hook = "kill" };
        }

        return new QuestOutcome
        {
            Html = last.Html,
            QuestId = last.QuestId,
            Hook = "kill",
            JournalChanged = changed,
            Sounds = sounds,
            Lines = lines,
            Items = items,
            Exp = exp,
            Sp = sp
        };
    }

    /// <summary>Quests with a <c>party: true</c> kill step for this NPC (Mobius <c>getRandomPartyMember*</c> scripts).</summary>
    public IReadOnlyList<int> PartyKillQuests(int npcId)
    {
        var ids = new List<int>();
        foreach (var quest in _catalog.Quests)
        {
            foreach (var dialog in quest.Dialogs)
            {
                if (dialog.Hook == QuestHook.Kill && dialog.Party && dialog.NpcId == npcId)
                {
                    ids.Add(quest.Id);
                    break;
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Would this member get credit? State/cond/gates match and no capped <c>drop_item</c> is already full
    /// (Mobius <c>checkPartyMember</c>, e.g. Q316 skips members who already hold the Varool fang).
    /// </summary>
    public bool PartyKillApplies(QuestJournal journal, PlayerInventory inventory, int questId, int npcId, QuestActor? actor)
    {
        if (_catalog.Find(questId) is not { } quest)
            return false;
        var dialog = Find(quest, QuestHook.Kill, npcId, "", journal.Find(quest.Id), actor, inventory);
        if (dialog is not { Party: true })
            return false;
        foreach (var op in dialog.Ops)
        {
            if (op is DropItemOp { Limit: > 0 } drop && inventory.CountOf(drop.ItemId) >= drop.Limit)
                return false;
        }

        return true;
    }

    public QuestOutcome OnPartyKill(QuestJournal journal, PlayerInventory inventory, int questId, int npcId, QuestActor? actor)
    {
        if (!PartyKillApplies(journal, inventory, questId, npcId, actor))
            return new QuestOutcome { Hook = "kill" };
        var quest = _catalog.Find(questId)!;
        var dialog = Find(quest, QuestHook.Kill, npcId, "", journal.Find(quest.Id), actor, inventory)!;
        return Apply(journal, inventory, quest, dialog, "kill");
    }

    public QuestListSnapshot List(QuestJournal journal)
    {
        var active = new List<QuestJournalEntry>();
        var completed = new List<int>();
        var mask = new byte[QuestJournal.CompletedMaskLength];
        foreach (var row in journal.All())
        {
            if (_catalog.Find(row.QuestId) is not { } quest)
                continue;
            if (row.Phase == QuestPhase.Started)
            {
                active.Add(new QuestJournalEntry(
                    quest.Id,
                    quest.Name,
                    (int)QuestPhase.Started,
                    row.Cond,
                    row.Flags,
                    row.JournalWord,
                    quest.Repeatable));
            }
            else if (row.Phase == QuestPhase.Completed && !quest.Repeatable)
            {
                completed.Add(quest.Id);
                SetCompletedBit(mask, quest.Id);
            }
        }

        return new QuestListSnapshot
        {
            Active = active.ToArray(),
            CompletedMask = mask,
            CompletedQuestIds = completed.ToArray(),
            MaxActive = _catalog.MaxActive
        };
    }

    public static void SetCompletedBit(byte[] mask, int questId)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (questId < 0)
            return;
        int index = questId / 8;
        if ((uint)index >= (uint)mask.Length)
            return;
        mask[index] |= (byte)(1 << (questId % 8));
    }

    QuestOutcome Apply(QuestJournal journal, PlayerInventory inventory, QuestDefinition quest, QuestDialog dialog, string hook)
    {
        var plan = new Plan(journal.Find(quest.Id));
        if (!TryOps(journal, inventory, quest, dialog.Ops, plan, out var failure))
            return failure;
        var outcome = Commit(journal, inventory, quest, plan, hook, dialog.Event);
        return new QuestOutcome
        {
            Failure = outcome.Failure,
            Message = outcome.Message,
            Html = dialog.Html,
            QuestId = quest.Id,
            Hook = hook,
            Event = dialog.Event,
            JournalChanged = outcome.JournalChanged,
            Sounds = outcome.Sounds,
            Lines = outcome.Lines,
            Items = outcome.Items,
            Exp = outcome.Exp,
            Sp = outcome.Sp,
            Spawns = outcome.Spawns
        };
    }
}
