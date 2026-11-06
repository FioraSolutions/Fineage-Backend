using System.Text;

namespace Fineage.Game.Quests;

/// <summary>
/// Runs datapack steps. Rules follow Mobius Interlude quest state, not its classes:
/// created / started / completed, cond and string variables, one active cap,
/// one-time completion kept, repeatable completion and abort deleted,
/// quest items removed on exit.
/// </summary>
public sealed class QuestMachine : IQuestKillHook
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

    bool TryOps(
        QuestJournal journal,
        PlayerInventory inventory,
        QuestDefinition quest,
        IReadOnlyList<QuestOp> ops,
        Plan plan,
        out QuestOutcome failure)
    {
        failure = new QuestOutcome();
        foreach (var op in ops)
        {
            switch (op)
            {
                case CreateOp:
                    if (plan.Phase == QuestPhase.Completed && plan.Present && !plan.Delete)
                    {
                        failure = Fail(QuestFailure.State, "That quest is already finished.", quest.Id, "",
                            lines: [Line(QuestNotice.AlreadyFinished, $"{quest.Name} is already finished.", quest.Name)]);
                        return false;
                    }

                    if (!plan.Present || plan.Delete)
                    {
                        plan.Present = true;
                        plan.Delete = false;
                        plan.Phase = QuestPhase.Created;
                        plan.Vars.Clear();
                    }

                    break;
                case StartOp:
                    if (!plan.Present || plan.Delete || plan.Phase != QuestPhase.Created)
                    {
                        if (plan.Phase == QuestPhase.Completed && plan.Present && !plan.Delete)
                        {
                            failure = Fail(QuestFailure.State, "That quest is already finished.", quest.Id, "",
                                lines: [Line(QuestNotice.AlreadyFinished, $"{quest.Name} is already finished.", quest.Name)]);
                            return false;
                        }

                        failure = Fail(QuestFailure.State, "That quest cannot be started.", quest.Id, "");
                        return false;
                    }

                    if (journal.StartedCount >= _catalog.MaxActive)
                    {
                        failure = Fail(QuestFailure.Limit, "You cannot take on any more quests.", quest.Id, "",
                            lines: [Line(QuestNotice.TooMany, "You cannot take on any more quests.")]);
                        return false;
                    }

                    plan.Phase = QuestPhase.Started;
                    plan.Vars[QuestProgress.CondVar] = "1";
                    plan.Sounds.Add(QuestSounds.Accept);
                    plan.Lines.Add(Line(QuestNotice.Acquired, $"You have acquired the quest {quest.Name}.", quest.Name));
                    break;
                case SetCondOp setCond:
                    if (!Started(plan))
                    {
                        failure = Fail(QuestFailure.State, "That quest has not been started.", quest.Id, "");
                        return false;
                    }

                    int previous = CondOf(plan);
                    plan.Vars[QuestProgress.CondVar] = setCond.Cond.ToString();
                    if (setCond.Cond > previous)
                        plan.Sounds.Add(QuestSounds.Middle);
                    break;
                case SetVarOp setVar:
                    if (!plan.Present || plan.Delete || plan.Phase == QuestPhase.Completed)
                    {
                        failure = Fail(QuestFailure.State, "That quest cannot be updated.", quest.Id, "");
                        return false;
                    }

                    if (setVar.Key == QuestProgress.CondVar)
                    {
                        int before = CondOf(plan);
                        plan.Vars[setVar.Key] = setVar.Value;
                        if (int.Parse(setVar.Value) > before && plan.Phase == QuestPhase.Started)
                            plan.Sounds.Add(QuestSounds.Middle);
                    }
                    else
                    {
                        plan.Vars[setVar.Key] = setVar.Value;
                    }

                    break;
                case UnsetVarOp unset:
                    if (!plan.Present || plan.Delete || plan.Phase == QuestPhase.Completed)
                    {
                        failure = Fail(QuestFailure.State, "That quest cannot be updated.", quest.Id, "");
                        return false;
                    }

                    plan.Vars.Remove(unset.Key);
                    break;
                case GiveItemOp give:
                    if (!AddDelta(inventory, plan, give.ItemId, give.Count))
                    {
                        failure = Fail(QuestFailure.BadRequest, "That reward does not fit in the bag.", quest.Id, "");
                        return false;
                    }

                    string itemName = _items.Get(give.ItemId).Name;
                    plan.Sounds.Add(QuestSounds.ItemGet);
                    plan.Lines.Add(Line(
                        QuestNotice.ItemEarned,
                        $"You have earned {itemName} ({give.Count}).",
                        itemName,
                        give.Count.ToString()));
                    break;
                case GiveAdenaOp adena:
                    if (!AddDelta(inventory, plan, QuestCatalog.AdenaItemId, adena.Count))
                    {
                        failure = Fail(QuestFailure.BadRequest, "That reward does not fit in the bag.", quest.Id, "");
                        return false;
                    }

                    plan.Lines.Add(Line(
                        QuestNotice.Adena,
                        $"You have obtained {adena.Count} Adena.",
                        adena.Count.ToString()));
                    break;
                case GiveExpSpOp exp:
                    if (plan.Exp > long.MaxValue - exp.Exp)
                        plan.Exp = long.MaxValue;
                    else
                        plan.Exp += exp.Exp;
                    if (plan.Sp > int.MaxValue - exp.Sp)
                        plan.Sp = int.MaxValue;
                    else
                        plan.Sp += exp.Sp;
                    plan.Lines.Add(Line(
                        QuestNotice.ExpSp,
                        $"You have earned {exp.Exp} experience and {exp.Sp} SP.",
                        exp.Exp.ToString(),
                        exp.Sp.ToString()));
                    break;
                case TakeItemOp take:
                    if (!AddDelta(inventory, plan, take.ItemId, -take.Count))
                    {
                        failure = Fail(QuestFailure.Item, "You do not have the required item.", quest.Id, "");
                        return false;
                    }

                    break;
                case CompleteOp:
                    if (!Started(plan))
                    {
                        failure = Fail(QuestFailure.State, "That quest has not been started.", quest.Id, "");
                        return false;
                    }

                    if (quest.Repeatable)
                        plan.Delete = true;
                    else
                    {
                        plan.Phase = QuestPhase.Completed;
                        plan.Vars.Clear();
                        plan.Delete = false;
                        plan.Present = true;
                    }

                    WipeQuestItems(quest, inventory, plan);
                    plan.Sounds.Add(QuestSounds.Finish);
                    plan.Lines.Add(Line(QuestNotice.Completed, $"{quest.Name} is complete.", quest.Name));
                    break;
                case DropItemOp drop:
                {
                    if (!Started(plan))
                        break;
                    int have = inventory.CountOf(drop.ItemId) + plan.Delta.GetValueOrDefault(drop.ItemId);
                    if (drop.Limit > 0 && have >= drop.Limit)
                        break;
                    if (_random() >= drop.Chance)
                        break;
                    int add = drop.Limit > 0 ? Math.Min(drop.Count, drop.Limit - have) : drop.Count;
                    if (!AddDelta(inventory, plan, drop.ItemId, add))
                        break;
                    if (drop.Limit > 0 && have + add >= drop.Limit)
                    {
                        plan.Sounds.Add(QuestSounds.Middle);
                        if (drop.CondAtLimit is int atLimit)
                            plan.Vars[QuestProgress.CondVar] = atLimit.ToString();
                    }
                    else
                    {
                        plan.Sounds.Add(QuestSounds.ItemGet);
                    }

                    break;
                }
                case SellItemsOp sell:
                {
                    long pay = 0;
                    int total = 0;
                    foreach (var (itemId, adena) in sell.Items)
                    {
                        int have = inventory.CountOf(itemId) + plan.Delta.GetValueOrDefault(itemId);
                        if (have <= 0)
                            continue;
                        AddDelta(inventory, plan, itemId, -have);
                        total += have;
                        pay += (long)have * adena;
                    }

                    if (total == 0)
                    {
                        failure = Fail(QuestFailure.Item, "You do not have the required item.", quest.Id, "");
                        return false;
                    }

                    if (total >= sell.BonusMin && sell.BonusAdena > 0)
                        pay += sell.BonusAdena;
                    int payInt = (int)Math.Min(int.MaxValue, pay);
                    if (payInt > 0 && AddDelta(inventory, plan, QuestCatalog.AdenaItemId, payInt))
                        plan.Lines.Add(Line(QuestNotice.Adena, $"You have obtained {payInt} Adena.", payInt.ToString()));
                    break;
                }
                case ExchangeAllOp exchange:
                {
                    int handed = inventory.CountOf(exchange.ItemId) + plan.Delta.GetValueOrDefault(exchange.ItemId);
                    if (handed <= 0)
                    {
                        failure = Fail(QuestFailure.Item, "You do not have the required item.", quest.Id, "");
                        return false;
                    }

                    AddDelta(inventory, plan, exchange.ItemId, -handed);
                    int reward = handed * exchange.Per;
                    if (!AddDelta(inventory, plan, exchange.RewardItemId, reward))
                    {
                        failure = Fail(QuestFailure.BadRequest, "That reward does not fit in the bag.", quest.Id, "");
                        return false;
                    }

                    string rewardName = _items.Get(exchange.RewardItemId).Name;
                    plan.Sounds.Add(QuestSounds.ItemGet);
                    plan.Lines.Add(Line(QuestNotice.ItemEarned, $"You have earned {rewardName} ({reward}).", rewardName, reward.ToString()));
                    if (exchange.BonusAdena > 0 && handed >= exchange.BonusMin)
                    {
                        AddDelta(inventory, plan, QuestCatalog.AdenaItemId, exchange.BonusAdena);
                        plan.Lines.Add(Line(QuestNotice.Adena, $"You have obtained {exchange.BonusAdena} Adena.", exchange.BonusAdena.ToString()));
                    }

                    break;
                }
                case SoundOp sound:
                    plan.Sounds.Add(sound.Name);
                    break;
                case SpawnNpcOp spawn:
                    plan.Spawns.Add(spawn.NpcId);
                    break;
            }
        }

        return true;
    }

    QuestOutcome Commit(QuestJournal journal, PlayerInventory inventory, QuestDefinition quest, Plan plan, string hook, string evt)
    {
        bool changed = WriteJournal(journal, quest.Id, plan);
        var touches = new List<BagTouch>();
        foreach (int itemId in plan.Delta.Keys.Order())
        {
            int delta = plan.Delta[itemId];
            if (delta > 0)
            {
                // A stack merge keeps the held objectId: the client needs Modify, not Add (same rule as pickup, 2ef5c42).
                var before = new HashSet<int>(inventory.Items.Select(i => i.ObjectId));
                foreach (var piece in inventory.AddPieces(itemId, delta))
                    touches.Add(new BagTouch(before.Contains(piece.ObjectId) ? BagTouchKind.Modify : BagTouchKind.Add, piece));
            }
            else if (delta < 0)
            {
                if (!inventory.TryTake(itemId, -delta, touches))
                    throw new InvalidOperationException($"Quest {quest.Id} removed item {itemId} that was no longer in the bag.");
            }
        }

        return new QuestOutcome
        {
            Html = "",
            QuestId = quest.Id,
            Hook = hook,
            Event = evt,
            JournalChanged = changed,
            Sounds = plan.Sounds,
            Lines = plan.Lines,
            Items = touches,
            Exp = plan.Exp,
            Sp = plan.Sp,
            Spawns = plan.Spawns
        };
    }

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

    static QuestDialog? Find(QuestDefinition quest, QuestHook hook, int npcId, string eventName, QuestProgress? state, QuestActor? actor = null, PlayerInventory? inventory = null)
    {
        foreach (var dialog in quest.Dialogs)
        {
            if (dialog.Hook != hook || dialog.NpcId != npcId)
                continue;
            if (hook == QuestHook.Event && !string.Equals(dialog.Event, eventName, StringComparison.Ordinal))
                continue;
            if (!WhenMatches(dialog, state))
                continue;
            if (!ActorMatches(dialog, actor))
                continue;
            if (dialog.HasItem != 0 && inventory != null && inventory.CountOf(dialog.HasItem) < dialog.MinCount)
                continue;
            if (!ItemsMatch(dialog, inventory))
                continue;
            return dialog;
        }

        return null;
    }

    /// <summary>
    /// Race / level gates. Datapacks list the passing branch first and the refusal pages after,
    /// mirroring Mobius <c>race != ELF ? 01 : level &lt; MIN ? 02 : 03</c>. No actor (legacy callers) skips gates.
    /// </summary>
    static bool ActorMatches(QuestDialog dialog, QuestActor? actor)
    {
        if (actor is not { } who)
            return true;
        if (dialog.Races is { Count: > 0 } races && !races.Contains(who.Race))
            return false;
        if (dialog.MinLevel > 0 && who.Level < dialog.MinLevel)
            return false;
        if (dialog.MaxLevel > 0 && who.Level > dialog.MaxLevel)
            return false;
        if (dialog.Classes is { Count: > 0 } classes && !classes.Contains(who.ClassId))
            return false;
        return true;
    }

    /// <summary>
    /// Mobius path quests branch on <c>hasQuestItems(a, b, ...)</c> (all held) and <c>!hasAtLeastOneQuestItem(...)</c> (none held).
    /// No inventory (quest-list probe) skips item gates, as <c>hasItem</c> does.
    /// </summary>
    static bool ItemsMatch(QuestDialog dialog, PlayerInventory? inventory)
    {
        if (inventory == null)
            return true;
        foreach (int id in dialog.HasItems ?? [])
        {
            if (inventory.CountOf(id) <= 0)
                return false;
        }

        foreach (int id in dialog.LacksItems ?? [])
        {
            if (inventory.CountOf(id) > 0)
                return false;
        }

        // Mobius getQuestItemsCount(id) >= n for several items at once.
        foreach (var pair in dialog.AtLeast ?? new Dictionary<int, int>())
        {
            if (inventory.CountOf(pair.Key) < pair.Value)
                return false;
        }

        return true;
    }

    static bool WhenMatches(QuestDialog dialog, QuestProgress? state)
    {
        switch (dialog.When)
        {
            case QuestWhen.Absent:
                if (state != null)
                    return false;
                break;
            case QuestWhen.Any:
                break;
            case QuestWhen.Created:
                if (state == null || state.Phase != QuestPhase.Created)
                    return false;
                break;
            case QuestWhen.Started:
                if (state == null || state.Phase != QuestPhase.Started)
                    return false;
                break;
            case QuestWhen.Completed:
                if (state == null || state.Phase != QuestPhase.Completed)
                    return false;
                break;
        }

        if (dialog.Cond is int cond && (state == null || state.Cond != cond))
            return false;
        if (dialog.WhenVar != null)
        {
            if (state == null || state.Get(dialog.WhenVar) != dialog.WhenValue)
                return false;
        }

        return true;
    }

    static string DefaultHtml(string npcName, IReadOnlyList<(int Id, string Name)> links)
    {
        var html = new StringBuilder();
        html.Append("<html><body>").Append(Escape(npcName)).Append(":<br>");
        if (links.Count == 0)
            html.Append("I have nothing for you.");
        else
        {
            foreach (var link in links)
            {
                html.Append("<a action=\"quest_talk:").Append(link.Id).Append("\">")
                    .Append(Escape(link.Name)).Append("</a><br>");
            }
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    static QuestLine Line(QuestNotice notice, string text, params string[] args) => new(notice, text, args);

    static QuestOutcome Fail(
        QuestFailure failure,
        string message,
        int questId,
        string hook,
        string evt = "",
        IReadOnlyList<QuestLine>? lines = null) => new()
        {
            Failure = failure,
            Message = message,
            QuestId = questId,
            Hook = hook,
            Event = evt,
            Lines = lines ?? []
        };

    sealed class Plan
    {
        public Plan(QuestProgress? current)
        {
            if (current == null)
            {
                Present = false;
                Phase = QuestPhase.Created;
                Vars = new Dictionary<string, string>(StringComparer.Ordinal);
                return;
            }

            Present = true;
            Phase = current.Phase;
            Vars = new Dictionary<string, string>(current.Vars, StringComparer.Ordinal);
        }

        public bool Present;
        public bool Delete;
        public QuestPhase Phase;
        public Dictionary<string, string> Vars;
        public Dictionary<int, int> Delta { get; } = [];
        public List<string> Sounds { get; } = [];
        public List<QuestLine> Lines { get; } = [];
        public List<int> Spawns { get; } = [];
        public long Exp;
        public int Sp;
    }
}
