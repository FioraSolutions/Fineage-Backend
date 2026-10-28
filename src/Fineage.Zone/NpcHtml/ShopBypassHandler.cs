namespace Fineage.Zone.NpcHtml;

/// <summary>
/// Mobius <c>Buy</c>, <c>Sell</c>, <c>Multisell</c>, and <c>exc_multisell</c>.
/// <c>Exchange</c> is the same inventory-only multisell as <c>exc_multisell</c>.
/// CT 0 has no <c>Exchange</c> bypass class; the command is registered here so the stub is gone.
/// The handler only names the window. The world loads the list and edits the bag.
/// </summary>
public sealed class ShopBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["Buy", "Sell", "Multisell", "exc_multisell", "Exchange"];

    public BypassReply Handle(BypassCall call)
    {
        var token = BypassCommands.Token(call.Command);
        if (token.Equals("Sell", StringComparison.OrdinalIgnoreCase))
            return BypassReply.Sell();
        if (token.Equals("Buy", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryListId(call.Command, out int listId))
                return BypassReply.Page(Fineage.Protocol.ShopProtocol.NoListHtml);
            return BypassReply.Buy(listId);
        }

        if (!TryListId(call.Command, out int multiId))
            return BypassReply.Page(Fineage.Protocol.ShopProtocol.NoListHtml);
        bool inventoryOnly = !token.Equals("Multisell", StringComparison.OrdinalIgnoreCase);
        return BypassReply.MultiSell(multiId, inventoryOnly);
    }

    static bool TryListId(string command, out int listId)
    {
        listId = 0;
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && int.TryParse(parts[1], out listId) && listId > 0;
    }
}
