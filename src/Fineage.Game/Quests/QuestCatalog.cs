using System.Text.Json;

namespace Fineage.Game.Quests;

/// <summary>
/// Data-driven quests. Mobius Interlude quests are Java scripts plus HTML;
/// Fineage stores one folder per quest under <c>data/quests/</c> (see <see cref="LoadPack"/>).
/// </summary>
public sealed class QuestCatalog
{
    /// <summary>Directory under <c>data/</c> that holds <c>_meta.json</c> and per-quest folders.</summary>
    public const string DirectoryName = "quests";
    public const string MetaFileName = "_meta.json";
    public const string QuestFileName = "quest.json";
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

    /// <summary>
    /// Loads <c>data/quests/_meta.json</c> plus every <c>data/quests/*/quest.json</c>.
    /// Dialog HTML may be inline or <c>htmlFile</c> relative to that quest folder (Mobius-style).
    /// </summary>
    public static QuestCatalog LoadPack(string questsDirectory, ItemCatalog items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(questsDirectory);
        ArgumentNullException.ThrowIfNull(items);
        if (!Directory.Exists(questsDirectory))
            throw new DirectoryNotFoundException($"Quest pack not found: {questsDirectory}");

        string metaPath = Path.Combine(questsDirectory, MetaFileName);
        if (!File.Exists(metaPath))
            throw new FileNotFoundException($"Quest pack meta not found: {metaPath}", metaPath);

        MetaDto meta;
        try
        {
            meta = JsonSerializer.Deserialize<MetaDto>(File.ReadAllText(metaPath), JsonOptions)
                ?? throw new InvalidDataException($"{metaPath} was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {metaPath}.", ex);
        }

        int maxActive = meta.MaxActive == 0 ? DefaultMaxActive : meta.MaxActive;
        int talkRange = meta.TalkRange == 0 ? DefaultTalkRange : meta.TalkRange;
        ValidateLimits(maxActive, talkRange, metaPath, items);

        var quests = new List<QuestDefinition>();
        var seen = new HashSet<int>();
        foreach (string dir in Directory.EnumerateDirectories(questsDirectory).OrderBy(static d => d, StringComparer.Ordinal))
        {
            string questPath = Path.Combine(dir, QuestFileName);
            if (!File.Exists(questPath))
                continue;
            QuestDto row;
            try
            {
                row = JsonSerializer.Deserialize<QuestDto>(File.ReadAllText(questPath), JsonOptions)
                    ?? throw new InvalidDataException($"{questPath} was empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Could not read {questPath}.", ex);
            }

            quests.Add(BuildQuest(row, items, questPath, dir, seen));
        }

        if (quests.Count == 0)
            throw new InvalidDataException($"{questsDirectory} has no {QuestFileName} folders.");

        quests.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return new QuestCatalog(maxActive, talkRange, quests);
    }

    /// <summary>In-memory / test helper: monolith <c>{ maxActive, talkRange, quests: [...] }</c> JSON.</summary>
    public static QuestCatalog Parse(string json, ItemCatalog items, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(items);
        string where = source ?? "quests";
        var file = JsonSerializer.Deserialize<QuestFileDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"{where} was empty.");

        int maxActive = file.MaxActive == 0 ? DefaultMaxActive : file.MaxActive;
        int talkRange = file.TalkRange == 0 ? DefaultTalkRange : file.TalkRange;
        ValidateLimits(maxActive, talkRange, where, items);

        var quests = new List<QuestDefinition>();
        var seen = new HashSet<int>();
        foreach (var row in file.Quests ?? [])
            quests.Add(BuildQuest(row, items, where, htmlBaseDir: null, seen));

        quests.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return new QuestCatalog(maxActive, talkRange, quests);
    }

    static void ValidateLimits(int maxActive, int talkRange, string where, ItemCatalog items)
    {
        if (maxActive < 1 || maxActive > 100)
            throw new InvalidDataException($"{where} maxActive must be 1..100.");
        if (talkRange < 1 || talkRange > 5000)
            throw new InvalidDataException($"{where} talkRange must be 1..5000.");
        if (!items.TryGet(AdenaItemId, out _))
            throw new InvalidDataException($"{where} requires item {AdenaItemId} (adena) in the item catalog.");
    }

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
            "drop_item" => ReadDrop(op, items, where, questId),
            "exchange_all" => ReadExchange(op, items, where, questId),
            "sell_items" => ReadSell(op, items, where, questId),
            "sound" => ReadSound(op, where, questId),
            "spawn_npc" => ReadSpawn(op, where, questId),
            _ => throw new InvalidDataException($"{where} quest {questId} has unknown op '{op.Op}'.")
        };
    }

    static SpawnNpcOp ReadSpawn(OpDto op, string where, int questId)
    {
        if (op.NpcId <= 0)
            throw new InvalidDataException($"{where} quest {questId} spawn_npc needs a positive npcId.");
        return new SpawnNpcOp(op.NpcId);
    }

    static DropItemOp ReadDrop(OpDto op, ItemCatalog items, string where, int questId)
    {
        int count = op.Count ?? 1;
        double chance = op.Chance ?? 1.0;
        int limit = op.Limit ?? 0;
        if (count < 1 || !items.TryGet(op.ItemId, out _) || op.ItemId == AdenaItemId)
            throw new InvalidDataException($"{where} quest {questId} drop_item {op.ItemId} is unknown or has a bad count.");
        if (chance is <= 0 or > 1 || limit < 0 || op.CondAtLimit is < 0 or > 255)
            throw new InvalidDataException($"{where} quest {questId} drop_item needs chance 0..1, limit >= 0, condAtLimit 0..255.");
        return new DropItemOp(op.ItemId, count, chance, limit, op.CondAtLimit);
    }

    static SellItemsOp ReadSell(OpDto op, ItemCatalog items, string where, int questId)
    {
        var rows = new List<(int, int)>();
        foreach (var row in op.Prices ?? [])
        {
            if (!items.TryGet(row.ItemId, out _) || row.Adena < 0)
                throw new InvalidDataException($"{where} quest {questId} sell_items has a bad price row.");
            rows.Add((row.ItemId, row.Adena));
        }

        if (rows.Count == 0)
            throw new InvalidDataException($"{where} quest {questId} sell_items needs prices.");
        return new SellItemsOp(rows, op.BonusMin ?? 0, op.BonusAdena ?? 0);
    }

    static ExchangeAllOp ReadExchange(OpDto op, ItemCatalog items, string where, int questId)
    {
        int reward = op.RewardItemId ?? 0;
        if (!items.TryGet(op.ItemId, out _) || !items.TryGet(reward, out _) || (op.Per ?? 1) < 1 || (op.BonusAdena ?? 0) < 0)
            throw new InvalidDataException($"{where} quest {questId} exchange_all needs known itemId/rewardItemId and per >= 1.");
        return new ExchangeAllOp(op.ItemId, reward, op.Per ?? 1, op.BonusMin ?? 0, op.BonusAdena ?? 0);
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
        "any" => QuestWhen.Any,
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

    static string ResolveHtml(DialogDto dialog, string? htmlBaseDir, string where, int questId)
    {
        string file = (dialog.HtmlFile ?? "").Trim();
        if (file.Length == 0)
            return dialog.Html ?? "";

        if (htmlBaseDir == null)
            throw new InvalidDataException($"{where} quest {questId} uses htmlFile but has no quest folder.");
        if (file.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(file) || file.Contains('/') || file.Contains('\\'))
            throw new InvalidDataException($"{where} quest {questId} htmlFile must be a plain file name in the quest folder.");

        string path = Path.Combine(htmlBaseDir, file);
        if (!File.Exists(path))
            throw new FileNotFoundException($"{where} quest {questId} htmlFile '{file}' not found.", path);
        return File.ReadAllText(path);
    }

    sealed class MetaDto
    {
        public int MaxActive { get; set; }
        public int TalkRange { get; set; }
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
        /// <summary>Plain file name under the quest folder (Mobius HTML sibling).</summary>
        public string? HtmlFile { get; set; }
        public List<OpDto>? Ops { get; set; }
        public List<int>? Races { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int HasItem { get; set; }
        public int MinCount { get; set; } = 1;
        public bool Party { get; set; }
        public List<int>? Classes { get; set; }
        public List<int>? HasItems { get; set; }
        public List<int>? LacksItems { get; set; }
        public Dictionary<string, int>? AtLeast { get; set; }
        public List<int>? HasAny { get; set; }
        public bool? IsMage { get; set; }
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
        public double? Chance { get; set; }
        public int? Limit { get; set; }
        public int? CondAtLimit { get; set; }
        public int? RewardItemId { get; set; }
        public int? Per { get; set; }
        public int? BonusMin { get; set; }
        public int? BonusAdena { get; set; }
        public int NpcId { get; set; }
        public List<PriceDto>? Prices { get; set; }
    }

    sealed class PriceDto
    {
        public int ItemId { get; set; }
        public int Adena { get; set; }
    }
}
