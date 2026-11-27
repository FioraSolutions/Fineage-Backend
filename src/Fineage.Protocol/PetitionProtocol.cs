namespace Fineage.Protocol;

/// <summary>Mobius petition queue rules (<c>PetitionManager</c> / <c>RequestPetition</c>).</summary>
public static class PetitionProtocol
{
    public const int MaxContentLength = 255;
    public const int MaxPerPlayer = 5;
    public const int MaxPending = 25;
    public const int MinType = 1;
    public const int MaxType = 9;

    public const int AcceptedSm = 389;
    public const int OneActiveSm = 390;
    public const int CancelFailedSm = 393;
    public const int ApplicationAcceptedSm = 406;
    public const int BeingProcessedSm = 407;
    public const int WaitingListSm = 601;
    public const int UnavailableSm = 602;
    public const int NoGmsSm = 702;
    public const int SubmittedTodaySm = 730;
    public const int CanceledSm = 736;
    public const int NotSubmittedSm = 738;
    public const int NotInChatSm = 745;
    public const int TooLongSm = 971;
    public const int PetitionServerErrorSm = 381;

    public const string AcceptedText = "Your petition application has been accepted.";
    public const string OneActiveText = "You may only submit one petition (active) at a time.";
    public const string NoGmsText =
        "There are no GMs currently visible in the public list as they may be performing other functions at the moment.";
    public const string TooLongText = "Petitions cannot exceed 255 characters.";
    public const string NotSubmittedText = "You have not submitted a petition.";
    public const string BeingProcessedText = "Your petition is being processed.";
    public const string UnavailableText = "The petition system is currently unavailable. Please try again later.";
}

public enum PetitionState
{
    Pending = 0,
    InProcess = 1,
    Completed = 2,
    PetitionerCancel = 3,
    ResponderCancel = 4,
    ResponderReject = 5,
    ResponderMissing = 6,
}
