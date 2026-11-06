using System.Text.Json;

namespace Fineage.Game.Recipes;

/// <summary>Mobius <c>RecipeList</c> row from <c>data/recipes.json</c> (Recipes.xml).</summary>
public sealed record RecipeIngredient(int ItemId, int Count);

public sealed record RecipeProduction(int ItemId, int Count);

public sealed record RecipeDefinition(
    int Id,
    int RecipeItemId,
    string Name,
    int Level,
    bool Dwarven,
    int SuccessRate,
    int MpConsume,
    IReadOnlyList<RecipeIngredient> Ingredients,
    RecipeProduction Production);

/// <summary>Loads Mobius craft recipes. Keys: list id (make-self) and recipe scroll item id (learn).</summary>
public sealed class RecipeCatalog
{
    public const string FileName = "recipes.json";
    public const int CreateDwarvenSkillId = 172;
    public const int CreateCommonSkillId = 1320;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, RecipeDefinition> _byId;
    readonly Dictionary<int, RecipeDefinition> _byRecipeItemId;

    RecipeCatalog(Dictionary<int, RecipeDefinition> byId, Dictionary<int, RecipeDefinition> byItem)
    {
        _byId = byId;
        _byRecipeItemId = byItem;
    }

    public static RecipeCatalog Empty { get; } = new([], []);

    public int Count => _byId.Count;

    public bool TryGet(int recipeId, out RecipeDefinition recipe) => _byId.TryGetValue(recipeId, out recipe!);

    public bool TryGetByRecipeItem(int itemId, out RecipeDefinition recipe) =>
        _byRecipeItemId.TryGetValue(itemId, out recipe!);

    public IEnumerable<RecipeDefinition> All => _byId.Values;

    public static RecipeCatalog Load(string path)
    {
        if (!File.Exists(path))
            return Empty;
        var json = File.ReadAllText(path);
        return Parse(json, path);
    }

    public static RecipeCatalog Parse(string json, string where)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"{where} was empty.");
        var byId = new Dictionary<int, RecipeDefinition>();
        var byItem = new Dictionary<int, RecipeDefinition>();
        foreach (var row in file.Recipes ?? [])
        {
            if (row.Id < 1 || row.RecipeItemId < 1 || row.Production == null || row.Production.ItemId < 1)
                continue;
            var ingredients = (row.Ingredients ?? [])
                .Where(i => i.ItemId > 0 && i.Count > 0)
                .Select(i => new RecipeIngredient(i.ItemId, i.Count))
                .ToArray();
            var def = new RecipeDefinition(
                row.Id,
                row.RecipeItemId,
                row.Name ?? "",
                Math.Max(1, row.Level),
                row.Dwarven,
                Math.Clamp(row.SuccessRate, 0, 100),
                Math.Max(0, row.MpConsume),
                ingredients,
                new RecipeProduction(row.Production.ItemId, Math.Max(1, row.Production.Count)));
            byId[def.Id] = def;
            byItem[def.RecipeItemId] = def;
        }
        return new RecipeCatalog(byId, byItem);
    }

    sealed class FileDto
    {
        public List<RowDto>? Recipes { get; set; }
    }

    sealed class RowDto
    {
        public int Id { get; set; }
        public int RecipeItemId { get; set; }
        public string? Name { get; set; }
        public int Level { get; set; }
        public bool Dwarven { get; set; }
        public int SuccessRate { get; set; } = 100;
        public int MpConsume { get; set; }
        public List<IngredientDto>? Ingredients { get; set; }
        public ProductionDto? Production { get; set; }
    }

    sealed class IngredientDto
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    sealed class ProductionDto
    {
        public int ItemId { get; set; }
        public int Count { get; set; } = 1;
    }
}
