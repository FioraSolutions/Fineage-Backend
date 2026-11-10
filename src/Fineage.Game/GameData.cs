using System.Text.Json;
using Fineage.Game.Quests;
using Fineage.Game.Recipes;
using Fineage.Game.Skills;

namespace Fineage.Game;

/// <summary>
/// Loads item templates, starter kits, and Interlude class templates from <c>data/</c>.
/// Ids and stats were checked against L2J Mobius Interlude; the JSON is original data, not a copy of Mobius XML.
/// </summary>
public sealed partial class GameData
{
    public const string ItemsFileName = "items.json";
    public const string StarterKitsFileName = "starter-kits.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    GameData(
        ItemCatalog items,
        StarterKits starterKits,
        ClassCatalog? classes,
        QuestCatalog quests,
        SkillCatalog skills,
        SkillTreeCatalog skillTrees,
        RecipeCatalog recipes)
    {
        Items = items;
        StarterKits = starterKits;
        Classes = classes;
        Quests = quests;
        Skills = skills;
        SkillTrees = skillTrees;
        Recipes = recipes;
    }

    public ItemCatalog Items { get; }

    public StarterKits StarterKits { get; }

    /// <summary>Null when the catalog was built in memory for an item test.</summary>
    public ClassCatalog? Classes { get; }

    public QuestCatalog Quests { get; }

    public SkillCatalog Skills { get; }

    public SkillTreeCatalog SkillTrees { get; }

    public RecipeCatalog Recipes { get; }

    public static GameData Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException($"Game data directory not found: {dataDirectory}");

        var items = ReadItems(Path.Combine(dataDirectory, ItemsFileName));
        var kits = ReadKits(Path.Combine(dataDirectory, StarterKitsFileName), items);
        var classes = ClassCatalog.Load(dataDirectory);
        var quests = QuestCatalog.LoadPack(Path.Combine(dataDirectory, QuestCatalog.DirectoryName), items);
        var skills = SkillCatalog.Load(Path.Combine(dataDirectory, SkillCatalog.FileName));
        var skillTrees = SkillTreeCatalog.Load(Path.Combine(dataDirectory, SkillTreeCatalog.FileName));
        var recipes = RecipeCatalog.Load(Path.Combine(dataDirectory, RecipeCatalog.FileName));
        return new GameData(items, kits, classes, quests, skills, skillTrees, recipes);
    }

    public static GameData LoadDefault()
    {
        var directory = FindDataDirectory(AppContext.BaseDirectory)
            ?? FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
        {
            throw new DirectoryNotFoundException(
                "Could not find data/items.json, data/starter-kits.json, and the class catalogs. Pass the data directory to GameData.Load.");
        }

        return Load(directory);
    }

    public static string? FindDataDirectory(string start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start);
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir != null)
        {
            var data = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(data, ItemsFileName)) &&
                File.Exists(Path.Combine(data, StarterKitsFileName)) &&
                File.Exists(Path.Combine(data, ClassCatalog.ClassesFileName)) &&
                File.Exists(Path.Combine(data, ClassCatalog.ExperienceFileName)) &&
                File.Exists(Path.Combine(data, ClassCatalog.StatBonusesFileName)) &&
                File.Exists(Path.Combine(data, SkillCatalog.FileName)) &&
                File.Exists(Path.Combine(data, SkillTreeCatalog.FileName)))
                return data;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>Builds game data from an in-memory catalog. Used by tests that need body slots the shipped catalog does not list.</summary>
    public static GameData From(ItemCatalog items, StarterKits starterKits, QuestCatalog? quests = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(starterKits);
        return new GameData(
            items,
            starterKits,
            classes: null,
            quests ?? QuestCatalog.Empty,
            skills: SkillCatalog.Parse("{\"skills\":[]}", "memory"),
            skillTrees: SkillTreeCatalog.Parse("{\"trees\":[]}", "memory"),
            recipes: RecipeCatalog.Empty);
    }

    public PlayerInventory CreateStarterInventory(int classId, int firstObjectId = PlayerInventory.FirstObjectId) =>
        CreateStarterInventory(classId, new ItemObjectIdAllocator(firstObjectId));

    public PlayerInventory CreateStarterInventory(int classId, ItemObjectIdAllocator ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var inventory = new PlayerInventory(Items, ids);
        inventory.AddStarterKit(StarterKits.ForClass(classId));
        return inventory;
    }
}
