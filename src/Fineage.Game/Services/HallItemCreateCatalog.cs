using System.Text.Json;
using Fineage.Game.Shops;

namespace Fineage.Game.Services;

/// <summary>Clan hall item-creation multisells merged from <c>data/clan-hall-item-create.json</c>.</summary>
public sealed class HallItemCreateCatalog
{
    public const string FileName = "clan-hall-item-create.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public HallItemCreateCatalog(IReadOnlyList<MultiSellListDefinition> lists) => Lists = lists;

    public IReadOnlyList<MultiSellListDefinition> Lists { get; }

    public static readonly HallItemCreateCatalog Empty = new([]);

    public static HallItemCreateCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path));
    }

    public static HallItemCreateCatalog Parse(string json)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions);
        if (file?.Lists == null || file.Lists.Count == 0)
            return Empty;

        var lists = new List<MultiSellListDefinition>();
        foreach (var row in file.Lists)
        {
            var entries = new List<MultiSellEntry>();
            int next = 1;
            foreach (var entry in row.Entries ?? [])
            {
                int id = entry.Id > 0 ? entry.Id : next;
                entries.Add(new MultiSellEntry(
                    id,
                    ReadParts(entry.Ingredients),
                    ReadParts(entry.Products)));
                next++;
            }

            lists.Add(new MultiSellListDefinition(
                row.Id,
                row.ApplyTaxes,
                row.MaintainEnchantment,
                row.Npcs ?? [],
                entries));
        }

        return new HallItemCreateCatalog(lists);
    }

    public static MultiSellCatalog MergeInto(MultiSellCatalog baseline, HallItemCreateCatalog hall)
    {
        if (hall.Lists.Count == 0)
            return baseline;
        var merged = new List<MultiSellListDefinition>(baseline.Lists.Count + hall.Lists.Count);
        foreach (var list in baseline.Lists.Values)
            merged.Add(list);
        foreach (var list in hall.Lists)
        {
            if (!baseline.TryGet(list.Id, out _))
                merged.Add(list);
        }

        return MultiSellCatalog.From(merged);
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