using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fineage.Game.RequiredData;

namespace Fineage.Zone.RequiredData;

/// <summary>One dangling reference: <paramref name="Source"/> points at <paramref name="Kind"/> <paramref name="Key"/> that no catalog defines.</summary>
public sealed record RequiredDataViolation(RequiredDataKind Kind, string Key, string Source, string Detail)
{
    public override string ToString() => $"{Kind.ToId()} {Key} <- {Source}: {Detail}";
}

/// <summary>
/// #985 boot validation. Every catalog that points at another catalog by id (spawns → npcs, drops → items,
/// buylists → items, skill trees → skills, NPC html bypasses → buylists / multisells / teleport lists / npc scripts, …)
/// is resolved here once, before the world starts. A missing target is a <see cref="RequiredDataViolation"/>
/// naming the id and the file that referenced it, so a holey import fails boot and CI instead of becoming a
/// silent default at the first player action. Original-data holes are listed per kind in
/// <c>data/required-data-registry.json</c> <c>knownAbsent</c> with a reason; a listed key that is no longer
/// missing is itself a violation so the list cannot rot.
/// </summary>
public static partial class RequiredDataCrossCheck
{
    static readonly JsonDocumentOptions DocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Throws <see cref="RequiredDataException"/> listing every violation when any reference dangles.</summary>
    public static void AssertClean(string dataDirectory, RequiredDataRegistry registry)
    {
        var violations = Run(dataDirectory, registry);
        if (violations.Count == 0)
            return;
        var first = violations[0];
        throw new RequiredDataException(
            first.Kind,
            first.Key,
            registry.Get(first.Kind).File,
            $"{violations.Count} dangling data reference(s) at boot:\n  " + string.Join("\n  ", violations));
    }

    public static IReadOnlyList<RequiredDataViolation> Run(string dataDirectory, RequiredDataRegistry registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(registry);
        var ctx = new Context(dataDirectory, registry);
        ctx.CheckSpawns();
        ctx.CheckDrops();
        ctx.CheckBossesAndMinions();
        ctx.CheckSummons();
        ctx.CheckSkillTrees();
        ctx.CheckItemSkills();
        ctx.CheckShops();
        ctx.CheckRecipes();
        ctx.CheckNpcAiSupport();
        ctx.CheckHtmlBypasses();
        ctx.CheckStaleKnownAbsent();
        return ctx.Violations;
    }

    [GeneratedRegex(@"bypass\s+-h\s+npc_%objectId%_(\w+)[ \t]*([^""]*)", RegexOptions.IgnoreCase)]
    private static partial Regex NpcBypass();

    [GeneratedRegex(@"bypass\s+-h\s+Script\s+([^\s""]+)")]
    private static partial Regex ScriptBypass();

    [GeneratedRegex(@"^(\d+)")]
    private static partial Regex LeadingNpcId();

    sealed class Context
    {
        readonly string _dir;
        readonly RequiredDataRegistry _registry;
        readonly HashSet<(RequiredDataKind, string)> _seen = [];
        readonly HashSet<(RequiredDataKind, string)> _knownHit = [];
        HashSet<int>? _npcs, _items, _skills;
        Dictionary<int, HashSet<int>>? _buyLists;
        HashSet<int>? _multiSells;
        Dictionary<int, HashSet<string>>? _teleports;
        HashSet<string>? _scripts;
        HashSet<int>? _quests;

        public Context(string dir, RequiredDataRegistry registry)
        {
            _dir = dir;
            _registry = registry;
        }

        public List<RequiredDataViolation> Violations { get; } = [];

        HashSet<int> Npcs => _npcs ??= Ids(Root("npcs.json", "npcs"), "npcId");
        HashSet<int> Items => _items ??= Ids(Root("items.json", "items"), "id");
        HashSet<int> Skills => _skills ??= Ids(Root("skills.json", "skills"), "skillId");

        Dictionary<int, HashSet<int>> BuyLists => _buyLists ??= Root("buylists.json", "lists")
            .ToDictionary(l => Int(l, "id", "buylists.json"), l => Arr(l, "npcs").Select(n => n!.GetValue<int>()).ToHashSet());

        HashSet<int> MultiSells => _multiSells ??= Ids(Root("multisell.json", "lists"), "id");

        Dictionary<int, HashSet<string>> Teleports => _teleports ??= Root("teleports.json", "gatekeepers")
            .ToDictionary(
                g => Int(g, "npcId", "teleports.json"),
                g => Arr(g, "lists").Select(l => (string)l!["name"]!).ToHashSet(StringComparer.OrdinalIgnoreCase));

        HashSet<string> Scripts => _scripts ??= Root("npc-scripts.json", "scripts")
            .Select(s => (string)s!["id"]!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<int> Quests => _quests ??= Directory
            .EnumerateDirectories(Path.Combine(_dir, "quests"))
            .Select(d => LeadingNpcId().Match(Path.GetFileName(d)))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToHashSet();

        public void CheckSpawns()
        {
            foreach (var (name, doc) in Pack("spawns"))
            {
                foreach (var row in Arr(doc, "npcs"))
                    Need(RequiredDataKind.Npc, Int(row, "npcId", name), Npcs, name, "spawn row npcId");
            }
        }

        public void CheckDrops()
        {
            foreach (var (name, doc) in Pack("drops", "npc-*.json"))
            {
                Need(RequiredDataKind.Npc, Int(doc, "npcId", name), Npcs, name, "droplist owner npcId");
                foreach (int item in Deep(doc, "itemId"))
                    Need(RequiredDataKind.Item, item, Items, name, "drop itemId");
            }
        }

        public void CheckBossesAndMinions()
        {
            foreach (var boss in Root("raidbosses.json", "bosses"))
                Need(RequiredDataKind.Npc, Int(boss, "npcId", "raidbosses.json"), Npcs, "raidbosses.json", "boss npcId");

            var masters = Doc("npc-minions.json")["masters"]?.AsObject()
                ?? throw Corrupt("npc-minions.json", "masters");
            foreach (var (master, minions) in masters)
            {
                Need(RequiredDataKind.Npc, int.Parse(master, CultureInfo.InvariantCulture), Npcs, "npc-minions.json", "minion master");
                foreach (var minion in minions!.AsArray())
                    Need(RequiredDataKind.Npc, Int(minion, "id", "npc-minions.json"), Npcs, "npc-minions.json", $"minion of {master}");
            }
        }

        public void CheckSummons()
        {
            var doc = Doc("summons.json");
            foreach (var summon in Arr(doc, "summons"))
            {
                foreach (var level in Arr(summon, "levels"))
                {
                    Need(RequiredDataKind.Npc, Int(level, "npcId", "summons.json"), Npcs, "summons.json", $"summon skill {summon!["skillId"]}");
                    foreach (string key in new[] { "itemId", "upkeepItemId" })
                    {
                        if (level![key]?.GetValue<int>() is int item and > 0)
                            Need(RequiredDataKind.Item, item, Items, "summons.json", $"summon skill {summon!["skillId"]} {key}");
                    }
                }
            }
        }

        public void CheckSkillTrees()
        {
            foreach (var tree in Root("skill-trees.json", "trees"))
            {
                foreach (var entry in Arr(tree, "skills"))
                {
                    Need(RequiredDataKind.Skill, Int(entry, "skillId", "skill-trees.json"), Skills, "skill-trees.json", $"class {tree!["classId"]} tree");
                    foreach (int item in Deep(entry, "itemId"))
                        Need(RequiredDataKind.Item, item, Items, "skill-trees.json", $"class {tree!["classId"]} learn item");
                }
            }

            foreach (var entry in Root("fishing.json", "skillTree"))
            {
                Need(RequiredDataKind.Skill, Int(entry, "skillId", "fishing.json"), Skills, "fishing.json", "fishing skill tree");
                foreach (int item in Deep(entry, "itemId"))
                    Need(RequiredDataKind.Item, item, Items, "fishing.json", "fishing skill learn item");
            }

            foreach (var row in Root("pledge-skills.json", "skills"))
            {
                Need(RequiredDataKind.Skill, Int(row, "skillId", "pledge-skills.json"), Skills, "pledge-skills.json", "clan skill");
                foreach (var item in Arr(row, "items"))
                    Need(RequiredDataKind.Item, Int(item, "id", "pledge-skills.json"), Items, "pledge-skills.json", "clan skill learn item");
            }
        }

        public void CheckItemSkills()
        {
            foreach (var row in Root("item-equip-skills.json", "items"))
            {
                int item = Int(row, "itemId", "item-equip-skills.json");
                Need(RequiredDataKind.Item, item, Items, "item-equip-skills.json", "equip-skill owner");
                foreach (var skill in Arr(row, "skills"))
                    Need(RequiredDataKind.Skill, Int(skill, "id", "item-equip-skills.json"), Skills, "item-equip-skills.json", $"equip skill of item {item}");
            }

            foreach (var row in Root("herbs.json", "items"))
            {
                int item = Int(row, "itemId", "herbs.json");
                Need(RequiredDataKind.Item, item, Items, "herbs.json", "herb");
                foreach (var skill in Arr(row, "skills"))
                    Need(RequiredDataKind.Skill, Int(skill, "skillId", "herbs.json"), Skills, "herbs.json", $"herb {item} skill");
            }

            foreach (var blade in Root("cursed-weapons.json", "blades"))
            {
                Need(RequiredDataKind.Item, Int(blade, "itemId", "cursed-weapons.json"), Items, "cursed-weapons.json", "cursed weapon");
                Need(RequiredDataKind.Skill, Int(blade, "skillId", "cursed-weapons.json"), Skills, "cursed-weapons.json", "cursed weapon skill");
            }

            foreach (var row in Root("item-skills.json", "items"))
                Need(RequiredDataKind.Item, Int(row, "itemId", "item-skills.json"), Items, "item-skills.json", "usable item");
        }

        public void CheckShops()
        {
            foreach (var list in Root("buylists.json", "lists"))
            {
                int id = Int(list, "id", "buylists.json");
                foreach (int npc in Arr(list, "npcs").Select(n => n!.GetValue<int>()))
                    Need(RequiredDataKind.Npc, npc, Npcs, "buylists.json", $"buylist {id} npc");
                foreach (var item in Arr(list, "items"))
                    Need(RequiredDataKind.Item, Int(item, "id", "buylists.json"), Items, "buylists.json", $"buylist {id} product");
            }

            foreach (var list in Root("multisell.json", "lists"))
            {
                int id = Int(list, "id", "multisell.json");
                foreach (var entry in Arr(list, "entries"))
                {
                    foreach (var row in Arr(entry, "ingredients").Concat(Arr(entry, "products")))
                    {
                        int item = Int(row, "id", "multisell.json");
                        // Mobius multisell pseudo-ids: -200 clan reputation, -300 PC bang / fame points (not items).
                        if (item > 0)
                            Need(RequiredDataKind.Item, item, Items, "multisell.json", $"multisell {id}");
                    }
                }
            }
        }

        public void CheckRecipes()
        {
            foreach (var recipe in Root("recipes.json", "recipes"))
            {
                int id = Int(recipe, "id", "recipes.json");
                foreach (int item in Deep(recipe, "itemId").Append(Int(recipe, "recipeItemId", "recipes.json")))
                    Need(RequiredDataKind.Item, item, Items, "recipes.json", $"recipe {id}");
            }
        }

        public void CheckNpcAiSupport()
        {
            foreach (var row in Root("npc-ai-support.json", "npcs"))
            {
                int npc = Int(row, "npcId", "npc-ai-support.json");
                Need(RequiredDataKind.Npc, npc, Npcs, "npc-ai-support.json", "AI support row");
                foreach (var (scope, value) in row!.AsObject())
                {
                    if (value is not JsonArray casts)
                        continue;
                    foreach (var cast in casts)
                        Need(RequiredDataKind.Skill, Int(cast, "id", "npc-ai-support.json"), Skills, "npc-ai-support.json", $"npc {npc} {scope} cast");
                }
            }
        }

        public void CheckHtmlBypasses()
        {
            string root = Path.Combine(_dir, "html");
            if (!Directory.Exists(root))
                throw new RequiredDataException(RequiredDataKind.NpcScript, "html", "html/", $"NPC html pack missing at {root}");
            foreach (string path in Directory.EnumerateFiles(root, "*.htm", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string rel = "html/" + Path.GetRelativePath(root, path).Replace('\\', '/');
                // GM pages (admin_*) are operator tools, not NPC dialogs.
                if (rel.StartsWith("html/admin/", StringComparison.Ordinal))
                    continue;
                var npcMatch = LeadingNpcId().Match(Path.GetFileName(path));
                int? npc = npcMatch.Success ? int.Parse(npcMatch.Groups[1].Value, CultureInfo.InvariantCulture) : null;
                string text = File.ReadAllText(path);

                foreach (Match m in NpcBypass().Matches(text))
                {
                    string verb = m.Groups[1].Value.ToLowerInvariant();
                    string[] args = m.Groups[2].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (args.Length == 0)
                        continue;
                    switch (verb)
                    {
                        case "buy" or "wear" when int.TryParse(args[0], CultureInfo.InvariantCulture, out int list):
                            if (!BuyLists.TryGetValue(list, out var allowed))
                                Report(RequiredDataKind.BuyList, list.ToString(CultureInfo.InvariantCulture), rel, $"{verb} bypass to unknown buylist");
                            else if (npc is int owner && allowed.Count > 0 && !allowed.Contains(owner))
                                Report(RequiredDataKind.BuyList, $"{list}@{owner}", rel, $"buylist {list} does not allow npc {owner}");
                            break;
                        case "multisell" or "exc_multisell" when int.TryParse(args[0], CultureInfo.InvariantCulture, out int ms):
                            if (!MultiSells.Contains(ms))
                                Report(RequiredDataKind.MultiSell, ms.ToString(CultureInfo.InvariantCulture), rel, "multisell bypass to unknown list");
                            break;
                        case "teleport" when npc is int gatekeeper:
                            if (!Teleports.TryGetValue(gatekeeper, out var lists) || !lists.Contains(args[0]))
                                Report(RequiredDataKind.Teleport, $"{gatekeeper}:{args[0]}", rel, $"teleport bypass to list '{args[0]}' that teleports.json does not define for npc {gatekeeper}");
                            break;
                    }
                }

                foreach (Match m in ScriptBypass().Matches(text))
                {
                    string script = m.Groups[1].Value;
                    // "Script <n>" is a quest link (QuestBypassHandler routes positive ids to the quest engine).
                    if (int.TryParse(script, CultureInfo.InvariantCulture, out int questId) && questId > 0)
                    {
                        Need(RequiredDataKind.Quest, questId, Quests, rel, "Script bypass to quest id");
                        continue;
                    }

                    if (!Scripts.Contains(script))
                        Report(RequiredDataKind.NpcScript, script, rel, "Script bypass to a script npc-scripts.json does not register");
                }
            }
        }

        public void CheckStaleKnownAbsent()
        {
            foreach (var entry in _registry.Entries)
            {
                foreach (var hole in entry.KnownAbsent)
                {
                    if (!_knownHit.Contains((entry.Kind, hole.Key)))
                    {
                        Violations.Add(new RequiredDataViolation(
                            entry.Kind,
                            hole.Key,
                            RequiredDataRegistry.FileName,
                            "knownAbsent key is no longer referenced or is now defined; remove it from the registry"));
                    }
                }
            }
        }

        void Need(RequiredDataKind kind, int id, HashSet<int> defined, string source, string detail)
        {
            if (!defined.Contains(id))
                Report(kind, id.ToString(CultureInfo.InvariantCulture), source, detail);
        }

        void Report(RequiredDataKind kind, string key, string source, string detail)
        {
            if (_registry.Get(kind).IsKnownAbsent(key))
            {
                _knownHit.Add((kind, key));
                return;
            }

            // One line per (kind, key): the first referencing file is enough to find the hole.
            if (_seen.Add((kind, key)))
                Violations.Add(new RequiredDataViolation(kind, key, source, detail));
        }

        JsonNode Doc(string file)
        {
            string path = Path.Combine(_dir, file);
            if (!File.Exists(path))
                throw new FileNotFoundException($"required-data cross-check: {file} missing at {path}", path);
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: DocOptions)
                ?? throw Corrupt(file, "<root>");
        }

        JsonArray Root(string file, string key) =>
            Doc(file)[key]?.AsArray() ?? throw Corrupt(file, key);

        IEnumerable<(string Name, JsonNode Doc)> Pack(string directory, string pattern = "*.json")
        {
            string path = Path.Combine(_dir, directory);
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"required-data cross-check: {directory}/ missing at {path}");
            foreach (string file in Directory.EnumerateFiles(path, pattern).Order(StringComparer.Ordinal))
            {
                string name = directory + "/" + Path.GetFileName(file);
                yield return (name, JsonNode.Parse(File.ReadAllText(file), documentOptions: DocOptions) ?? throw Corrupt(name, "<root>"));
            }
        }

        static InvalidDataException Corrupt(string file, string key) =>
            new($"required-data cross-check: {file} has no '{key}'.");

        static JsonArray Arr(JsonNode? node, string key) => node?[key] as JsonArray ?? [];

        static int Int(JsonNode? node, string key, string file) =>
            node?[key]?.GetValue<int>() ?? throw new InvalidDataException(
                $"required-data cross-check: {file} row {node?.ToJsonString()[..Math.Min(120, node.ToJsonString().Length)]} has no '{key}'.");

        static HashSet<int> Ids(JsonArray rows, string key) =>
            rows.Select(r => r?[key]?.GetValue<int>() ?? throw new InvalidDataException($"row without '{key}'")).ToHashSet();

        static IEnumerable<int> Deep(JsonNode? node, string key)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (name, value) in obj)
                    {
                        if (name == key && value is JsonValue v && v.TryGetValue(out int id) && id > 0)
                            yield return id;
                        foreach (int nested in Deep(value, key))
                            yield return nested;
                    }

                    break;
                case JsonArray arr:
                    foreach (var value in arr)
                    {
                        foreach (int nested in Deep(value, key))
                            yield return nested;
                    }

                    break;
            }
        }
    }
}
