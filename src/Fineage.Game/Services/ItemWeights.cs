using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>Mobius item template weight by item id. Missing ids weigh 0.</summary>
public sealed class ItemWeights
{
    public const string FileName = "item-weights.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, int> _weights;

    public ItemWeights(IReadOnlyDictionary<int, int>? weights = null)
    {
        _weights = weights == null
            ? new Dictionary<int, int>()
            : new Dictionary<int, int>(weights);
        foreach (var pair in _weights)
        {
            if (pair.Key <= 0 || pair.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(weights), $"Item {pair.Key} weight {pair.Value} is invalid.");
        }
    }

    public static ItemWeights Empty { get; } = new();

    public int Of(int itemId) => _weights.TryGetValue(itemId, out int weight) ? weight : 0;

    public int Load(IEnumerable<(int ItemId, int Count)> stacks)
    {
        long total = 0;
        foreach (var (itemId, count) in stacks)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(stacks));
            total = checked(total + (long)Of(itemId) * count);
        }

        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    public static ItemWeights Parse(string json, string source)
    {
        WeightFileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<WeightFileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not an item weight table.", ex);
        }

        var map = new Dictionary<int, int>();
        if (file?.Weights != null)
        {
            foreach (var pair in file.Weights)
            {
                if (!int.TryParse(pair.Key, out int itemId) || itemId <= 0 || pair.Value < 0)
                    throw new InvalidDataException($"{source} weight '{pair.Key}' is invalid.");
                map[itemId] = pair.Value;
            }
        }

        return new ItemWeights(map);
    }

    public static ItemWeights LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        if (path == null)
            return Empty;
        return Parse(File.ReadAllText(path), path);
    }

    sealed class WeightFileDto
    {
        public Dictionary<string, int>? Weights { get; set; }
    }
}
