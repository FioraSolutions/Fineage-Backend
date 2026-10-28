using System.Text.Json;

namespace Fineage.Game.Shops;

/// <summary>One merchant buylist. Prices are the Mobius unit price before castle and town tax.</summary>
public sealed class BuyListDefinition
{
    public BuyListDefinition(int id, IReadOnlyList<int> npcs, IReadOnlyList<BuyProduct> products)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
        Npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        Products = products ?? throw new ArgumentNullException(nameof(products));
        var byItem = new Dictionary<int, BuyProduct>();
        foreach (var product in products)
        {
            if (!byItem.TryAdd(product.ItemId, product))
                throw new ArgumentException($"Buylist {id} lists item {product.ItemId} twice.");
        }

        ByItem = byItem;
    }

    public int Id { get; }

    public IReadOnlyList<int> Npcs { get; }

    public IReadOnlyList<BuyProduct> Products { get; }

    public IReadOnlyDictionary<int, BuyProduct> ByItem { get; }

    public bool Allows(int npcId) => Npcs.Contains(npcId);

    public BuyProduct? Find(int itemId) => ByItem.GetValueOrDefault(itemId);
}

/// <summary>One row of a buylist. <see cref="MaxCount"/> below 0 is unlimited.</summary>
public sealed class BuyProduct
{
    public BuyProduct(int itemId, int price, int maxCount = -1, int restockMinutes = -1)
    {
        if (itemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(itemId));
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price));
        ItemId = itemId;
        Price = price;
        MaxCount = maxCount;
        RestockMinutes = restockMinutes;
    }

    public int ItemId { get; }

    public int Price { get; }

    public int MaxCount { get; }

    /// <summary>Minutes until a limited row is full again. Not used when <see cref="MaxCount"/> is below 0.</summary>
    public int RestockMinutes { get; }

    public bool Limited => MaxCount >= 0;
}

/// <summary>Every Interlude buylist. Loaded from <c>data/buylists.json</c>.</summary>
public sealed class BuyListCatalog
{
    public const string FileName = "buylists.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, BuyListDefinition> _byId;
    readonly HashSet<int> _npcs;

    BuyListCatalog(Dictionary<int, BuyListDefinition> byId)
    {
        _byId = byId;
        var npcs = new HashSet<int>();
        foreach (var list in byId.Values)
        {
            foreach (int npc in list.Npcs)
                npcs.Add(npc);
        }

        _npcs = npcs;
    }

    public IReadOnlyDictionary<int, BuyListDefinition> Lists => _byId;

    public bool TryGet(int listId, out BuyListDefinition list) => _byId.TryGetValue(listId, out list!);

    /// <summary>True when any buylist names this NPC. Sell accepts those NPCs plus merchants and fishermen.</summary>
    public bool NpcListed(int npcId) => _npcs.Contains(npcId);

    public static BuyListCatalog From(IEnumerable<BuyListDefinition> lists)
    {
        ArgumentNullException.ThrowIfNull(lists);
        var map = new Dictionary<int, BuyListDefinition>();
        foreach (var list in lists)
        {
            if (!map.TryAdd(list.Id, list))
                throw new ArgumentException($"Duplicate buylist {list.Id}.");
        }

        return new BuyListCatalog(map);
    }

    public static BuyListCatalog Load(string path)
    {
        var file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"{path} is empty.");
        if (file.Lists == null || file.Lists.Count == 0)
            throw new InvalidDataException($"{path} has no buylists.");

        var lists = new List<BuyListDefinition>(file.Lists.Count);
        foreach (var row in file.Lists)
        {
            if (row.Id <= 0)
                throw new InvalidDataException($"{path} has a buylist id that is not positive.");
            var products = new List<BuyProduct>();
            foreach (var item in row.Items ?? [])
            {
                if (item.Id <= 0)
                    throw new InvalidDataException($"{path} buylist {row.Id} has an item id that is not positive.");
                if (item.Price < 0)
                    throw new InvalidDataException($"{path} buylist {row.Id} item {item.Id} has a negative price.");
                products.Add(new BuyProduct(item.Id, item.Price, item.Count, item.RestockMinutes));
            }

            lists.Add(new BuyListDefinition(row.Id, row.Npcs ?? [], products));
        }

        try
        {
            return From(lists);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"{path} is not a valid buylist catalog. {ex.Message}", ex);
        }
    }

    public static BuyListCatalog LoadDefault()
    {
        var directory = GameData.FindDataDirectory(AppContext.BaseDirectory)
            ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory())
            ?? throw new DirectoryNotFoundException("Could not find data/buylists.json.");
        return Load(Path.Combine(directory, FileName));
    }

    sealed class FileDto
    {
        public List<ListDto>? Lists { get; set; }
    }

    sealed class ListDto
    {
        public int Id { get; set; }
        public List<int>? Npcs { get; set; }
        public List<ItemDto>? Items { get; set; }
    }

    sealed class ItemDto
    {
        public int Id { get; set; }
        public int Price { get; set; }
        public int Count { get; set; } = -1;
        public int RestockMinutes { get; set; } = -1;
    }
}
