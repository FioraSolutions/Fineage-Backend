using Fineage.Protocol;

namespace Fineage.Zone.NpcHtml;

/// <summary>Mobius <c>handlers.bypass.npc.Rift</c>: EnterRift / ChangeRiftRoom / ExitRift.</summary>
public sealed class RiftBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["EnterRift", "ChangeRiftRoom", "ExitRift"];

    public BypassReply Handle(BypassCall call)
    {
        string cmd = call.Command.Trim();
        if (cmd.StartsWith("EnterRift", StringComparison.OrdinalIgnoreCase))
        {
            string tail = cmd.Length > 9 ? cmd[9..].Trim() : "";
            if (!int.TryParse(tail, out int type) || type is < 1 or > 6)
                return BypassReply.Fail("bad_request");
            return BypassReply.ServiceCall(NpcServiceIntent.DimensionalRift, "enter", type);
        }

        if (cmd.StartsWith("ChangeRiftRoom", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.DimensionalRift, "change");
        if (cmd.StartsWith("ExitRift", StringComparison.OrdinalIgnoreCase))
            return BypassReply.ServiceCall(NpcServiceIntent.DimensionalRift, "exit");
        return BypassReply.Fail("bad_request");
    }
}