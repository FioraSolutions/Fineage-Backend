using System.Collections.Frozen;
using Fineage.Game.Services;
using Fineage.Zone.World;

namespace Fineage.Zone.Items;

/// <summary>
/// Mobius <c>ItemHandler</c> table: dispatch by handler name from <see cref="ItemHandlerCatalog"/>.
/// An item with a catalogued name but no registered handler is logged and not consumed (#925).
/// </summary>
internal sealed class ItemHandlerRegistry
{
    readonly ItemHandlerCatalog _catalog;
    readonly FrozenDictionary<string, IItemHandler> _byName;

    public ItemHandlerRegistry(ItemHandlerCatalog catalog, IEnumerable<IItemHandler> handlers)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        var map = new Dictionary<string, IItemHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers ?? throw new ArgumentNullException(nameof(handlers)))
        {
            if (string.IsNullOrWhiteSpace(handler.Name))
                throw new ArgumentException("Item handler name is empty.", nameof(handlers));
            if (!map.TryAdd(handler.Name, handler))
                throw new ArgumentException($"Duplicate item handler '{handler.Name}'.", nameof(handlers));
        }

        _byName = map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static ItemHandlerRegistry CreateDefault(ItemHandlerCatalog catalog) =>
        new(catalog,
        [
            new BookItemHandler(),
            new MapsItemHandler(),
            new RollingDiceItemHandler(),
            new CalculatorItemHandler(),
            new SpecialXMasItemHandler()
        ]);

    /// <summary>True when <paramref name="itemId"/> has a Mobius named handler entry.</summary>
    public bool IsCatalogued(int itemId) => _catalog.TryGet(itemId, out _);

    /// <summary>
    /// When the item id is not in the catalog, returns false and leaves existing UseItem paths alone.
    /// When it is catalogued but the handler name is missing from the registry, logs and returns true with Ok
    /// so the item is not consumed and ItemSkills is not tried.
    /// </summary>
    public bool TryDispatch(ItemHandlerCall call, out ItemHandlerResult result)
    {
        result = default;
        if (!_catalog.TryGet(call.Item.ItemId, out string name))
            return false;

        if (!_byName.TryGetValue(name, out var handler))
        {
            ZoneLog.Error($"[item-handler] item {call.Item.ItemId} handler '{name}' is not registered");
            result = ItemHandlerResult.Ok();
            return true;
        }

        result = handler.Use(call);
        return true;
    }
}
