using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Clan;
using Fineage.Zone.Siege;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public ChSiegeBoard ChSieges => _chSiege.Board;

    void LoadChSiege()
    {
        if (_accounts == null)
            return;
        var saved = _accounts.ListSiegableClanHalls();
        if (saved.Count > 0)
            _chSiege.Board.Restore(saved, NowUnixMs());
    }

    bool PersistChSiege()
    {
        if (_accounts == null)
            return false;
        return _accounts.SaveSiegableClanHalls(_chSiege.Board.Export());
    }

    void TickChSieges(TickContext tick)
    {
        long now = tick.WallUnixMs;
        foreach (var transition in _chSiege.Step(tick))
        {
            switch (transition)
            {
                case ChCloseRegistrationTransition close:
                    Broadcast(SiegeLines.RegistrationEnded(close.Name));
                    PersistChSiege();
                    break;
                case ChSiegeStartTransition start:
                    ApplyChSiegeStart(start, now);
                    break;
                case ChSiegeEndTransition end:
                    ApplyChSiegeEnd(end);
                    break;
            }
        }
    }

    void ApplyChSiegeStart(ChSiegeStartTransition start, long now)
    {
        if (start.Kind == ChSiegeStartKind.Cancelled)
            Broadcast(SiegeLines.SiegeCancelled(start.Name));
        else if (start.Kind == ChSiegeStartKind.Started)
        {
            Broadcast(SiegeLines.SiegeStarted(start.Name));
            RespawnHallDoors(start.HallId, weak: false);
        }
        PersistChSiege();
    }

    void ApplyChSiegeEnd(ChSiegeEndTransition end)
    {
        Broadcast(SiegeLines.SiegeFinished(end.Name));
        if (end.Won && end.WinnerClanId > 0)
        {
            int hallId = end.HallId;
            foreach (var clan in _clan.All)
            {
                if (clan.D.HallId == hallId && clan.Id != end.WinnerClanId)
                {
                    clan.D.HallId = 0;
                    SaveClan(clan);
                }
            }
            if (_clan.TryGet(end.WinnerClanId, out var winner))
            {
                winner.D.HallId = hallId;
                SaveClan(winner);
                Broadcast(SiegeLines.Victorious(winner.Name, end.Name));
            }
        }
        else
            Broadcast(SiegeLines.Draw(end.Name));
        PersistChSiege();
        SyncClanHallOwnership();
        RespawnHallDoors(end.HallId, weak: false);
    }

    /// <summary>Test / script hook when a hall mission succeeds (flag capture, etc.).</summary>
    public string? CaptureClanHall(int clanId, int hallId, long? nowUnixMs = null)
    {
        long now = nowUnixMs ?? NowUnixMs();
        var hall = _chSiege.Board.Find(hallId);
        if (hall == null || !hall.InProgress)
            return "siege";
        if (!_chSiege.Board.SetPendingWinner(hallId, clanId))
            return "siege";
        _chSiege.Board.EndSiege(hallId, now, out int winner);
        ApplyChSiegeEnd(new ChSiegeEndTransition(hallId, winner, hall.Name, winner > 0));
        return null;
    }

    string? TryChSiegeRegister(ZoneSession session, SiegeRegisterRequestMsg request, SiegeClanInfo? clan, long now)
    {
        bool joining = request.Joining == 1;
        var zoneClan = ClanOf(session.ObjectId);
        int castleId = zoneClan?.CastleId ?? 0;
        int hallId = zoneClan?.D.HallId ?? 0;
        string? error = joining
            ? _chSiege.Board.Register(new SiegeRequester(session.ObjectId), clan, request.CastleId, castleId, hallId,
                cid => _chSiege.Board.ClanRegistered(cid) && !_chSiege.Board.Find(request.CastleId)!.Attackers.Contains(cid),
                now)
            : _chSiege.Board.Unregister(new SiegeRequester(session.ObjectId), clan, request.CastleId, now);
        if (error != null)
            return error;
        PersistChSiege();
        var hall = _chSiege.Board.Find(request.CastleId)!;
        var line = joining
            ? SiegeLines.RegisteredAttacker(hall.Name)
            : SiegeLines.Unregistered(hall.Name);
        FanSystemLine(session, line);
        QueueChSiegeInfo(session, request.CastleId, now);
        return null;
    }

    void QueueChSiegeInfo(ZoneSession session, int hallId, long nowUnixMs)
    {
        var hall = _chSiege.Board.Find(hallId);
        if (hall == null)
            return;
        long regDeadline = hall.NextSiegeUnixMs - _chSiege.Board.Settings.RegistrationCloseBeforeMs;
        var clans = hall.Attackers.Select(id => new SiegeClanMsg
        {
            ClanId = id,
            Side = SiegeProtocol.Attacker
        }).ToArray();
        if (hall.OwnerClanId > 0 && clans.All(c => c.ClanId != hall.OwnerClanId))
        {
            clans = [.. clans, new SiegeClanMsg { ClanId = hall.OwnerClanId, Side = SiegeProtocol.Defender }];
        }

        var msg = new SiegeInfoMsg
        {
            CastleId = hallId,
            Name = hall.Name,
            OwnerClanId = hall.OwnerClanId,
            TaxPercent = 0,
            SiegeUnixMs = hall.NextSiegeUnixMs,
            RegistrationDeadlineUnixMs = regDeadline,
            RegistrationClosed = hall.RegistrationClosed,
            InProgress = hall.InProgress,
            EndsUnixMs = hall.EndsUnixMs,
            Clans = clans
        };
        NetBridge?.FanoutSiegeInfo([session.ObjectId], msg);
    }

    void SyncClanHallOwnership()
    {
        foreach (var hall in _chSiege.Board.Halls)
        {
            if (hall.OwnerClanId <= 0)
                continue;
            if (_clan.TryGet(hall.OwnerClanId, out var owner))
                owner.D.HallId = hall.Id;
        }
    }
}