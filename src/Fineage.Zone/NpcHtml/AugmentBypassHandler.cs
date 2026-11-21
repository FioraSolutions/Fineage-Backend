using Fineage.Protocol;

namespace Fineage.Zone.NpcHtml;

/// <summary>Mobius blacksmith <c>Augment 1</c> / <c>Augment 2</c> variation windows.</summary>
public sealed class AugmentBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["Augment"];

    public BypassReply Handle(BypassCall call)
    {
        var parts = call.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string variant = parts.Length >= 2 ? parts[1] : "1";
        return variant switch
        {
            "2" => BypassReply.ServiceCall(NpcServiceIntent.AugmentCancel),
            _ => BypassReply.ServiceCall(NpcServiceIntent.AugmentMake)
        };
    }
}