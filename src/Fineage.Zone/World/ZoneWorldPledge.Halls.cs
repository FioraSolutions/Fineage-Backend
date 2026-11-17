using Fineage.Accounts;
using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Clan hall ownership and the auction core after Mobius <c>ClanHallAuction</c>, <c>ClanHallAuctionManager</c> and the
/// <c>Auctioneer</c> bidding bypasses: a level 2+ clan without a hall bids (HALL_AUCTION privilege) with adena from its
/// clan warehouse, one hall at a time; a new bid must beat the highest (or reach the starting bid); cancelling refunds
/// 90%. When the 7-day auction ends the highest bidder owns the hall, the rest get 90% back; with no bids the auction
/// restarts. Seller-side auctions, lease fees and hall functions are not implemented yet.
/// </summary>
public sealed partial class ZoneWorld
{
    const long HallAuctionMs = 7L * 24 * 3600 * 1000;
    const int HallMinClanLevel = 2;

    ClanHallCatalog? _hallCatalog;
    Dictionary<int, ClanHallSave>? _halls;

    internal ClanHallCatalog HallCatalog
    {
        get => _hallCatalog ??= ClanHallCatalog.LoadDefault();
        set
        {
            _hallCatalog = value;
            _halls = null;
        }
    }

    Dictionary<int, ClanHallSave> Halls(long now)
    {
        if (_halls != null)
            return _halls;
        var stored = (_accounts?.ListClanHalls() ?? []).ToDictionary(h => h.Id);
        _halls = [];
        foreach (var info in HallCatalog.Halls.OrderBy(h => h.Id))
        {
            var hall = stored.TryGetValue(info.Id, out var s) ? s : new ClanHallSave { Id = info.Id, StartingBid = info.StartingBid };
            if (hall.StartingBid <= 0)
                hall.StartingBid = info.StartingBid;
            if (hall.OwnerClanId != 0 && !_clans.ContainsKey(hall.OwnerClanId))
                hall.OwnerClanId = 0;
            if (hall.OwnerClanId == 0 && hall.AuctionEndUnixMs <= 0)
                hall.AuctionEndUnixMs = now + HallAuctionMs;
            _halls[hall.Id] = hall;
        }
        foreach (var clan in _clans.Values)
        {
            var owned = _halls.Values.FirstOrDefault(h => h.OwnerClanId == clan.Id);
            clan.D.HallId = owned?.Id ?? 0;
        }
        return _halls;
    }

    void SaveHalls() => _accounts?.SaveClanHalls([.. (_halls ?? []).Values.OrderBy(h => h.Id)]);

    /// <summary>The hall this clan has a live bid on (Mobius <c>getAuctionBiddedAt</c>), 0 none.</summary>
    int BidHallOf(int clanId, long now) =>
        Halls(now).Values.FirstOrDefault(h => h.Bids.Any(b => b.ClanId == clanId))?.Id ?? 0;

    string? SendHallList(ZoneSession session, long now)
    {
        int clanId = ClanIdFor(session.ObjectId);
        var rows = Halls(now).Values.OrderBy(h => h.Id).Select(h =>
        {
            HallCatalog.TryGet(h.Id, out var info);
            return new ClanHallNet
            {
                HallId = h.Id,
                Name = info?.Name ?? "",
                Location = info?.Location ?? "",
                Grade = info?.Grade ?? 0,
                Lease = info?.Lease ?? 0,
                OwnerClanId = h.OwnerClanId,
                OwnerClanName = _clans.TryGetValue(h.OwnerClanId, out var owner) ? owner.Name : "",
                AuctionEndUnixMs = h.OwnerClanId == 0 ? h.AuctionEndUnixMs : 0,
                StartingBid = h.StartingBid,
                HighestBid = h.Bids.Count == 0 ? 0 : h.Bids.Max(b => b.Amount),
                BidCount = h.Bids.Count,
                OwnBid = clanId == 0 ? 0 : h.Bids.FirstOrDefault(b => b.ClanId == clanId)?.Amount ?? 0
            };
        }).ToArray();
        NetBridge?.FanoutClanHallList([session.ObjectId], new ClanHallListNetMsg { Halls = rows });
        return null;
    }

    string? BidOnHall(ZoneSession session, ZoneClan clan, int hallId, long amount, long now)
    {
        var halls = Halls(now);
        if (!halls.TryGetValue(hallId, out var hall) || hall.OwnerClanId != 0)
            return PledgeProtocol.BadArgument;
        if (clan.Level < HallMinClanLevel)
        {
            SelfLine(session, 0, "Your clan's level needs to be at least 2 before you can participate in an auction.");
            return PledgeProtocol.Conditions;
        }
        if (!HasPriv(clan, session.ObjectId, PledgeProtocol.PrivHallAuction))
            return NotAuthorized(session);
        int other = BidHallOf(clan.Id, now);
        if (clan.D.HallId != 0 || (other != 0 && other != hallId))
        {
            SelfLine(session, 0, "You can't bid at more than one auction.");
            return PledgeProtocol.Conditions;
        }
        long highest = hall.Bids.Count == 0 ? 0 : hall.Bids.Max(b => b.Amount);
        if (amount > int.MaxValue || amount < hall.StartingBid || (hall.Bids.Count > 0 && amount <= highest))
        {
            SelfLine(session, 677, "Your bid price must be higher than the minimum price that can be bid.");
            return PledgeProtocol.BadArgument;
        }

        var own = hall.Bids.FirstOrDefault(b => b.ClanId == clan.Id);
        long required = amount - (own?.Amount ?? 0);
        var wh = ClanWarehouseOf(clan)!;
        var adena = wh.FindByItemId(PledgeProtocol.AdenaItemId);
        if (adena == null || adena.Count < required)
        {
            SelfLine(session, 279, "You do not have enough adena.");
            return PledgeProtocol.Conditions;
        }
        wh.TryTake(adena.ObjectId, (int)required, out _, out _);
        if (own == null)
        {
            own = new ClanHallBidSave { ClanId = clan.Id };
            hall.Bids.Add(own);
        }
        own.Amount = amount;
        own.BidderName = session.Name;
        own.TimeUnixMs = now;
        SaveClan(clan);
        SaveHalls();
        SelfLine(session, 0, "You have bid in a clan hall auction.");
        return null;
    }

    string? CancelHallBid(ZoneSession session, ZoneClan clan, int hallId)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        if (!HasPriv(clan, session.ObjectId, PledgeProtocol.PrivHallAuction))
            return NotAuthorized(session);
        if (!Halls(now).TryGetValue(hallId, out var hall))
            return PledgeProtocol.BadArgument;
        var own = hall.Bids.FirstOrDefault(b => b.ClanId == clan.Id);
        if (own == null)
            return PledgeProtocol.BadArgument;
        hall.Bids.Remove(own);
        RefundHallBid(clan, own.Amount, tax: true);
        SaveHalls();
        SelfLine(session, 0, "You have canceled your bid.");
        return null;
    }

    /// <summary>Mobius <c>returnItem</c>: adena back to the clan warehouse, minus 10% when <paramref name="tax"/>.</summary>
    void RefundHallBid(ZoneClan clan, long amount, bool tax)
    {
        long back = tax ? (long)(amount * 0.9) : amount;
        if (back > 0)
            ClanWarehouseOf(clan)!.Add(PledgeProtocol.AdenaItemId, (int)Math.Min(back, int.MaxValue), 0, out _);
        SaveClan(clan);
    }

    /// <summary>Mobius <c>ClanHallAuction.endAuction</c> for every hall whose auction time is up.</summary>
    void TickHallAuctions(long now)
    {
        if (_halls == null && HallCatalog.Halls.Count == 0)
            return;
        bool changed = false;
        foreach (var hall in Halls(now).Values)
        {
            if (hall.OwnerClanId != 0 || hall.AuctionEndUnixMs > now)
                continue;
            changed = true;
            var winner = hall.Bids
                .Where(b => _clans.TryGetValue(b.ClanId, out var c) && c.D.HallId == 0)
                .OrderByDescending(b => b.Amount).ThenBy(b => b.TimeUnixMs).FirstOrDefault();
            foreach (var bid in hall.Bids)
            {
                if (bid != winner && _clans.TryGetValue(bid.ClanId, out var loser))
                    RefundHallBid(loser, bid.Amount, tax: true);
            }
            hall.Bids.Clear();
            if (winner == null || !_clans.TryGetValue(winner.ClanId, out var clan))
            {
                hall.AuctionEndUnixMs = now + HallAuctionMs;
                continue;
            }
            hall.OwnerClanId = clan.Id;
            hall.AuctionEndUnixMs = 0;
            hall.PaidUntilUnixMs = now + HallAuctionMs;
            clan.D.HallId = hall.Id;
            SaveClan(clan);
            BroadcastClan(clan, charInfo: true);
            HallCatalog.TryGet(hall.Id, out var info);
            ClanLine(OnlineIds(clan), 0, $"Congratulations, your clan has won the clan hall {info?.Name ?? hall.Id.ToString()}!");
        }
        if (changed)
            SaveHalls();
    }

    /// <summary>Clan destroyed: its hall returns to auction and its live bid is dropped without refund target.</summary>
    void ReleaseClanHalls(int clanId)
    {
        if (_halls == null && _accounts == null)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        bool changed = false;
        foreach (var hall in Halls(now).Values)
        {
            if (hall.OwnerClanId == clanId)
            {
                hall.OwnerClanId = 0;
                hall.PaidUntilUnixMs = 0;
                hall.AuctionEndUnixMs = now + HallAuctionMs;
                changed = true;
            }
            changed |= hall.Bids.RemoveAll(b => b.ClanId == clanId) > 0;
        }
        if (changed)
            SaveHalls();
    }

    /// <summary>Test hook: force every running auction to end now.</summary>
    internal void EndHallAuctionsForTest(long now)
    {
        lock (_gate)
        {
            foreach (var hall in Halls(now).Values)
            {
                if (hall.OwnerClanId == 0)
                    hall.AuctionEndUnixMs = now;
            }
            TickHallAuctions(now);
        }
    }

    internal (int Owner, int Bids, long Highest)? HallForTest(int hallId)
    {
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            return Halls(now).TryGetValue(hallId, out var h)
                ? (h.OwnerClanId, h.Bids.Count, h.Bids.Count == 0 ? 0 : h.Bids.Max(b => b.Amount))
                : null;
        }
    }
}

public sealed partial class ZoneWorld
{
    /// <summary>Test hook: drop items straight into a clan warehouse.</summary>
    internal bool AddClanWarehouseItemForTest(int clanId, int itemId, int count)
    {
        lock (_gate)
        {
            if (!_clans.TryGetValue(clanId, out var clan))
                return false;
            ClanWarehouseOf(clan)!.Add(itemId, count, 0, out _);
            return SaveClan(clan);
        }
    }
}
