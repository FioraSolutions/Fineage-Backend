using Fineage.Protocol;
using Fineage.Zone.Siege;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public SiegeBoard Sieges => _siege;

    /// <summary>
    /// Admin and test hook. The clan must be an attacker on the one siege that is in progress.
    /// </summary>
    public string? CaptureCastle(int clanId, DateTimeOffset? now = null)
    {
        lock (_gate)
            return CaptureLocked(clanId, 0, (now ?? _clock()).ToUnixTimeMilliseconds(), outbound: null);
    }

    public SiegeStartKind StartSiege(int castleId, DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            long ms = (now ?? _clock()).ToUnixTimeMilliseconds();
            var kind = _siege.StartSiege(castleId, ms);
            if (kind is SiegeStartKind.Started or SiegeStartKind.Cancelled)
                PersistSiege();
            return kind;
        }
    }

    public string? EndSiege(int castleId, DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            long ms = (now ?? _clock()).ToUnixTimeMilliseconds();
            string? error = _siege.EndSiege(castleId, ms);
            if (error == null)
                PersistSiege();
            return error;
        }
    }

    public async Task<string?> TrySiegeRegisterAsync(ZoneSession session, SiegeRegisterRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            AnnounceClosed(now, outbound);
            bool joining = request.Joining == 1;
            bool attacker = request.Attacker == 1;
            var clan = ClanInfoFor(session.ObjectId);
            error = joining
                ? _siege.Register(new SiegeActor(session.ObjectId), clan, request.CastleId, attacker, now)
                : _siege.Unregister(new SiegeActor(session.ObjectId), clan, request.CastleId, now);
            if (error == null)
            {
                PersistSiege();
                var castle = _siege.Find(request.CastleId);
                string name = castle?.Name ?? "";
                var line = joining
                    ? attacker ? SiegeLines.RegisteredAttacker(name) : SiegeLines.RegisteredDefender(name)
                    : SiegeLines.Unregistered(name);
                outbound.Add(Pack(session, MsgType.SystemMessage, line));
                QueueSiegeInfo(session, request.CastleId, now, outbound);
            }
        }

        if (error != null)
            return error;
        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TrySiegeAnswerAsync(ZoneSession session, SiegeDefenderAnswerRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            AnnounceClosed(now, outbound);
            error = _siege.AnswerDefender(
                new SiegeActor(session.ObjectId),
                ClanInfoFor(session.ObjectId),
                request.CastleId,
                request.ClanId,
                request.Approved,
                now);
            if (error == null)
            {
                PersistSiege();
                string name = _siege.Find(request.CastleId)?.Name ?? "";
                var line = request.Approved == 1
                    ? SiegeLines.DefenderApproved(name)
                    : SiegeLines.DefenderRefused(name);
                outbound.Add(Pack(session, MsgType.SystemMessage, line));
                QueueSiegeInfo(session, request.CastleId, now, outbound);
            }
        }

        if (error != null)
            return error;
        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TrySiegeListAsync(ZoneSession session, int castleId, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            AnnounceClosed(now, outbound);
            if (_siege.List(castleId, now) == null)
                error = SiegeProtocol.Castle;
            else
            {
                error = null;
                QueueSiegeInfo(session, castleId, now, outbound);
            }
        }

        if (error != null)
            return error;
        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryAdminCaptureAsync(ZoneSession session, int castleId, int clanId, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = CaptureLocked(clanId, castleId, _clock().ToUnixTimeMilliseconds(), outbound, session);

        if (error != null)
            return error;
        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

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

    string? CaptureLocked(int clanId, int castleId, long nowUnixMs, List<Pending>? outbound, ZoneSession? actor = null)
    {
        AnnounceClosed(nowUnixMs, outbound);
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

        if (castle != null && outbound != null)
        {
            Broadcast(SiegeLines.CastleCaptured(castle.Name), outbound);
            if (!castle.InProgress)
                Broadcast(SiegeLines.SiegeFinished(castle.Name), outbound);
            if (actor != null)
                QueueSiegeInfo(actor, castle.Id, nowUnixMs, outbound);
        }

        return null;
    }

    void AnnounceClosed(long nowUnixMs, List<Pending>? outbound)
    {
        if (outbound == null)
            return;
        foreach (int castleId in _siege.Refresh(nowUnixMs))
        {
            var castle = _siege.Find(castleId);
            if (castle != null)
                Broadcast(SiegeLines.RegistrationEnded(castle.Name), outbound);
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

    void QueueSiegeInfo(ZoneSession session, int castleId, long nowUnixMs, List<Pending> outbound)
    {
        var list = _siege.List(castleId, nowUnixMs);
        if (list == null)
            return;
        outbound.Add(Pack(session, MsgType.SiegeInfo, new SiegeInfoMsg
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
        }));
    }

    void Broadcast(SystemMessageMsg line, List<Pending> outbound)
    {
        foreach (var entity in _byId.Values.OrderBy(entity => entity.ObjectId))
        {
            if (entity.Session is { InWorld: true } session)
                outbound.Add(Pack(session, MsgType.SystemMessage, line));
        }
    }
}
