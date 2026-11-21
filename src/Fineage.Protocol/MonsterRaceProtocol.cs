namespace Fineage.Protocol;

/// <summary>Mobius CT_0 Interlude <c>MonsterRaceManager</c> + <c>RaceManager</c> (30995).</summary>
public static class MonsterRaceProtocol
{
    public const int TicketItemId = 4443;
    public const int AdenaItemId = 57;
    public const int RaceManagerNpcId = 30995;
    public const int RunnerCount = 8;
    public const int SpeedSegments = 20;

    public static readonly int[] TicketPrices =
    [
        100, 500, 1000, 5000, 10000, 20000, 50000, 100000
    ];

    // System messages (@ClientString ids from Mobius SystemMessageId.java).
    public const int TicketsAvailable = 816;
    public const int NowSelling = 817;
    public const int SalesEndInMinutes = 818;
    public const int SalesClosedOddsPosted = 819;
    public const int RaceBeginsInMinutes = 820;
    public const int RaceBeginsIn30Seconds = 821;
    public const int RaceCountdownFiveSeconds = 822;
    public const int RaceBeginsInSeconds = 823;
    public const int TheyreOff = 824;
    public const int RaceFinished = 825;
    public const int FirstAndSecondPrizeLanes = 826;
    public const int AcquiredTicket = 371;
    public const int TicketsNoLongerAvailable = 1046;
    public const int PayoutNotWhileSelling = 1044;

    public const string NotSelling = "monster_race_closed";
    public const string Adena = "monster_race_adena";
    public const string BadTicket = "monster_race_ticket";
}