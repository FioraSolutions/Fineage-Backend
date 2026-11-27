using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Items;

/// <summary>Mobius <c>Book</c>: open <c>data/html/help/{itemId}.htm</c>.</summary>
internal sealed class BookItemHandler : IItemHandler
{
    public string Name => "Book";

    public ItemHandlerResult Use(ItemHandlerCall call)
    {
        call.World.UseBookItem(call.Session, call.Player, call.Item.ItemId);
        return ItemHandlerResult.Ok();
    }
}

/// <summary>Mobius <c>Maps</c>: <c>ShowMiniMap</c>.</summary>
internal sealed class MapsItemHandler : IItemHandler
{
    public string Name => "Maps";

    public ItemHandlerResult Use(ItemHandlerCall call)
    {
        call.World.UseMapItem(call.Session, call.Item.ItemId);
        return ItemHandlerResult.Ok();
    }
}

/// <summary>Mobius <c>RollingDice</c>: roll 1–6, broadcast <c>Dice</c> + system message.</summary>
internal sealed class RollingDiceItemHandler : IItemHandler
{
    public string Name => "RollingDice";

    public ItemHandlerResult Use(ItemHandlerCall call)
    {
        call.World.UseDiceItem(call.Session, call.Player, call.Item.ItemId);
        return ItemHandlerResult.Ok();
    }
}

/// <summary>Mobius <c>Calculator</c>: <c>ShowCalculator</c>.</summary>
internal sealed class CalculatorItemHandler : IItemHandler
{
    public string Name => "Calculator";

    public ItemHandlerResult Use(ItemHandlerCall call)
    {
        call.World.UseCalculatorItem(call.Session, call.Item.ItemId);
        return ItemHandlerResult.Ok();
    }
}

/// <summary>Mobius <c>SpecialXMas</c>: <c>ShowXMasSeal</c>.</summary>
internal sealed class SpecialXMasItemHandler : IItemHandler
{
    public string Name => "SpecialXMas";

    public ItemHandlerResult Use(ItemHandlerCall call)
    {
        call.World.UseXMasSealItem(call.Session, call.Item.ItemId);
        return ItemHandlerResult.Ok();
    }
}
