using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>Who stands in front of the village master (Mobius <c>player.getRace / getClassId / getLevel</c>).</summary>
public readonly record struct ClassActor(int Race, int ClassId, int ClassLevel, bool Mage, int Level);

/// <summary>One first-transfer choice (Mobius <c>ClassChangeRequested</c> branch).</summary>
public sealed record ClassChangeOption(
    int ClassId,
    int FromClass,
    int ProofItemId,
    int MinLevel,
    string LowLevel,
    string LowLevelNoProof,
    string Success,
    string NoProof);

/// <summary>Mobius <c>ElfHumanFighterChange1</c> / <c>ElfHumanWizardChange1</c> as data.</summary>
public sealed record ClassChangeScript(
    string Script,
    IReadOnlyList<int> Npcs,
    bool MageGroup,
    IReadOnlyDictionary<int, string> ClassList,
    string Mismatch,
    string SecondClass,
    string ThirdClass,
    string FourthClassPage,
    int RewardItemId,
    int RewardCount,
    IReadOnlyList<ClassChangeOption> Options);

/// <summary>
/// What the world does after a village-master bypass: show <see cref="Page"/> (a file under
/// <c>html/village_master/&lt;script&gt;/</c>); when <see cref="Change"/> is set, take the proof, set the class, and give the coupon first.
/// </summary>
public sealed record VillageMasterResult(string Script, string Page, ClassChangeOption? Change = null, int RewardItemId = 0, int RewardCount = 0)
{
    public string HtmlPath => $"village_master/{Script}/{Page}";
}

/// <summary>
/// Mobius village_master first-transfer logic (FirstClassTransferTalk + Elf/Human Change1) driven by
/// <c>data/village-masters.json</c>. Pure decisions; the zone applies them.
/// </summary>
public sealed class VillageMasterCatalog
{
    public const string FileName = "village-masters.json";
    public const string TransferTalkScript = "FirstClassTransferTalk";

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

    VillageMasterCatalog(Dictionary<int, int> talk, HashSet<int> fighter, HashSet<int> mage, IReadOnlySet<int> mageRoots,
        Dictionary<string, ClassChangeScript> changes)
    {
        _talkMasters = talk;
        _humanFighterMasters = fighter;
        _humanMageMasters = mage;
        MageRoots = mageRoots;
        _changes = changes;
    }

    public static VillageMasterCatalog Empty { get; } = new([], [], [], new HashSet<int>(), new(StringComparer.OrdinalIgnoreCase));

    /// <summary>Base mystic classes (Mobius <c>isMageClass</c> roots). A class is in the mage group when its root is here.</summary>
    public IReadOnlySet<int> MageRoots { get; }

    public IEnumerable<ClassChangeScript> Changes => _changes.Values;

    public bool Knows(string script) =>
        string.Equals(script, TransferTalkScript, StringComparison.OrdinalIgnoreCase) || _changes.ContainsKey(script);

    /// <summary>Mobius <c>onTalk</c> (bypass <c>Quest &lt;script&gt;</c> with no event). Null = script does not serve this NPC.</summary>
    public VillageMasterResult? Talk(string script, int npcId, ClassActor who)
    {
        if (string.Equals(script, TransferTalkScript, StringComparison.OrdinalIgnoreCase))
            return TransferTalk(npcId, who);
        if (!_changes.TryGetValue(script, out var change) || !change.Npcs.Contains(npcId))
            return null;
        string suffix = who.Mage == change.MageGroup && change.ClassList.TryGetValue(who.Race, out var list) ? list : change.Mismatch;
        return new VillageMasterResult(change.Script, $"{npcId}-{suffix}.htm");
    }

    /// <summary>Mobius <c>onAdvEvent</c>: a page name of this NPC, or a class id to transfer to.</summary>
    public VillageMasterResult? Event(string script, int npcId, string evt, ClassActor who, Func<int, int> countOf)
    {
        ArgumentNullException.ThrowIfNull(countOf);
        evt = (evt ?? "").Trim();
        if (string.Equals(script, TransferTalkScript, StringComparison.OrdinalIgnoreCase))
        {
            // Mobius returns the event as the page: only that master's own "<npc>_*.html" pages.
            if (!_talkMasters.ContainsKey(npcId) || !evt.StartsWith($"{npcId}_", StringComparison.Ordinal) || !SafePage(evt))
                return null;
            return new VillageMasterResult(TransferTalkScript, evt);
        }

        if (!_changes.TryGetValue(script, out var change) || !change.Npcs.Contains(npcId))
            return null;
        if (int.TryParse(evt, out int classId))
            return Request(change, npcId, classId, who, countOf);

        // Static info pages: "<npc>-NN.htm" below the mismatch page (Mobius case list 01..17 / 01..14).
        if (evt.Length == npcId.ToString().Length + 7 && evt.StartsWith($"{npcId}-", StringComparison.Ordinal) && evt.EndsWith(".htm", StringComparison.Ordinal)
            && int.TryParse(evt.AsSpan(npcId.ToString().Length + 1, 2), out int number)
            && int.TryParse(change.Mismatch, out int mismatch) && number >= 1 && number < mismatch)
            return new VillageMasterResult(change.Script, evt);
        return null;
    }

    static VillageMasterResult? Request(ClassChangeScript change, int npcId, int classId, ClassActor who, Func<int, int> countOf)
    {
        string Page(string suffix) => $"{npcId}-{suffix}.htm";
        if (who.ClassLevel == 1)
            return new VillageMasterResult(change.Script, Page(change.SecondClass));
        if (who.ClassLevel == 2)
            return new VillageMasterResult(change.Script, Page(change.ThirdClass));
        if (who.ClassLevel >= 3)
            return new VillageMasterResult(change.Script, change.FourthClassPage);
        foreach (var option in change.Options)
        {
            if (option.ClassId != classId || option.FromClass != who.ClassId)
                continue;
            bool proof = countOf(option.ProofItemId) > 0;
            if (who.Level < option.MinLevel)
                return new VillageMasterResult(change.Script, Page(proof ? option.LowLevel : option.LowLevelNoProof));
            if (!proof)
                return new VillageMasterResult(change.Script, Page(option.NoProof));
            return new VillageMasterResult(change.Script, Page(option.Success), option, change.RewardItemId, change.RewardCount);
        }

        // Mobius returns null (no page) for a class the player cannot take.
        return null;
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

    static bool SafePage(string page)
    {
        if (page.Length is < 3 or > 64 || !page.EndsWith(".html", StringComparison.Ordinal))
            return false;
        foreach (char c in page)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' or '.'))
                return false;
        }

        return !page.Contains("..", StringComparison.Ordinal);
    }

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

        var changes = new Dictionary<string, ClassChangeScript>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in file.Changes ?? [])
        {
            if (string.IsNullOrWhiteSpace(c.Script) || c.Npcs is not { Count: > 0 } || c.Options is not { Count: > 0 })
                throw new InvalidDataException($"{where} change script needs script, npcs, options.");
            var list = new Dictionary<int, string>();
            foreach (var pair in c.ClassList ?? [])
                list[int.Parse(pair.Key)] = pair.Value;
            var options = c.Options.Select(o =>
            {
                if (o.Pages is not { Count: 4 } p || o.ProofItemId <= 0 || o.ClassId <= 0)
                    throw new InvalidDataException($"{where} {c.Script} option {o.ClassId} needs proofItemId and 4 pages.");
                return new ClassChangeOption(o.ClassId, o.FromClass, o.ProofItemId, o.MinLevel, p[0], p[1], p[2], p[3]);
            }).ToArray();
            changes[c.Script.Trim()] = new ClassChangeScript(c.Script.Trim(), c.Npcs.ToArray(),
                string.Equals(c.Group, "mage", StringComparison.OrdinalIgnoreCase), list,
                c.Mismatch ?? "", c.SecondClass ?? "", c.ThirdClass ?? "", c.FourthClassPage ?? "",
                c.RewardItemId, c.RewardCount, options);
        }

        return new VillageMasterCatalog(talk, (file.TransferTalk?.HumanFighterMasters ?? []).ToHashSet(),
            (file.TransferTalk?.HumanMageMasters ?? []).ToHashSet(), (file.MageRoots ?? []).ToHashSet(), changes);
    }

    sealed class FileDto
    {
        public TalkDto? TransferTalk { get; set; }
        public List<int>? MageRoots { get; set; }
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
        public string? Group { get; set; }
        public Dictionary<string, string>? ClassList { get; set; }
        public string? Mismatch { get; set; }
        public string? SecondClass { get; set; }
        public string? ThirdClass { get; set; }
        public string? FourthClassPage { get; set; }
        public int RewardItemId { get; set; }
        public int RewardCount { get; set; }
        public List<OptionDto>? Options { get; set; }
    }

    sealed class OptionDto
    {
        public int ClassId { get; set; }
        public int FromClass { get; set; }
        public int ProofItemId { get; set; }
        public int MinLevel { get; set; }
        public List<string>? Pages { get; set; }
    }
}
