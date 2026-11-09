using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Game;

/// <summary>One skill granted while an item is equipped (Mobius Inventory <c>item_skill</c>).</summary>
public readonly record struct ItemEquipSkill(int Id, int Level);

/// <summary>
/// Mobius <c>stats/items</c> equip skills (<c>data/item-equip-skills.json</c>).
/// Consumable UseItem chains stay in <see cref="ItemSkillCatalog"/>.
/// </summary>
public sealed class ItemEquipSkillCatalog
{
    public const string FileName = "item-equip-skills.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, IReadOnlyList<ItemEquipSkill>> _byItem;

    ItemEquipSkillCatalog(Dictionary<int, IReadOnlyList<ItemEquipSkill>> byItem) => _byItem = byItem;

    public static ItemEquipSkillCatalog Empty { get; } = new([]);

    public int Count => _byItem.Count;

    public bool TryGet(int itemId, out IReadOnlyList<ItemEquipSkill> skills) =>
        _byItem.TryGetValue(itemId, out skills!);

    /// <summary>Merge equip skills for every worn piece into <paramref name="target"/> (higher level wins).</summary>
    public void CollectEquipped(PlayerInventory? inventory, Dictionary<int, int> target)
    {
        if (inventory == null || _byItem.Count == 0)
            return;
        foreach (PaperdollSlot slot in Enum.GetValues<PaperdollSlot>())
        {
            var item = inventory.GetEquipped(slot);
            if (item == null || !_byItem.TryGetValue(item.ItemId, out var skills))
                continue;
            foreach (var skill in skills)
            {
                if (skill.Id <= 0)
                    continue;
                int level = Math.Max(1, skill.Level);
                if (!target.TryGetValue(skill.Id, out int have) || level > have)
                    target[skill.Id] = level;
            }
        }
    }

    public static ItemEquipSkillCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path), path);
    }

    public static ItemEquipSkillCatalog Parse(string json, string source = FileName)
    {
        FileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not an item-equip-skill table.", ex);
        }

        var byItem = new Dictionary<int, IReadOnlyList<ItemEquipSkill>>();
        foreach (var row in file?.Items ?? [])
        {
            if (row.ItemId <= 0)
                throw new InvalidDataException($"{source} has an item row without a valid id.");
            var skills = (row.Skills ?? [])
                .Where(s => s.Id > 0)
                .Select(s => new ItemEquipSkill(s.Id, Math.Max(1, s.Level)))
                .ToArray();
            if (skills.Length == 0)
                continue;
            byItem[row.ItemId] = skills;
        }
        return new ItemEquipSkillCatalog(byItem);
    }

    sealed class FileDto
    {
        public List<RowDto>? Items { get; set; }
    }

    sealed class RowDto
    {
        public int ItemId { get; set; }
        public string? Name { get; set; }
        public List<SkillDto>? Skills { get; set; }
    }

    sealed class SkillDto
    {
        public int Id { get; set; }
        public int Level { get; set; } = 1;
    }
}
