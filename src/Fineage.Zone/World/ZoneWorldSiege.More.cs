using Fineage.Protocol;
using Fineage.Zone.Siege;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void LoadSiege()
    {
        if (_accounts == null)
        {
            AdoptClanOwners();
            ApplyBoardOwnersToClans();
            return;
        }

        var saved = _accounts.ListCastles();
        if (saved.Count > 0)
            _siege.Board.Restore(saved, _accounts.ListSiegeClans(), NowUnixMs());
        else
            AdoptClanOwners();
        ApplyBoardOwnersToClans();
    }

    void AdoptClanOwners()
    {
        foreach (var clan in _clan.All)
        {
            if (clan.CastleId > 0)
                _siege.Board.TryAdoptOwner(clan.CastleId, clan.Id);
        }
    }

    void ReleaseSiegeClan(int clanId)
    {
        int castleId = _siege.Board.CastleOwnedBy(clanId);
        _siege.Board.ReleaseClan(clanId);
        PersistSiege();
        ResetManorIfOwnerless(castleId);
    }

    string? CaptureLocked(int clanId, int castleId, long nowUnixMs, ZoneSession? actor = null)
    {
        AnnounceClosed(nowUnixMs);
        int target = castleId == 0 ? ActiveSiegeCastleFor(clanId) : castleId;
        int previousOwner = target > 0 ? _siege.Board.Find(target)?.OwnerClanId ?? 0 : 0;
        string? error = castleId == 0
            ? _siege.Board.CaptureCastle(clanId, nowUnixMs)
            : _siege.Board.CaptureCastle(clanId, castleId, nowUnixMs, AllyOfClan);
        if (error != null)
            return error;

        int taken = _siege.Board.CastleOwnedBy(clanId);
        var castle = _siege.Board.Find(taken);
        ApplyBoardOwnersToClans();
        if (!PersistSiege())
            return SiegeProtocol.Store;

        if (castle != null)
        {
            string clanName = _clan.TryGet(clanId, out var winner) ? winner.Name : "";
            Broadcast(SiegeLines.CastleCaptured(clanName, castle.Name));
            if (castle.InProgress)
                OnSiegeMidVictory(castle, previousOwner, nowUnixMs);
            else
            {
                if (previousOwner != 0 && previousOwner != clanId && _clan.TryGet(previousOwner, out var loser))
                    RemoveCirclets(loser, castle.Id);
                SyncSiegeRuntime(nowUnixMs);
            }

            if (actor != null)
                QueueSiegeInfo(actor, castle.Id, nowUnixMs);
        }

        return null;
    }

    int AllyOfClan(int clanId) => _clan.TryGet(clanId, out var clan) ? clan.D.AllyId : 0;

    /// <summary>The running siege this clan attacks (legacy capture without a castle id).</summary>
    int ActiveSiegeCastleFor(int clanId)
    {
        foreach (var castle in _siege.Board.Castles)
        {
            if (castle.InProgress && SiegeSide(clanId, castle.Id) == SiegeProtocol.StateAttacker)
                return castle.Id;
        }

        return 0;
    }

    void AnnounceClosed(long nowUnixMs)
    {
        foreach (int castleId in _siege.Board.Refresh(nowUnixMs))
        {
            var castle = _siege.Board.Find(castleId);
            if (castle != null)
                Broadcast(SiegeLines.RegistrationEnded(castle.Name));
        }
    }

    void ApplyBoardOwnersToClans()
    {
        foreach (var clan in _clan.All)
            clan.CastleId = _siege.Board.CastleOwnedBy(clan.Id);
        foreach (var entity in _byId.Values)
        {
            if (entity.Kind == ZoneEntityKind.Player)
                SyncClanStanding(entity);
        }
    }

    bool PersistSiege()
    {
        if (_accounts == null)
            return true;
        var owners = new Dictionary<int, int>();
        foreach (var clan in _clan.All)
            owners[clan.Id] = clan.CastleId;
        return _accounts.SaveSiege(_siege.Board.ExportCastles(), _siege.Board.ExportClans(), owners);
    }

    SiegeClanInfo? ClanInfoFor(int characterId)
    {
        var clan = ClanOf(characterId);
        if (clan == null)
            return null;
        return new SiegeClanInfo
        {
            Id = clan.Id,
            Level = clan.Level,
            LeaderId = clan.LeaderId,
            DissolvingExpiryUnixMs = clan.DissolvingExpiryUnixMs
        };
    }

    void QueueSiegeInfo(ZoneSession session, int castleId, long nowUnixMs)
    {
        var list = _siege.Board.List(castleId, nowUnixMs);
        if (list == null)
            return;
        var msg = new SiegeInfoMsg
        {
            CastleId = list.CastleId,
            Name = list.Name,
            OwnerClanId = list.OwnerClanId,
            TaxPercent = list.TaxPercent,
            SiegeUnixMs = list.SiegeUnixMs,
            RegistrationDeadlineUnixMs = list.RegistrationDeadlineUnixMs,
            RegistrationClosed = list.RegistrationClosed,
            InProgress = list.InProgress,
            EndsUnixMs = list.EndsUnixMs,
            Clans = list.Clans.Select(row => new SiegeClanMsg { ClanId = row.ClanId, Side = row.Side }).ToArray()
        };
        // Siege state is per-castle, so the requester is the only watcher: no broadcast, just this session.
        NetBridge?.FanoutSiegeInfo([session.ObjectId], msg);
    }

    void Broadcast(SystemMessageMsg line)
    {
        var live = new List<int>();
        foreach (var entity in _byId.Values.OrderBy(entity => entity.ObjectId))
        {
            if (entity.Session is { InWorld: true })
                live.Add(entity.ObjectId);
        }
        // Castle capture and the end of a siege are server-wide news, not a per-session reply.
        if (live.Count > 0)
            NetBridge?.FanoutSystemMessage(live, line);
    }

    /// <summary>One siege line to one session over Net, next to the V2 frame the same call queues.</summary>
    void FanSystemLine(ZoneSession session, SystemMessageMsg line) =>
        NetBridge?.FanoutSystemMessage([session.ObjectId], line);

}
