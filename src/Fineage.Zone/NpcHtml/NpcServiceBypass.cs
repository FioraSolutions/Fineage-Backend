using Fineage.Protocol;

namespace Fineage.Zone.NpcHtml;

/// <summary>
/// <c>showTeleports</c>, <c>teleport LIST ID</c>, and <c>goto</c> (an alias of teleport).
/// Noblesse selection stays on the stub. The world prices and moves the player.
/// </summary>
public sealed class TeleportBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["Teleport", "goto", "showTeleports"];

    public BypassReply Handle(BypassCall call)
    {
        var parts = call.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return BypassReply.Fail("bad_request");
        var token = parts[0];
        if (token.Equals("showTeleports", StringComparison.OrdinalIgnoreCase))
        {
            var list = parts.Length >= 2 ? parts[1] : TeleportRulesNormal();
            return BypassReply.ServiceCall(NpcServiceIntent.TeleportList, list);
        }

        if (token.Equals("goto", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("teleport", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("Teleport", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length == 2 && int.TryParse(parts[1], out int onlyId))
                return BypassReply.ServiceCall(NpcServiceIntent.Teleport, "NORMAL", onlyId);
            if (parts.Length >= 3 && int.TryParse(parts[2], out int id))
                return BypassReply.ServiceCall(NpcServiceIntent.Teleport, parts[1], id);
            return BypassReply.Fail("bad_request");
        }

        return BypassReply.Fail("bad_request");
    }

    static string TeleportRulesNormal() => "NORMAL";
}

/// <summary>Private warehouse windows. Clan and freight stay stubbed.</summary>
public sealed class WarehouseBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["DepositP", "WithdrawP"];

    public BypassReply Handle(BypassCall call)
    {
        var token = BypassCommands.Token(call.Command);
        if (token.Equals("DepositP", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.Deposit);
        if (token.Equals("WithdrawP", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.Withdraw);
        return BypassReply.Fail("bad_request");
    }
}

/// <summary>Newbie Helper supplemental magic and the Blessing of Protection.</summary>
public sealed class BuffBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["SupportMagic", "SupportBlessing", "GiveBlessing"];

    public BypassReply Handle(BypassCall call)
    {
        var token = BypassCommands.Token(call.Command);
        if (token.Equals("SupportMagic", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.SupportMagic);
        if (token.Equals("SupportBlessing", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.SupportBlessing);
        if (token.Equals("GiveBlessing", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.GiveBlessing);
        return BypassReply.Fail("bad_request");
    }
}
