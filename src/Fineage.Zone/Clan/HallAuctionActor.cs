using Fineage.Accounts;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.Clan;

/// <summary>The auction of <paramref name="HallId"/> ran out of time.</summary>
public readonly record struct HallAuctionDue(int HallId);

/// <summary>
/// Clan hall auctions after Mobius <c>ClanHallAuctionManager</c> as a global actor on the calendar: it holds the hall
/// records and, at the step's wall sample, returns every free hall whose 7-day auction has ended, in hall order. The
/// world settles them against the bidding clans (the winner, the 90% refunds, a restart without bids). Bids and
/// cancellations are requests on <see cref="Halls"/>.
/// </summary>
public sealed class HallAuctionActor() : GlobalActor<HallAuctionDue>(StageName, ActorTime.Calendar)
{
    public const string StageName = "clan.hall-auctions";

    Dictionary<int, ClanHallSave>? _halls;

    /// <summary>The hall records by id; null until the world loaded them (they need the clan list).</summary>
    public Dictionary<int, ClanHallSave>? Halls
    {
        get
        {
            RequireActorThread();
            return _halls;
        }
        set
        {
            RequireActorThread();
            _halls = value;
        }
    }

    protected override void OnStep(long nowUnixMs, List<HallAuctionDue> effects)
    {
        if (_halls == null)
            return;
        foreach (var hall in _halls.Values.OrderBy(h => h.Id))
        {
            if (hall.OwnerClanId == 0 && hall.AuctionEndUnixMs <= nowUnixMs)
                effects.Add(new HallAuctionDue(hall.Id));
        }
    }
}
