using System.Text;

namespace Fineage.Game.Quests;

public sealed partial class QuestMachine
{
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
}
