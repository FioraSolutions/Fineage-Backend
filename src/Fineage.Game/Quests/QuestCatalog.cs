using System.Text.Json;

namespace Fineage.Game.Quests;

/// <summary>
/// Data-driven quests. Mobius Interlude quests are Java scripts plus HTML;
/// this catalog stores the same lifecycle (create, start, cond, complete, abort) as JSON.
/// </summary>
public sealed class QuestCatalog
{
    public const string FileName = "quests.json";
    public const int DefaultMaxActive = 25;
    public const int DefaultTalkRange = 150;
    public const int AdenaItemId = 57;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, QuestDefinition> _byId;

    QuestCatalog(int maxActive, int talkRange, IReadOnlyList<QuestDefinition> quests)
    {
        MaxActive = maxActive;
        TalkRange = talkRange;
        Quests = quests;
        _byId = new Dictionary<int, QuestDefinition>(quests.Count);
        foreach (var quest in quests)
            _byId.Add(quest.Id, quest);
    }

    public static QuestCatalog Empty { get; } = new(DefaultMaxActive, DefaultTalkRange, []);

    public int MaxActive { get; }

    public int TalkRange { get; }

    public IReadOnlyList<QuestDefinition> Quests { get; }

    public QuestDefinition? Find(int questId) => _byId.TryGetValue(questId, out var quest) ? quest : null;

    public static QuestCatalog Load(string path, ItemCatalog items)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Quest datapack not found: {path}", path);
        try
        {
            return Parse(File.ReadAllText(path), items, path);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }
    }

    public static QuestCatalog Parse(string json, ItemCatalog items, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(items);
        string where = source ?? "quests";
        var file = JsonSerializer.Deserialize<QuestFileDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"{where} was empty.");

        int maxActive = file.MaxActive == 0 ? DefaultMaxActive : file.MaxActive;
        if (maxActive < 1 || maxActive > 100)
            throw new InvalidDataException($"{where} maxActive must be 1..100.");
        int talkRange = file.TalkRange == 0 ? DefaultTalkRange : file.TalkRange;
        if (talkRange < 1 || talkRange > 5000)
            throw new InvalidDataException($"{where} talkRange must be 1..5000.");
        if (!items.TryGet(AdenaItemId, out _))
            throw new InvalidDataException($"{where} requires item {AdenaItemId} (adena) in the item catalog.");

        var quests = new List<QuestDefinition>();
        var seen = new HashSet<int>();
        foreach (var row in file.Quests ?? [])
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
                dialogs.Add(ReadDialog(dialog, items, where, row.Id));

            quests.Add(new QuestDefinition(
                row.Id,
                row.Name.Trim(),
                row.Repeatable,
                row.Abortable ?? true,
                questItems,
                dialogs));
        }

        quests.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return new QuestCatalog(maxActive, talkRange, quests);
    }

    static QuestDialog ReadDialog(DialogDto dialog, ItemCatalog items, string where, int questId)
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

        string html = dialog.Html ?? "";
        if (hook != QuestHook.Kill && string.IsNullOrWhiteSpace(html))
            throw new InvalidDataException($"{where} quest {questId} step needs html.");
        if (html.Length > 12_000)
            throw new InvalidDataException($"{where} quest {questId} html is longer than 12000 characters.");

        string? whenVar = string.IsNullOrWhiteSpace(dialog.WhenVar) ? null : dialog.WhenVar.Trim();
        string? whenValue = dialog.WhenValue;
        if (whenVar != null && !QuestJournal.IsStoredVar(whenVar, whenValue ?? ""))
            throw new InvalidDataException($"{where} quest {questId} whenVar is not a stored variable.");

        var ops = new List<QuestOp>();
        foreach (var op in dialog.Ops ?? [])
            ops.Add(ReadOp(op, items, where, questId));

        return new QuestDialog(dialog.NpcId, hook, evt, when, dialog.Cond, whenVar, whenValue, html, ops);
    }

    static QuestOp ReadOp(OpDto op, ItemCatalog items, string where, int questId)
    {
        string name = (op.Op ?? "").Trim().ToLowerInvariant();
        return name switch
        {
            "create" => new CreateOp(),
            "start" => new StartOp(),
            "set_cond" => ReadCond(op, where, questId),
            "set" => ReadSet(op, where, questId),
            "unset" => ReadUnset(op, where, questId),
            "give_item" => ReadGive(op, items, where, questId),
            "give_adena" => ReadAdena(op, where, questId),
            "give_exp_sp" => ReadExp(op, where, questId),
            "take_item" => ReadTake(op, items, where, questId),
            "complete" => new CompleteOp(),
            "sound" => ReadSound(op, where, questId),
            _ => throw new InvalidDataException($"{where} quest {questId} has unknown op '{op.Op}'.")
        };
    }

    static SetCondOp ReadCond(OpDto op, string where, int questId)
    {
        if (op.Cond is not int cond || cond is < 0 or > 255)
            throw new InvalidDataException($"{where} quest {questId} set_cond needs cond 0..255.");
        return new SetCondOp(cond);
    }

    static SetVarOp ReadSet(OpDto op, string where, int questId)
    {
        string key = (op.Key ?? "").Trim();
        string value = op.Value ?? "";
        if (!QuestJournal.IsStoredVar(key, value))
            throw new InvalidDataException($"{where} quest {questId} set key is not a stored variable.");
        if (key == QuestProgress.CondVar && (!int.TryParse(value, out int cond) || cond is < 0 or > 255))
            throw new InvalidDataException($"{where} quest {questId} cond value must be 0..255.");
        if (key == QuestProgress.FlagsVar && !int.TryParse(value, out _))
            throw new InvalidDataException($"{where} quest {questId} {QuestProgress.FlagsVar} must be an integer.");
        return new SetVarOp(key, value);
    }

    static UnsetVarOp ReadUnset(OpDto op, string where, int questId)
    {
        string key = (op.Key ?? "").Trim();
        if (!QuestJournal.IsStoredVar(key, ""))
            throw new InvalidDataException($"{where} quest {questId} unset key is not a stored variable.");
        return new UnsetVarOp(key);
    }

    static GiveItemOp ReadGive(OpDto op, ItemCatalog items, string where, int questId)
    {
        int count = op.Count ?? 1;
        if (op.ItemId == AdenaItemId)
            throw new InvalidDataException($"{where} quest {questId} give_item cannot grant adena. Use give_adena.");
        if (count < 1 || !items.TryGet(op.ItemId, out _))
            throw new InvalidDataException($"{where} quest {questId} give_item {op.ItemId} is unknown or has a bad count.");
        return new GiveItemOp(op.ItemId, count);
    }

    static GiveAdenaOp ReadAdena(OpDto op, string where, int questId)
    {
        int count = op.Count ?? 1;
        if (count < 1)
            throw new InvalidDataException($"{where} quest {questId} give_adena needs a positive count.");
        return new GiveAdenaOp(count);
    }

    static GiveExpSpOp ReadExp(OpDto op, string where, int questId)
    {
        long exp = op.Exp ?? 0;
        int sp = op.Sp ?? 0;
        if (exp < 0 || sp < 0 || (exp == 0 && sp == 0))
            throw new InvalidDataException($"{where} quest {questId} give_exp_sp needs a non-negative exp or sp.");
        return new GiveExpSpOp(exp, sp);
    }

    static TakeItemOp ReadTake(OpDto op, ItemCatalog items, string where, int questId)
    {
        int count = op.Count ?? 1;
        if (count < 1 || !items.TryGet(op.ItemId, out _))
            throw new InvalidDataException($"{where} quest {questId} take_item {op.ItemId} is unknown or has a bad count.");
        return new TakeItemOp(op.ItemId, count);
    }

    static SoundOp ReadSound(OpDto op, string where, int questId)
    {
        string name = (op.Name ?? "").Trim();
        if (!QuestSounds.Known(name))
            throw new InvalidDataException($"{where} quest {questId} sound '{name}' is unknown.");
        return new SoundOp(name);
    }

    static QuestHook ParseHook(string? text, string where, int questId) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "first_talk" => QuestHook.FirstTalk,
        "talk" => QuestHook.Talk,
        "event" => QuestHook.Event,
        "kill" => QuestHook.Kill,
        _ => throw new InvalidDataException($"{where} quest {questId} has unknown hook '{text}'.")
    };

    static QuestWhen ParseWhen(string? text, string where, int questId) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "absent" => QuestWhen.Absent,
        "created" => QuestWhen.Created,
        "started" => QuestWhen.Started,
        "completed" => QuestWhen.Completed,
        _ => throw new InvalidDataException($"{where} quest {questId} has unknown when '{text}'.")
    };

    static bool IsEvent(string text)
    {
        if (text.Length is < 1 or > 64)
            return false;
        foreach (char c in text)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-'))
                return false;
        }

        return true;
    }

    sealed class QuestFileDto
    {
        public int MaxActive { get; set; }
        public int TalkRange { get; set; }
        public List<QuestDto>? Quests { get; set; }
    }

    sealed class QuestDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool Repeatable { get; set; }
        public bool? Abortable { get; set; }
        public List<int>? QuestItems { get; set; }
        public List<DialogDto>? Dialogs { get; set; }
    }

    sealed class DialogDto
    {
        public int NpcId { get; set; }
        public string? Hook { get; set; }
        public string? Event { get; set; }
        public string? When { get; set; }
        public int? Cond { get; set; }
        public string? WhenVar { get; set; }
        public string? WhenValue { get; set; }
        public string? Html { get; set; }
        public List<OpDto>? Ops { get; set; }
    }

    sealed class OpDto
    {
        public string? Op { get; set; }
        public int? Cond { get; set; }
        public string? Key { get; set; }
        public string? Value { get; set; }
        public int ItemId { get; set; }
        public int? Count { get; set; }
        public long? Exp { get; set; }
        public int? Sp { get; set; }
        public string? Name { get; set; }
    }
}
