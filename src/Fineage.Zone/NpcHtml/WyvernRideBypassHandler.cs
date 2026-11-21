using Fineage.Protocol;

namespace Fineage.Zone.NpcHtml;

/// <summary>Mobius wyvern manager <c>RideWyvern</c> bypass (castle lord, strider out, B crystals).</summary>
public sealed class WyvernRideBypassHandler : IBypassHandler
{
    public IReadOnlyList<string> Commands { get; } = ["RideWyvern"];

    public BypassReply Handle(BypassCall call) => BypassReply.ServiceCall(NpcServiceIntent.WyvernRide);
}