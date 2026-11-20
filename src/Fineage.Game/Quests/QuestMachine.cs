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

    public QuestDefinition? QuestDef(int questId) => _catalog.Find(questId);

    public QuestOutcome FirstTalk(QuestJournal journal, PlayerInventory inventory, int npcId, string npcName, QuestActor? actor = null)
    {
        foreach (var quest in _catalog.Quests)
        {
            var dialog = Find(quest, QuestHook.FirstTalk, npcId, "", journal.Find(quest.Id), actor, inventory, _random);
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
        var dialog = Find(quest, QuestHook.Talk, npcId, "", journal.Find(quest.Id), actor, inventory, _random);
        if (dialog == null)
            return Fail(QuestFailure.State, "That quest event does not apply.", questId, "talk");
        return Apply(journal, inventory, quest, dialog, "talk");
    }

    public QuestOutcome AdvEvent(QuestJournal journal, PlayerInventory inventory, int questId, int npcId, string eventName, QuestActor? actor = null)
    {
        if (_catalog.Find(questId) is not { } quest)
            return Fail(QuestFailure.Unknown, "That quest does not exist.", questId, "event", eventName);
        var dialog = Find(quest, QuestHook.Event, npcId, eventName, journal.Find(quest.Id), actor, inventory, _random);
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

    public QuestOutcome OnKill(QuestJournal journal, PlayerInventory inventory, int npcId, QuestActor? actor) =>
        OnNpc(journal, inventory, npcId, actor, QuestHook.Kill, "kill");

    /// <summary>Mobius <c>onAttack</c> steps for this NPC (e.g. Q234 Pipette Knife on Baium).</summary>
    public QuestOutcome OnAttack(QuestJournal journal, PlayerInventory inventory, int npcId, QuestActor? actor) =>
        OnNpc(journal, inventory, npcId, actor, QuestHook.Attack, "attack");

    /// <summary>True when some quest has an attack step for this NPC (cheap pre-check for the hit path).</summary>
    public bool HasAttackSteps(int npcId) =>
        (_attackNpcs ??= _catalog.Quests.SelectMany(static q => q.Dialogs)
            .Where(static d => d.Hook == QuestHook.Attack).Select(static d => d.NpcId).ToHashSet()).Contains(npcId);

    HashSet<int>? _attackNpcs;

    /// <summary>
    /// Mobius <c>executeForEachPlayer</c>: a party member near the corpse (not the killer, who already ran
    /// <see cref="OnKill(QuestJournal, PlayerInventory, int, QuestActor?)"/>) gets the <c>everyone</c> kill steps.
    /// </summary>
    public QuestOutcome OnEveryoneKill(QuestJournal journal, PlayerInventory inventory, int npcId, QuestActor? actor) =>
        OnNpc(journal, inventory, npcId, actor, QuestHook.Kill, "kill", everyoneOnly: true);

    /// <summary>True when some quest has an <c>everyone</c> kill step for this NPC.</summary>
    public bool HasEveryoneSteps(int npcId) =>
        (_everyoneNpcs ??= _catalog.Quests.SelectMany(static q => q.Dialogs)
            .Where(static d => d.Hook == QuestHook.Kill && d.Everyone).Select(static d => d.NpcId).ToHashSet()).Contains(npcId);

    HashSet<int>? _everyoneNpcs;

    /// <summary>
    /// A quest-owned spawn died or was hit by its owner: run the <c>owned</c> steps against the owner's journal.
    /// <paramref name="solo"/> = the owner did it outside a party (Mobius <c>st == st2 &amp;&amp; !isInParty()</c>).
    /// </summary>
    public QuestOutcome OnOwned(QuestJournal ownerJournal, PlayerInventory ownerInventory, int npcId, QuestActor? ownerActor,
        QuestHook hook, bool solo) =>
        OnNpc(ownerJournal, ownerInventory, npcId, ownerActor, hook, hook == QuestHook.Kill ? "kill" : "attack",
            ownedMode: solo ? "solo" : "any");

    /// <summary>True when some quest has owned steps for this NPC.</summary>
    public bool HasOwnedSteps(int npcId) =>
        (_ownedNpcs ??= _catalog.Quests.SelectMany(static q => q.Dialogs)
            .Where(static d => d.Owned != null).Select(static d => d.NpcId).ToHashSet()).Contains(npcId);

    HashSet<int>? _ownedNpcs;

    /// <summary>Writes a quest spawn's despawn / death vars into its owner's quest row (no-op when the row is gone).</summary>
    public bool ApplySpawnVars(QuestJournal ownerJournal, int questId, IReadOnlyDictionary<string, string>? vars)
    {
        if (vars is not { Count: > 0 } || StartedRow(ownerJournal, questId) is not { } row)
            return false;
        var plan = new Plan(row);
        foreach (var pair in vars)
            plan.Vars[pair.Key] = pair.Value;
        return WriteJournal(ownerJournal, questId, plan);
    }

    /// <summary>
    /// Summons do not survive a relog or restart: put every started quest back to the state its summons' despawn
    /// vars describe (saga <c>spawned</c> / <c>Quest0</c>), so the step can be redone.
    /// </summary>
    public bool ResetSpawnVars(QuestJournal journal)
    {
        bool changed = false;
        foreach (var row in journal.All().ToList())
        {
            if (row.Phase != QuestPhase.Started || _catalog.Find(row.QuestId) is not { } quest)
                continue;
            Dictionary<string, string>? vars = null;
            foreach (var dialog in quest.Dialogs)
            {
                foreach (var op in dialog.Ops)
                {
                    if (op is SummonOp { Summon.GoneVars: { } gone })
                    {
                        foreach (var pair in gone)
                            (vars ??= new(StringComparer.Ordinal))[pair.Key] = pair.Value;
                    }
                }
            }

            changed |= ApplySpawnVars(journal, row.QuestId, vars);
        }

        return changed;
    }

    static QuestProgress? StartedRow(QuestJournal journal, int questId) =>
        journal.Find(questId) is { Phase: QuestPhase.Started } row ? row : null;

    QuestOutcome OnNpc(QuestJournal journal, PlayerInventory inventory, int npcId, QuestActor? actor, QuestHook hook,
        string label, bool everyoneOnly = false, string? ownedMode = null)
    {
        QuestOutcome? last = null;
        var sounds = new List<string>();
        var lines = new List<QuestLine>();
        var items = new List<BagTouch>();
        var spawns = new List<int>();
        var spawnsAt = new List<QuestSpawnPoint>();
        var extras = new QuestExtras();
        long exp = 0;
        int sp = 0;
        bool changed = false;
        foreach (var quest in _catalog.Quests)
        {
            var dialog = Find(quest, hook, npcId, "", journal.Find(quest.Id), actor, inventory, _random, ownedMode);
            if (dialog == null || dialog.Party || (everyoneOnly && !dialog.Everyone))
                continue;
            var outcome = Apply(journal, inventory, quest, dialog, label);
            if (!outcome.Ok)
                return outcome;
            sounds.AddRange(outcome.Sounds);
            lines.AddRange(outcome.Lines);
            items.AddRange(outcome.Items);
            spawns.AddRange(outcome.Spawns);
            spawnsAt.AddRange(outcome.SpawnsAt);
            extras.MergeFrom(outcome.Extras);
            exp += outcome.Exp;
            sp += outcome.Sp;
            changed |= outcome.JournalChanged;
            last = outcome;
        }

        if (last == null)
        {
            return new QuestOutcome { Hook = label };
        }

        return new QuestOutcome
        {
            Html = last.Html,
            QuestId = last.QuestId,
            Hook = label,
            JournalChanged = changed,
            Sounds = sounds,
            Lines = lines,
            Items = items,
            Spawns = spawns,
            SpawnsAt = spawnsAt,
            Extras = extras.Empty ? QuestExtras.None : extras,
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
        var dialog = Find(quest, QuestHook.Kill, npcId, "", journal.Find(quest.Id), actor, inventory, _random);
        if (dialog == null)
            return new QuestOutcome { Hook = "kill" };
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

        var rewardSets = new List<QuestJournalRewardSet>();
        foreach (var row in active)
        {
            if (_catalog.Find(row.QuestId)?.JournalRewards is { Count: > 0 } rewards)
                rewardSets.Add(new QuestJournalRewardSet(row.QuestId, rewards));
        }

        return new QuestListSnapshot
        {
            Active = active.ToArray(),
            CompletedMask = mask,
            CompletedQuestIds = completed.ToArray(),
            MaxActive = _catalog.MaxActive,
            RewardSets = rewardSets.ToArray()
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
            Html = dialog.HtmlByVar is { } byVar && plan.Vars.TryGetValue(byVar.Var, out var pick) &&
                   byVar.Pages.TryGetValue(pick, out var page)
                ? page
                : dialog.Html,
            QuestId = quest.Id,
            Hook = hook,
            Event = dialog.Event,
            JournalChanged = outcome.JournalChanged,
            Sounds = outcome.Sounds,
            Lines = outcome.Lines,
            Items = outcome.Items,
            Exp = outcome.Exp,
            Sp = outcome.Sp,
            Spawns = outcome.Spawns,
            SpawnsAt = outcome.SpawnsAt,
            Noble = outcome.Noble,
            Teleport = outcome.Teleport,
            Social = outcome.Social,
            Extras = outcome.Extras
        };
    }
}
