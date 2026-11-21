using Fineage.Protocol;

namespace Fineage.Zone.NpcHtml;

/// <summary>Mobius <c>ClanHallManager.onBypassFeedback</c> service intents (no HTML on the zone server).</summary>
public sealed class ClanHallManagerBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } =
    [
        "manage",
        "manage_vault",
        "functions",
        "support",
    ];

    /// <summary>Mobius hall-only verbs (e.g. <c>goto</c>) must not register globally — teleporters use <c>goto</c> too.</summary>
    public static bool IsHallOnlyCommand(string token) =>
        token.Equals("goto", StringComparison.OrdinalIgnoreCase);

    public BypassReply Handle(BypassCall call)
    {
        var token = BypassCommands.Token(call.Command);
        return token switch
        {
            "manage" when call.Command.Contains(" deco ", StringComparison.OrdinalIgnoreCase) =>
                BypassReply.ServiceCall(NpcServiceIntent.HallDeco, call.Command),
            "manage" => BypassReply.ServiceCall(NpcServiceIntent.HallFunction, call.Command),
            "manage_vault" => BypassReply.ServiceCall(NpcServiceIntent.HallLease, call.Command),
            "support" => BypassReply.ServiceCall(NpcServiceIntent.HallSupport, call.Command),
            "goto" => BypassReply.ServiceCall(NpcServiceIntent.HallTeleport, call.Command),
            _ => BypassReply.ServiceCall(NpcServiceIntent.HallFunction, call.Command)
        };
    }
}