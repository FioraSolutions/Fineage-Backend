using System.Text.Json;

namespace Fineage.Game.Quests;

/// <summary>
/// Data-driven quests. Mobius Interlude quests are Java scripts plus HTML;
/// Fineage stores one folder per quest under <c>data/quests/</c> (see <see cref="LoadPack"/>).
/// </summary>
public sealed partial class QuestCatalog
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
}
