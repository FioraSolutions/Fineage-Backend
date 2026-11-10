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
            _siege.Restore(saved, _accounts.ListSiegeClans(), _clock().ToUnixTimeMilliseconds());
        else
            AdoptClanOwners();
        ApplyBoardOwnersToClans();
    }

    void AdoptClanOwners()
    {
        foreach (var clan in _clans.Values)
        {
            if (clan.CastleId > 0)
                _siege.TryAdoptOwner(clan.CastleId, clan.Id);
        }
    }

    void ReleaseSiegeClan(int clanId)
    {
        _siege.ReleaseClan(clanId);
        PersistSiege();
    }

    string? CaptureLocked(int clanId, int castleId, long nowUnixMs, ZoneSession? actor = null)
    {
        AnnounceClosed(nowUnixMs);
        string? error = castleId == 0
            ? _siege.CaptureCastle(clanId, nowUnixMs)
            : _siege.CaptureCastle(clanId, castleId, nowUnixMs);
        if (error != null)
            return error;

        int taken = _siege.CastleOwnedBy(clanId);
        var castle = _siege.Find(taken);
        ApplyBoardOwnersToClans();
        if (!PersistSiege())
            return SiegeProtocol.Store;

        if (castle != null)
        {
            Broadcast(SiegeLines.CastleCaptured(castle.Name));
            if (!castle.InProgress)
                Broadcast(SiegeLines.SiegeFinished(castle.Name));
            if (actor != null)
                QueueSiegeInfo(actor, castle.Id, nowUnixMs);
        }

        return null;
    }

    void AnnounceClosed(long nowUnixMs)
    {
        foreach (int castleId in _siege.Refresh(nowUnixMs))
        {
            var castle = _siege.Find(castleId);
            if (castle != null)
                Broadcast(SiegeLines.RegistrationEnded(castle.Name));
        }
    }

    void ApplyBoardOwnersToClans()
    {
        foreach (var clan in _clans.Values)
            clan.CastleId = _siege.CastleOwnedBy(clan.Id);
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
        foreach (var clan in _clans.Values)
            owners[clan.Id] = clan.CastleId;
        return _accounts.SaveSiege(_siege.ExportCastles(), _siege.ExportClans(), owners);
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
        var list = _siege.List(castleId, nowUnixMs);
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
            if (entity.Session is { InWorld: true } session)
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
