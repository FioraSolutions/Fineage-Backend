namespace Fineage.Game.Mobius;

/// <summary>
/// Mobius datapack folders under <see cref="MobiusXmlRoot"/> and their Fineage JSON targets.
/// </summary>
public static class MobiusDataPaths
{
    public const string StatsItems = "stats/items";
    public const string StatsNpcs = "stats/npcs";
    public const string StatsSkills = "stats/skills";
    public const string SpawnsRoot = "spawns";

    public const string FineageItemsJson = "items.json";
    public const string FineageNpcsJson = "npcs.json";
    public const string FineageSkillsJson = "skills.json";
    public const string FineageSpawnsDir = "spawns";

    /// <summary>Race starting areas — noted for future client race tiles.</summary>
    public static readonly string[] RaceVillageSpawnFolders =
    [
        "ElvenTerritory",
        "DarkElfTerritory",
        "OrcTerritory",
        "DwarvenTerritory"
    ];

    public static string ItemsDir(string xmlRoot) => Path.Combine(xmlRoot, StatsItems);
    public static string NpcsDir(string xmlRoot) => Path.Combine(xmlRoot, StatsNpcs);
    public static string SkillsDir(string xmlRoot) => Path.Combine(xmlRoot, StatsSkills);
    public static string SpawnsDir(string xmlRoot) => Path.Combine(xmlRoot, SpawnsRoot);
}
