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
