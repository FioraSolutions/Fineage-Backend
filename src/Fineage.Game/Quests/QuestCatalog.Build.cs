using System.Text.Json;

namespace Fineage.Game.Quests;

public sealed partial class QuestCatalog
{
    static QuestDefinition BuildQuest(QuestDto row, ItemCatalog items, string where, string? htmlBaseDir, HashSet<int> seen)
    {
        if (row.Id <= 0)
            throw new InvalidDataException($"{where} has a quest id that is not positive.");
        if (!seen.Add(row.Id))
            throw new InvalidDataException($"{where} repeats quest id {row.Id}.");
        if (string.IsNullOrWhiteSpace(row.Name) || row.Name.Trim().Length > 40)
            throw new InvalidDataException($"{where} quest {row.Id} needs a name of 1..40 characters.");

        var questItems = new List<int>();
        foreach (int itemId in row.QuestItems ?? [])
        {
            if (!items.TryGet(itemId, out _))
                throw new InvalidDataException($"{where} quest {row.Id} quest item {itemId} is unknown.");
            if (itemId == AdenaItemId)
                throw new InvalidDataException($"{where} quest {row.Id} cannot register adena as a quest item.");
            if (questItems.Contains(itemId))
                throw new InvalidDataException($"{where} quest {row.Id} repeats quest item {itemId}.");
            questItems.Add(itemId);
        }

        var dialogs = new List<QuestDialog>();
        foreach (var dialog in row.Dialogs ?? [])
            dialogs.Add(ReadDialog(dialog, items, where, row.Id, htmlBaseDir));

        return new QuestDefinition(
            row.Id,
            row.Name.Trim(),
            row.Repeatable,
            row.Abortable ?? true,
            questItems,
            dialogs);
    }

    static QuestDialog ReadDialog(DialogDto dialog, ItemCatalog items, string where, int questId, string? htmlBaseDir = null)
    {
        var hook = ParseHook(dialog.Hook, where, questId);
        var when = ParseWhen(dialog.When, where, questId);
        string evt = (dialog.Event ?? "").Trim();
        if (hook == QuestHook.Event)
        {
            if (!IsEvent(evt))
                throw new InvalidDataException($"{where} quest {questId} has an event name that is not 1..64 letters, digits, '_' or '-'.");
        }
        else if (evt.Length > 0)
        {
            throw new InvalidDataException($"{where} quest {questId} sets event on a {dialog.Hook} step.");
        }

        if (dialog.NpcId <= 0)
            throw new InvalidDataException($"{where} quest {questId} has a step with no npcId.");
        if (dialog.Cond is < 0 or > 255)
            throw new InvalidDataException($"{where} quest {questId} cond must be 0..255.");

        var ops = new List<QuestOp>();
        foreach (var op in dialog.Ops ?? [])
            ops.Add(ReadOp(op, items, where, questId));

        string html = ResolveHtml(dialog, htmlBaseDir, where, questId);
        // Mobius addSpawn events often return null htmltext; allow empty HTML when spawn_npc is present.
        bool spawnOnly = ops.Exists(static o => o is SpawnNpcOp);
        if (hook != QuestHook.Kill && string.IsNullOrWhiteSpace(html) && !spawnOnly)
            throw new InvalidDataException($"{where} quest {questId} step needs html.");
        if (html.Length > 12_000)
            throw new InvalidDataException($"{where} quest {questId} html is longer than 12000 characters.");

        string? whenVar = string.IsNullOrWhiteSpace(dialog.WhenVar) ? null : dialog.WhenVar.Trim();
        string? whenValue = dialog.WhenValue;
        if (whenVar != null && !QuestJournal.IsStoredVar(whenVar, whenValue ?? ""))
            throw new InvalidDataException($"{where} quest {questId} whenVar is not a stored variable.");

        var races = new List<int>();
        foreach (int race in dialog.Races ?? [])
        {
            if (race is < 0 or > 4)
                throw new InvalidDataException($"{where} quest {questId} race must be 0..4 (Human, Elf, Dark Elf, Orc, Dwarf).");
            if (!races.Contains(race))
                races.Add(race);
        }

        if (dialog.MinLevel is < 0 or > 99 || dialog.MaxLevel is < 0 or > 99)
            throw new InvalidDataException($"{where} quest {questId} minLevel/maxLevel must be 0..99.");
        if (dialog.MaxLevel > 0 && dialog.MinLevel > dialog.MaxLevel)
            throw new InvalidDataException($"{where} quest {questId} minLevel is above maxLevel.");

        foreach (int classId in dialog.Classes ?? [])
        {
            if (classId is < 0 or > 136)
                throw new InvalidDataException($"{where} quest {questId} class id {classId} is out of range.");
        }

        return new QuestDialog(dialog.NpcId, hook, evt, when, dialog.Cond, whenVar, whenValue, html, ops,
            races.Count == 0 ? null : races, dialog.MinLevel, dialog.MaxLevel, ReadHasItem(dialog, items, where, questId), Math.Max(1, dialog.MinCount),
            dialog.Party && hook == QuestHook.Kill,
            dialog.Classes is { Count: > 0 } classes ? classes.Distinct().ToArray() : null,
            ReadItemList(dialog.HasItems, items, where, questId, "hasItems"),
            ReadItemList(dialog.LacksItems, items, where, questId, "lacksItems"),
            ReadAtLeast(dialog.AtLeast, items, where, questId),
            ReadItemList(dialog.HasAny, items, where, questId, "hasAny"),
            dialog.IsMage);
    }

    static IReadOnlyDictionary<int, int>? ReadAtLeast(Dictionary<string, int>? rows, ItemCatalog items, string where, int questId)
    {
        if (rows is not { Count: > 0 })
            return null;
        var map = new Dictionary<int, int>();
        foreach (var pair in rows)
        {
            if (!int.TryParse(pair.Key, out int id) || !items.TryGet(id, out _) || pair.Value < 1)
                throw new InvalidDataException($"{where} quest {questId} atLeast '{pair.Key}' needs a known item and count >= 1.");
            map[id] = pair.Value;
        }

        return map;
    }

    static IReadOnlyList<int>? ReadItemList(List<int>? ids, ItemCatalog items, string where, int questId, string field)
    {
        if (ids is not { Count: > 0 })
            return null;
        foreach (int id in ids)
        {
            if (!items.TryGet(id, out _))
                throw new InvalidDataException($"{where} quest {questId} {field} item {id} is unknown.");
        }

        return ids.Distinct().ToArray();
    }

    static int ReadHasItem(DialogDto dialog, ItemCatalog items, string where, int questId)
    {
        if (dialog.HasItem != 0 && !items.TryGet(dialog.HasItem, out _))
            throw new InvalidDataException($"{where} quest {questId} hasItem {dialog.HasItem} is unknown.");
        return dialog.HasItem;
    }
}
