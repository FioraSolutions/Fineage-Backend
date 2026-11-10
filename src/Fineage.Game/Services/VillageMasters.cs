using System.Text.Json;

namespace Fineage.Game.Services;

public sealed class VillageMasterCatalog
{
    public const string FileName = "village-masters.json";
    public const string TransferTalkScript = "FirstClassTransferTalk";

    /// <summary>Placeholder the generator leaves where a script names the page after the master NPC.</summary>
    public const string NpcPlaceholder = "{npc}";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, int> _talkMasters;
    readonly HashSet<int> _humanFighterMasters;
    readonly HashSet<int> _humanMageMasters;
    readonly Dictionary<string, ClassChangeScript> _changes;
    readonly Dictionary<string, HashSet<int>> _categories;

    VillageMasterCatalog(Dictionary<int, int> talk, HashSet<int> fighter, HashSet<int> mage, IReadOnlySet<int> mageRoots,
        Dictionary<string, ClassChangeScript> changes, Dictionary<string, HashSet<int>> categories)
    {
        _talkMasters = talk;
        _humanFighterMasters = fighter;
        _humanMageMasters = mage;
        MageRoots = mageRoots;
        _changes = changes;
        _categories = categories;
    }

    public static VillageMasterCatalog Empty { get; } =
        new([], [], [], new HashSet<int>(), new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));

    /// <summary>Base mystic classes (Mobius <c>isMageClass</c> roots). A class is in the mage group when its root is here.</summary>
    public IReadOnlySet<int> MageRoots { get; }

    public IEnumerable<ClassChangeScript> Changes => _changes.Values;

    public bool Knows(string script) =>
        string.Equals(script, TransferTalkScript, StringComparison.OrdinalIgnoreCase) || _changes.ContainsKey(script);

    /// <summary>Mobius <c>isInCategory(CategoryType.X, classId)</c> from CategoryData.xml.</summary>
    public bool InCategory(string category, int classId) =>
        _categories.TryGetValue(category, out var members) && members.Contains(classId);

    /// <summary>Mobius <c>onTalk</c> (bypass <c>Quest &lt;script&gt;</c> with no event). Null = script does not serve this NPC.</summary>
    public VillageMasterResult? Talk(string script, int npcId, ClassActor who)
    {
        if (string.Equals(script, TransferTalkScript, StringComparison.OrdinalIgnoreCase))
            return TransferTalk(npcId, who);
        if (!_changes.TryGetValue(script, out var change) || !change.Npcs.Contains(npcId))
            return null;
        foreach (var rule in change.Talk)
        {
            if (Matches(rule, who, this))
                return new VillageMasterResult(change.Script, Page(change, npcId, rule.Page));
        }

        return null; // Mobius: the if/else tree always ends in an else, so this cannot happen with real data.
    }

    /// <summary>Mobius <c>onAdvEvent</c>: a page name of this NPC, or a class id to transfer to.</summary>
    public VillageMasterResult? Event(string script, int npcId, string evt, ClassActor who, Func<int, int> countOf)
    {
        ArgumentNullException.ThrowIfNull(countOf);
        evt = (evt ?? "").Trim();
        if (string.Equals(script, TransferTalkScript, StringComparison.OrdinalIgnoreCase))
        {
            // Mobius returns the event as the page: only that master's own "<npc>_*.html" pages.
            if (!_talkMasters.ContainsKey(npcId) || !evt.StartsWith($"{npcId}_", StringComparison.Ordinal) || !SafePageName(evt))
                return null;
            return new VillageMasterResult(TransferTalkScript, evt);
        }

        if (!_changes.TryGetValue(script, out var change) || !change.Npcs.Contains(npcId))
            return null;

        // Mobius keeps two shapes: DarkElf*/DwarfChange1 send a table index, the rest send the class id.
        bool byIndex = change.Options.Any(o => !string.Equals(o.Event, o.ClassId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var option = byIndex
            ? change.Options.FirstOrDefault(o => string.Equals(o.Event, evt, StringComparison.Ordinal))
            : (int.TryParse(evt, out int classId) ? change.Options.FirstOrDefault(o => o.ClassId == classId) : null);
        if (option != null)
            return Request(change, npcId, option, who, countOf);

        // Static info pages: Mobius only echoes the page names its own switch lists.
        if (change.EventPages.Contains(evt, StringComparer.Ordinal))
            return new VillageMasterResult(change.Script, evt);
        return null;
    }
    static bool SafePageName(string page)
    {
        if (page.Length is < 3 or > 64 || page.Contains("..", StringComparison.Ordinal))
            return false;
        foreach (char c in page)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' or '.'))
                return false;
        }

        return true;
    }

    VillageMasterResult? Request(ClassChangeScript change, int npcId, ClassChangeOption option, ClassActor who, Func<int, int> countOf)
    {
        // Mobius runs the category guards before the per-class branch: a character that already
        // made the next change is told so instead of transferring again.
        foreach (var guard in change.Guards)
        {
            if (guard.Categories.Count > 0 && guard.Categories.All(category => InCategory(category, who.ClassId)))
                return new VillageMasterResult(change.Script, Page(change, npcId, guard.Page));
        }

        if (option.FromClass != who.ClassId)
            return null; // Mobius: no page for a class the player cannot take.

        int proofs = option.ProofItemIds.Count;
        int have = option.ProofItemIds.Count(item => countOf(item) > 0);
        bool allProofs = have == proofs;
        if (who.Level < option.MinLevel)
            return new VillageMasterResult(change.Script, Page(change, npcId, allProofs ? option.LowLevel : option.LowLevelNoProof));
        if (!allProofs)
            return new VillageMasterResult(change.Script, Page(change, npcId, option.NoProof));
        return new VillageMasterResult(change.Script, Page(change, npcId, option.Success), option, change.RewardItemId, change.RewardCount);
    }

    static bool Matches(TalkRule rule, ClassActor who, VillageMasterCatalog catalog)
    {
        if (rule.IsElse)
            return true; // the trailing `else`
        if (rule.Races.Count > 0 && !rule.Races.Contains(who.Race))
            return false;
        if (rule.Classes.Count > 0 && !rule.Classes.Contains(who.ClassId))
            return false;
        if (rule.ClassLevels.Count > 0 && !rule.ClassLevels.Contains(who.ClassLevel))
            return false;
        // `categories` must all hold (Mobius `a && b`); `anyCategory` is the `a || b` branch.
        if (rule.Categories.Count > 0 && !rule.Categories.All(category => catalog.InCategory(category, who.ClassId)))
            return false;
        if (rule.AnyCategory.Count > 0 && !rule.AnyCategory.Any(category => catalog.InCategory(category, who.ClassId)))
            return false;
        return true;
    }

    VillageMasterResult? TransferTalk(int npcId, ClassActor who)
    {
        if (!_talkMasters.TryGetValue(npcId, out int race))
            return null;
        string Page(string suffix) => $"{npcId}_{suffix}.html";
        if (race != who.Race)
            return new VillageMasterResult(TransferTalkScript, Page("no"));
        if (who.ClassLevel >= 2)
            return new VillageMasterResult(TransferTalkScript, Page("transfer_2"));
        if (who.ClassLevel == 1)
            return new VillageMasterResult(TransferTalkScript, Page("transfer_1"));
        if (race == 0)
        {
            // Human: Blitz (fighter guild) only talks to fighters, Biotin (temple) only to mystics.
            if (who.Mage)
                return new VillageMasterResult(TransferTalkScript, Page(_humanMageMasters.Contains(npcId) ? "mystic" : "no"));
            return new VillageMasterResult(TransferTalkScript, Page(_humanFighterMasters.Contains(npcId) ? "fighter" : "no"));
        }

        if (race == 4)
            return new VillageMasterResult(TransferTalkScript, Page("fighter"));
        return new VillageMasterResult(TransferTalkScript, Page(who.Mage ? "mystic" : "fighter"));
    }

    static string Page(ClassChangeScript change, int npcId, string page) =>
        page.Replace("{npc}", npcId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>Class level and mage group from the class tree (Mobius CategoryType FIRST..FOURTH_CLASS_GROUP, MAGE_GROUP).</summary>
    public ClassActor Actor(ClassCatalog? classes, int race, int classId, int level)
    {
        int classLevel = 0;
        int root = classId;
        if (classes != null && classes.TryGet(classId, out _))
        {
            var chain = classes.ParentChain(classId);
            classLevel = chain.Count - 1;
            root = chain[0];
        }

        return new ClassActor(race, classId, classLevel, MageRoots.Contains(root), level);
    }

    public static VillageMasterCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path), path);
    }

    public static VillageMasterCatalog Parse(string json, string? source = null)
    {
        string where = source ?? FileName;
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions) ?? throw new InvalidDataException($"{where} was empty.");
        var talk = new Dictionary<int, int>();
        foreach (var pair in file.TransferTalk?.Masters ?? [])
        {
            if (!int.TryParse(pair.Key, out int npc) || pair.Value is < 0 or > 4)
                throw new InvalidDataException($"{where} transferTalk master '{pair.Key}' is bad.");
            talk[npc] = pair.Value;
        }

        var categories = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, ids) in file.Categories ?? [])
            categories[name] = [.. ids];

        var changes = new Dictionary<string, ClassChangeScript>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in file.Changes ?? [])
        {
            if (string.IsNullOrWhiteSpace(c.Script) || c.Npcs is not { Count: > 0 } || c.Options is not { Count: > 0 })
                throw new InvalidDataException($"{where} change script needs script, npcs, options.");
            var options = c.Options.Select(o =>
            {
                if (o.ClassId <= 0 || o.ProofItemIds is not { Count: > 0 } || string.IsNullOrWhiteSpace(o.Event))
                    throw new InvalidDataException($"{where} {c.Script} option {o.ClassId} needs event, classId and proofItemIds.");
                if (string.IsNullOrWhiteSpace(o.Low) || string.IsNullOrWhiteSpace(o.LowNoProof) ||
                    string.IsNullOrWhiteSpace(o.Success) || string.IsNullOrWhiteSpace(o.NoProof))
                    throw new InvalidDataException($"{where} {c.Script} option {o.ClassId} needs four pages.");

                return new ClassChangeOption(o.Event.Trim(), o.ClassId, o.FromClass, o.ProofItemIds.ToArray(), o.MinLevel,
                    o.Low, o.LowNoProof, o.Success, o.NoProof);
            }).ToArray();

            var guards = (c.Guards ?? []).Select(g => new CategoryGuard(g.Categories ?? [], g.Page ?? "")).ToArray();
            var rules = (c.Talk ?? []).Select(t => new TalkRule(t.Page ?? "", t.Races ?? [], t.Classes ?? [], t.ClassLevels ?? [],
                t.Categories ?? [], t.AnyCategory ?? [])).ToArray();
            if (rules.Length == 0)
                throw new InvalidDataException($"{where} {c.Script} needs at least one talk rule.");
            changes[c.Script.Trim()] = new ClassChangeScript(c.Script.Trim(), c.Npcs.ToArray(), c.PagePrefix ?? "", guards,
                options, rules, (c.EventPages ?? []).ToArray(), c.RewardItemId, c.RewardCount);
        }

        return new VillageMasterCatalog(talk, (file.TransferTalk?.HumanFighterMasters ?? []).ToHashSet(),
            (file.TransferTalk?.HumanMageMasters ?? []).ToHashSet(), (file.MageRoots ?? []).ToHashSet(), changes, categories);
    }

    sealed class FileDto
    {
        public TalkDto? TransferTalk { get; set; }
        public List<int>? MageRoots { get; set; }
        public Dictionary<string, List<int>>? Categories { get; set; }
        public List<ChangeDto>? Changes { get; set; }
    }

    sealed class TalkDto
    {
        public Dictionary<string, int>? Masters { get; set; }
        public List<int>? HumanFighterMasters { get; set; }
        public List<int>? HumanMageMasters { get; set; }
    }

    sealed class ChangeDto
    {
        public string? Script { get; set; }
        public List<int>? Npcs { get; set; }
        public string? PagePrefix { get; set; }
        public int RewardItemId { get; set; }
        public int RewardCount { get; set; }
        public List<GuardDto>? Guards { get; set; }
        public List<OptionDto>? Options { get; set; }
        public List<string>? EventPages { get; set; }
        public List<TalkDto2>? Talk { get; set; }
    }

    sealed class GuardDto
    {
        public List<string>? Categories { get; set; }
        public string? Page { get; set; }
    }

    sealed class OptionDto
    {
        public string? Event { get; set; }
        public int ClassId { get; set; }
        public int FromClass { get; set; }
        public List<int>? ProofItemIds { get; set; }
        public int MinLevel { get; set; }
        public string? Low { get; set; }
        public string? LowNoProof { get; set; }
        public string? Success { get; set; }
        public string? NoProof { get; set; }
    }

    sealed class TalkDto2
    {
        public string? Page { get; set; }
        public List<int>? Races { get; set; }
        public List<int>? Classes { get; set; }
        public List<int>? ClassLevels { get; set; }
        public List<string>? Categories { get; set; }
        public List<string>? AnyCategory { get; set; }
    }
}
