using System.Text.Json;

namespace Fineage.Game.Shops;

/// <summary>One multisell list. Entry ids are 1-based in file order, matching Mobius.</summary>
public sealed class MultiSellListDefinition
{
    public MultiSellListDefinition(
        int id,
        bool applyTaxes,
        bool maintainEnchantment,
        IReadOnlyList<int> npcs,
        IReadOnlyList<MultiSellEntry> entries)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
        ApplyTaxes = applyTaxes;
        MaintainEnchantment = maintainEnchantment;
        Npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    public int Id { get; }

    public bool ApplyTaxes { get; }

    public bool MaintainEnchantment { get; }

    public IReadOnlyList<int> Npcs { get; }

    public IReadOnlyList<MultiSellEntry> Entries { get; }

    /// <summary>Mobius <c>isNpcAllowed</c>. An empty NPC set allows nobody, including the -1 wildcard.</summary>
    public bool Allows(int npcId) => Npcs.Contains(npcId);
}

public sealed class MultiSellEntry
{
    public MultiSellEntry(int id, IReadOnlyList<MultiSellPart> ingredients, IReadOnlyList<MultiSellPart> products)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
        Ingredients = ingredients ?? throw new ArgumentNullException(nameof(ingredients));
        Products = products ?? throw new ArgumentNullException(nameof(products));
    }

    public int Id { get; }

    public IReadOnlyList<MultiSellPart> Ingredients { get; }

    public IReadOnlyList<MultiSellPart> Products { get; }
}

public sealed class MultiSellPart
{
    public MultiSellPart(int itemId, int count, int enchant = 0, bool tax = false, bool maintain = false)
    {
        ItemId = itemId;
        Count = count;
        Enchant = enchant;
        Tax = tax;
        Maintain = maintain;
    }

    public int ItemId { get; }

    public int Count { get; }

    public int Enchant { get; }

    /// <summary>Adena line whose count is the castle-tax base, not a normal ingredient.</summary>
    public bool Tax { get; }

    public bool Maintain { get; }
}

public sealed class MultiSellCatalog
{
    public const string FileName = "multisell.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, MultiSellListDefinition> _byId;

    MultiSellCatalog(Dictionary<int, MultiSellListDefinition> byId) => _byId = byId;

    public IReadOnlyDictionary<int, MultiSellListDefinition> Lists => _byId;

    public bool TryGet(int listId, out MultiSellListDefinition list) => _byId.TryGetValue(listId, out list!);

    public static MultiSellCatalog From(IEnumerable<MultiSellListDefinition> lists)
    {
        ArgumentNullException.ThrowIfNull(lists);
        var map = new Dictionary<int, MultiSellListDefinition>();
        foreach (var list in lists)
        {
            if (!map.TryAdd(list.Id, list))
                throw new ArgumentException($"Duplicate multisell {list.Id}.");
        }

        return new MultiSellCatalog(map);
    }

    public static MultiSellCatalog Load(string path)
    {
        var file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"{path} is empty.");
        if (file.Lists == null || file.Lists.Count == 0)
            throw new InvalidDataException($"{path} has no multisell lists.");

        var lists = new List<MultiSellListDefinition>(file.Lists.Count);
        foreach (var row in file.Lists)
        {
            if (row.Id <= 0)
                throw new InvalidDataException($"{path} has a multisell id that is not positive.");
            var entries = new List<MultiSellEntry>();
            int next = 1;
            foreach (var entry in row.Entries ?? [])
            {
                int id = entry.Id > 0 ? entry.Id : next;
                entries.Add(new MultiSellEntry(id, ReadParts(entry.Ingredients), ReadParts(entry.Products)));
                next++;
            }

            lists.Add(new MultiSellListDefinition(
                row.Id,
                row.ApplyTaxes,
                row.MaintainEnchantment,
                row.Npcs ?? [],
                entries));
        }

        try
        {
            return From(lists);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"{path} is not a valid multisell catalog. {ex.Message}", ex);
        }
    }

    public static MultiSellCatalog LoadDefault()
    {
        var directory = GameData.FindDataDirectory(AppContext.BaseDirectory)
            ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory())
            ?? throw new DirectoryNotFoundException("Could not find data/multisell.json.");
        return Load(Path.Combine(directory, FileName));
    }

    static List<MultiSellPart> ReadParts(List<PartDto>? rows)
    {
        var parts = new List<MultiSellPart>();
        foreach (var row in rows ?? [])
            parts.Add(new MultiSellPart(row.Id, row.Count, row.Enchant, row.Tax, row.Maintain));
        return parts;
    }

    sealed class FileDto
    {
        public List<ListDto>? Lists { get; set; }
    }

    sealed class ListDto
    {
        public int Id { get; set; }
        public bool ApplyTaxes { get; set; }
        public bool MaintainEnchantment { get; set; }
        public List<int>? Npcs { get; set; }
        public List<EntryDto>? Entries { get; set; }
    }

    sealed class EntryDto
    {
        public int Id { get; set; }
        public List<PartDto>? Ingredients { get; set; }
        public List<PartDto>? Products { get; set; }
    }

    sealed class PartDto
    {
        public int Id { get; set; }
        public int Count { get; set; }
        public int Enchant { get; set; }
        public bool Tax { get; set; }
        public bool Maintain { get; set; }
    }
}
