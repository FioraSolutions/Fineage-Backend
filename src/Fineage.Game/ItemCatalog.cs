namespace Fineage.Game;

public sealed class ItemCatalog
{
    readonly Dictionary<int, ItemTemplate> _byId;

    public ItemCatalog(IEnumerable<ItemTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        var list = new List<ItemTemplate>();
        var map = new Dictionary<int, ItemTemplate>();
        foreach (var template in templates)
        {
            ArgumentNullException.ThrowIfNull(template);
            if (template.Id <= 0)
                throw new ArgumentOutOfRangeException(nameof(templates), $"Item id {template.Id} must be positive.");
            if (string.IsNullOrWhiteSpace(template.Name))
                throw new ArgumentException($"Item {template.Id} needs a name.", nameof(templates));
            if (string.IsNullOrWhiteSpace(template.Icon))
                throw new ArgumentException($"Item {template.Id} needs an icon.", nameof(templates));
            if (template.PDef < 0)
                throw new ArgumentOutOfRangeException(nameof(templates), $"Item {template.Id} pDef cannot be negative.");
            if (template.CrystalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(templates), $"Item {template.Id} crystal count cannot be negative.");
            if (template.ReferencePrice < 0)
                throw new ArgumentOutOfRangeException(nameof(templates), $"Item {template.Id} price cannot be negative.");
            if (template.Weight < 0)
                throw new ArgumentOutOfRangeException(nameof(templates), $"Item {template.Id} weight cannot be negative.");
            if (template.Scroll != null)
            {
                if (template.Kind != ItemKind.Etc || template.Enchantable || template.Equippable)
                    throw new ArgumentException($"Item {template.Id} scroll must be a non-equippable etc item.", nameof(templates));
                if (template.Scroll.Grade == CrystalGrade.None)
                    throw new ArgumentException($"Item {template.Id} scroll needs a target grade.", nameof(templates));
                if (template.Scroll.MaxEnchant < 1 || template.Scroll.MaxEnchant > EnchantScroll.DefaultMaxEnchant)
                    throw new ArgumentOutOfRangeException(nameof(templates), $"Item {template.Id} scroll max enchant is out of range.");
                if (template.Scroll.BonusRate < 0 || template.Scroll.BonusRate > 100)
                    throw new ArgumentOutOfRangeException(nameof(templates), $"Item {template.Id} scroll bonus rate must be 0..100.");
            }
            if (!map.TryAdd(template.Id, template))
                throw new ArgumentException($"Duplicate item id {template.Id}.", nameof(templates));
            list.Add(template);
        }

        _byId = map;
        Templates = list;
    }

    public IReadOnlyList<ItemTemplate> Templates { get; }

    public bool TryGet(int itemId, out ItemTemplate template) => _byId.TryGetValue(itemId, out template!);

    public ItemTemplate Get(int itemId)
    {
        if (_byId.TryGetValue(itemId, out var template))
            return template;
        throw new ArgumentException($"Unknown item id {itemId}.", nameof(itemId));
    }
}
