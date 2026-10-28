using Fineage.Protocol;

namespace Fineage.Zone.Clan;

/// <summary>System lines for clan events. <see cref="SystemMessageMsg.Text"/> is what the client shows.</summary>
public static class ClanLines
{
    public static SystemMessageMsg Created() =>
        Line(ClanProtocol.Created, "The clan has been created.");

    public static SystemMessageMsg Entered(string clanName) =>
        Line(ClanProtocol.Entered, $"You have joined {clanName}.", clanName);

    public static SystemMessageMsg MemberJoined(string name) =>
        Line(ClanProtocol.MemberJoined, $"{name} has joined the clan.", name);

    public static SystemMessageMsg InviteDeclinedSelf(string requestorName) =>
        Line(ClanProtocol.InviteDeclinedSelf, $"You declined {requestorName}'s clan invitation.", requestorName);

    public static SystemMessageMsg InviteDeclinedRequestor(string targetName) =>
        Line(ClanProtocol.InviteDeclinedRequestor, $"{targetName} declined the clan invitation.", targetName);

    public static SystemMessageMsg Withdrawn() =>
        Line(ClanProtocol.Withdrawn, "You have left the clan.");

    public static SystemMessageMsg MemberWithdrawn(string name) =>
        Line(ClanProtocol.MemberWithdrawn, $"{name} has left the clan.", name);

    public static SystemMessageMsg JoinWait() =>
        Line(ClanProtocol.JoinWait, $"You must wait {ClanProtocol.JoinDays} day before joining another clan.");

    public static SystemMessageMsg Expelled(string name) =>
        Line(ClanProtocol.Expelled, $"{name} was dismissed from the clan.", name);

    public static SystemMessageMsg ExpelledLeader() =>
        Line(ClanProtocol.ExpelledLeader, "Clan member dismissed.");

    public static SystemMessageMsg RecruitWait() =>
        Line(ClanProtocol.RecruitWait, $"The clan must wait {ClanProtocol.JoinDays} day before accepting a new member.");

    public static SystemMessageMsg Dismissed() =>
        Line(ClanProtocol.Dismissed, $"You were dismissed from the clan. You must wait {ClanProtocol.JoinDays} day before joining another clan.");

    public static SystemMessageMsg DissolveRequested() =>
        Line(ClanProtocol.DissolveRequested, $"Clan dissolution has been registered. The clan dissolves in {ClanProtocol.DissolveDays} days.");

    public static SystemMessageMsg DissolveCancelled() =>
        Line(ClanProtocol.DissolveCancelled, "Clan dissolution was cancelled.");

    public static SystemMessageMsg Dispersed() =>
        Line(ClanProtocol.Dispersed, "The clan has been dissolved.");

    static SystemMessageMsg Line(int id, string text, params string[] args) => new()
    {
        Id = id,
        Params = args,
        Text = text
    };
}
