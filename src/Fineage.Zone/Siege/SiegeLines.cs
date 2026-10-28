using Fineage.Protocol;

namespace Fineage.Zone.Siege;

public static class SiegeLines
{
    public static SystemMessageMsg RegisteredAttacker(string castle) =>
        Line(SiegeProtocol.RegisteredAttacker, $"Your clan has registered to attack {castle}.", castle);

    public static SystemMessageMsg RegisteredDefender(string castle) =>
        Line(SiegeProtocol.RegisteredDefender, $"Your clan has asked to defend {castle}. The castle owner must approve.", castle);

    public static SystemMessageMsg Unregistered(string castle) =>
        Line(SiegeProtocol.Unregistered, $"Your clan has withdrawn from the siege of {castle}.", castle);

    public static SystemMessageMsg DefenderApproved(string castle) =>
        Line(SiegeProtocol.DefenderApproved, $"The defender was approved for {castle}.", castle);

    public static SystemMessageMsg DefenderRefused(string castle) =>
        Line(SiegeProtocol.DefenderRefused, $"The defender was refused for {castle}.", castle);

    public static SystemMessageMsg RegistrationEnded(string castle) =>
        Line(SiegeProtocol.RegistrationEnded, $"The registration term for {castle} has ended.", castle);

    public static SystemMessageMsg SiegeStarted(string castle) =>
        Line(SiegeProtocol.SiegeStarted, $"The siege of {castle} has started.", castle);

    public static SystemMessageMsg SiegeFinished(string castle) =>
        Line(SiegeProtocol.SiegeFinished, $"The siege of {castle} has finished.", castle);

    public static SystemMessageMsg SiegeCancelled(string castle) =>
        Line(SiegeProtocol.SiegeCancelled, $"The siege of {castle} was cancelled because no attackers registered.", castle);

    public static SystemMessageMsg CastleCaptured(string castle) =>
        Line(SiegeProtocol.CastleCaptured, $"The attacking clan has taken {castle}.", castle);

    static SystemMessageMsg Line(int id, string text, string castle) => new()
    {
        Id = id,
        Params = [castle],
        Text = text
    };
}
