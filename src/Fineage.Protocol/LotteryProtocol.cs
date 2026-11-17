namespace Fineage.Protocol;

/// <summary>
/// Lucky lottery after L2J Mobius CT_0 Interlude (<c>LotteryManager</c>, the <c>Loto</c> npc bypass):
/// five numbers of twenty, a weekly draw, a jackpot that keeps what nobody wins. Rules are reimplemented, not copied.
/// </summary>
public static class LotteryProtocol
{
    public const int TicketItemId = 4442;
    public const int AdenaItemId = 57;
    public const int Numbers = 5;
    public const int MaxNumber = 20;
    /// <summary>Sales stop ten minutes before the draw.</summary>
    public const long SalesCloseMs = 10 * 60_000L;

    // Loto bypass values (Mobius showLotoWindow).
    public const int PageBuy = 0;
    public const int PageContinue = 21;
    public const int PageConfirm = 22;
    public const int PageJackpot = 23;
    public const int PageResults = 24;
    public const int PageRules = 25;

    // System messages.
    public const int Earned = 54;
    public const int NotEnoughAdena = 279;
    public const int Disappeared = 302;
    public const int SalesSuspended = 783;
    public const int NoLongerAvailable = 784;
    public const int NotSelling = 930;
    public const int FirstPrizeWinners = 1112;
    public const int NoFirstPrize = 1113;

    public const string Closed = "lottery_closed";
    public const string Adena = "lottery_adena";
    public const string NotATicket = "lottery_ticket";
}
