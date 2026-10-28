namespace Fineage.Protocol;

/// <summary>Player trade on the zone port. Research: Project store <c>docs/trade-java-l2.md</c>.</summary>
public static class TradeProtocol
{
    /// <summary>Mobius Interlude trade range (3D).</summary>
    public const int Distance = 150;

    public const int MaxOfferRows = 8;

    public const string Busy = "trade_busy";
    public const string Invite = "trade_invite";
    public const string Session = "trade";
    public const string Item = "trade_item";
    public const string Range = "trade_range";
}
