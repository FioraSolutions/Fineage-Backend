using Fineage.Protocol;

namespace Fineage.Zone.NpcHtml;

/// <summary>Mobius <c>ClanHallDoorman.evolve N</c> → <see cref="PetManager.doEvolve"/>.</summary>
public sealed class PetEvolveBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["evolve"];

    public BypassReply Handle(BypassCall call)
    {
        var parts = call.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !int.TryParse(parts[^1], out int caseId) || caseId <= 0)
            return BypassReply.Fail("bad_request");
        return BypassReply.ServiceCall(NpcServiceIntent.PetEvolve, caseId.ToString(), caseId);
    }
}