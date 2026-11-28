using Fineage.Game;
using Fineage.Zone.World;

namespace Fineage.Zone.Items;

/// <summary>
/// Mobius <c>IItemHandler</c>: one named handler for etc-item use.
/// Returns null on success (or a soft refusal that already notified the client).
/// A missing registration is logged by the registry — handlers themselves always exist when invoked.
/// </summary>
internal interface IItemHandler
{
    string Name { get; }

    /// <summary>Apply the item. Does not consume the stack unless the handler returns a consume flag.</summary>
    ItemHandlerResult Use(ItemHandlerCall call);
}

internal readonly record struct ItemHandlerCall(
    ZoneSession Session,
    ZoneEntity Player,
    ItemInstance Item,
    ZoneWorld World);

/// <summary>Outcome of a named item handler. Items are not consumed for Book/Maps/Dice/Calculator/XMas.</summary>
internal readonly record struct ItemHandlerResult(string? Error = null)
{
    public static ItemHandlerResult Ok() => new();
    public static ItemHandlerResult Fail(string error) => new(error);
}
