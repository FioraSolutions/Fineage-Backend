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

    public static SystemMessageMsg CastleCaptured(string clan, string castle) => new()
    {
        Id = SiegeProtocol.CastleCaptured,
        Params = [clan, castle],
        Text = clan.Length > 0 ? $"Clan {clan} has succeeded in engraving the ruler of {castle}." : $"The attacking clan has taken {castle}."
    };

    public static SystemMessageMsg CancelledOwned(string castle) =>
        Line(SiegeProtocol.SiegeCancelledOwned, $"{castle}'s siege was canceled because there were no clans that participated.", castle);

    public static SystemMessageMsg Victorious(string clan, string castle) => new()
    {
        Id = SiegeProtocol.Victorious,
        Params = [clan, castle],
        Text = $"Clan {clan} is victorious over {castle}'s castle siege!"
    };

    public static SystemMessageMsg Draw(string castle) =>
        Line(SiegeProtocol.Draw, $"The siege of {castle} has ended in a draw.", castle);

    static SystemMessageMsg Line(int id, string text, string castle) => new()
    {
        Id = id,
        Params = [castle],
        Text = text
    };
}
