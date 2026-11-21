using Fineage.Protocol;

namespace Fineage.Zone.NpcHtml;

/// <summary>Mobius <c>handlers.bypass.npc.Festival</c>.</summary>
public sealed class FestivalBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["Festival", "FestivalDesc"];

    public BypassReply Handle(BypassCall call)
    {
        string cmd = call.Command.Trim();
        if (cmd.StartsWith("FestivalDesc", StringComparison.OrdinalIgnoreCase))
        {
            string tail = cmd.Length > 12 ? cmd[12..].Trim() : "";
            return BypassReply.ServiceCall(NpcServiceIntent.Festival, $"desc:{ParsePage(tail)}");
        }

        if (cmd.StartsWith("Festival", StringComparison.OrdinalIgnoreCase))
        {
            string tail = cmd.Length > 8 ? cmd[8..].Trim() : "";
            return BypassReply.ServiceCall(NpcServiceIntent.Festival, $"festival:{tail.Replace(' ', ':')}");
        }

        return BypassReply.Fail("bad_request");
    }

    static int ParsePage(string tail)
    {
        if (string.IsNullOrEmpty(tail))
            return 0;
        int i = 0;
        while (i < tail.Length && char.IsDigit(tail[i]))
            i++;
        return i > 0 && int.TryParse(tail[..i], out int page) ? page : 0;
    }
}