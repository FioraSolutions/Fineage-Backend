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
        TransferSkillTreeCatalog transferSkillTrees,
        SkillEnchantCatalog skillEnchants,
        RecipeCatalog recipes)
    {
        Items = items;
        StarterKits = starterKits;
        Classes = classes;
        Quests = quests;
        Skills = skills;
        SkillTrees = skillTrees;
        TransferSkillTrees = transferSkillTrees;
        SkillEnchants = skillEnchants;
        Recipes = recipes;
    }

    public ItemCatalog Items { get; }

    public StarterKits StarterKits { get; }

    /// <summary>Null when the catalog was built in memory for an item test.</summary>
    public ClassCatalog? Classes { get; }

    public QuestCatalog Quests { get; }

    public SkillCatalog Skills { get; }

    public SkillTreeCatalog SkillTrees { get; }

    public TransferSkillTreeCatalog TransferSkillTrees { get; }

    public SkillEnchantCatalog SkillEnchants { get; }

    public RecipeCatalog Recipes { get; }

    public static GameData Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException($"Game data directory not found: {dataDirectory}");

        var items = ReadItems(Path.Combine(dataDirectory, ItemsFileName));
        var kitPath = Path.Combine(dataDirectory, StarterKitsFileName);
        var kits = ReadKits(kitPath, items);
        var classes = ClassCatalog.Load(dataDirectory);
        kits.RequireClasses(classes.BaseClassIds, kitPath);
        var quests = QuestCatalog.LoadPack(Path.Combine(dataDirectory, QuestCatalog.DirectoryName), items);
        var skills = SkillCatalog.Load(Path.Combine(dataDirectory, SkillCatalog.FileName));
        var skillTrees = SkillTreeCatalog.Load(Path.Combine(dataDirectory, SkillTreeCatalog.FileName));
        var transferPath = Path.Combine(dataDirectory, TransferSkillTreeCatalog.FileName);
        var transferSkillTrees = File.Exists(transferPath)
            ? TransferSkillTreeCatalog.Load(transferPath)
            : TransferSkillTreeCatalog.Empty;
        var skillEnchants = LoadSkillEnchants(Path.Combine(dataDirectory, SkillEnchantCatalog.FileName));
        var recipes = RecipeCatalog.Load(Path.Combine(dataDirectory, RecipeCatalog.FileName));
        var whitelistPath = Path.Combine(dataDirectory, InterludeSkillWhitelist.FileName);
        if (File.Exists(whitelistPath))
        {
            var whitelist = InterludeSkillWhitelist.Load(whitelistPath);
            var violations = InterludeSkillReferenceValidator.ViolationsUnderDataDirectory(dataDirectory, whitelist);
            if (violations.Count > 0)
            {
                throw new InvalidDataException(
                    "Game data references skills that are not in CT_0 Interlude: "
                    + string.Join("; ", violations));
            }

            foreach (var skill in skills.Skills)
            {
                if (!whitelist.Contains(skill.SkillId))
                    throw new InvalidDataException(
                        $"skills.json skillId={skill.SkillId} ({skill.Name}) is not in {InterludeSkillWhitelist.FileName}.");
            }
        }

        return new GameData(items, kits, classes, quests, skills, skillTrees, transferSkillTrees, skillEnchants, recipes);
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
            transferSkillTrees: TransferSkillTreeCatalog.Empty,
            skillEnchants: SkillEnchantCatalog.Empty,
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

    static SkillEnchantCatalog LoadSkillEnchants(string path) =>
        File.Exists(path) ? SkillEnchantCatalog.Load(path) : SkillEnchantCatalog.Empty;
}