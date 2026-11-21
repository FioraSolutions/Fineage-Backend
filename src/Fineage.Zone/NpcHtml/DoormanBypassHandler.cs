using Fineage.Protocol;

namespace Fineage.Zone.NpcHtml;

/// <summary>Mobius <c>Doorman.onBypassFeedback</c>: <c>open_doors</c> / <c>close_doors</c> with door id tokens.</summary>
public sealed class DoormanBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["open_doors", "close_doors"];

    public BypassReply Handle(BypassCall call)
    {
        bool open = call.Command.StartsWith("open_doors", StringComparison.OrdinalIgnoreCase);
        return BypassReply.ServiceCall(open ? NpcServiceIntent.DoormanOpen : NpcServiceIntent.DoormanClose, call.Command);
    }
}