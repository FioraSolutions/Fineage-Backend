namespace Fineage.Protocol;

/// <summary>Mobius party matching room constants (#928).</summary>
public static class PartyMatchProtocol
{
    public const int Created = 1388;
    public const int Revised = 1389;
    public const int Exited = 1391;
    public const int MemberLeft = 1392;
    public const int Ousted = 1393;
    public const int Disbanded = 1395;
    public const int ListOnlyNonParty = 1396;
    public const int LeaderChanged = 1397;
    public const int LevelRequirements = 1413;
    public const int CannotDismissPartyMember = 1699;
    public const int InviteDeclined = 1728;
    public const int MemberEntered = 1900;

    public const string CreatedText = "You have created a party room.";
    public const string RevisedText = "The party room's information has been revised.";
    public const string ExitedText = "You have exited the party room.";
    public const string MemberLeftText = "{0} has left the party room.";
    public const string OustedText = "You have been ousted from the party room.";
    public const string DisbandedText = "The party room has been disbanded.";
    public const string ListOnlyNonPartyText =
        "The list of party rooms can only be viewed by a person who is not part of a party.";
    public const string LeaderChangedText = "The leader of the party room has changed.";
    public const string LevelRequirementsText = "You do not meet the requirements to enter that party room.";
    public const string CannotDismissPartyMemberText = "You cannot dismiss a party member by force.";
    public const string InviteDeclinedText =
        "The recipient of your invitation did not accept the party matching invitation.";
    public const string MemberEnteredText = "{0} has entered the party room.";
}
