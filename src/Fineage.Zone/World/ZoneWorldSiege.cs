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
            return CaptureLocked(clanId, 0, (now ?? _clock()).ToUnixTimeMilliseconds());
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

    public Task<string?> TrySiegeRegisterAsync(ZoneSession session, SiegeRegisterRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            AnnounceClosed(now);
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
                FanSystemLine(session, line);
                QueueSiegeInfo(session, request.CastleId, now);
            }
        }

        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TrySiegeAnswerAsync(ZoneSession session, SiegeDefenderAnswerRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            AnnounceClosed(now);
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
                FanSystemLine(session, line);
                QueueSiegeInfo(session, request.CastleId, now);
            }
        }

        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TrySiegeListAsync(ZoneSession session, int castleId, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            AnnounceClosed(now);
            if (_siege.List(castleId, now) == null)
                error = SiegeProtocol.Castle;
            else
            {
                error = null;
                QueueSiegeInfo(session, castleId, now);
            }
        }

        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryAdminCaptureAsync(ZoneSession session, int castleId, int clanId, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = CaptureLocked(clanId, castleId, _clock().ToUnixTimeMilliseconds(), session);

        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
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
        NetBridge?.FanoutSiegeInfo([session.ObjectId], ToNetSiegeInfo(msg));
    }

    static Fineage.Net.Contract.SiegeInfoNetMsg ToNetSiegeInfo(SiegeInfoMsg info) => new()
    {
        CastleId = info.CastleId,
        Name = info.Name,
        OwnerClanId = info.OwnerClanId,
        TaxPercent = info.TaxPercent,
        SiegeUnixMs = info.SiegeUnixMs,
        RegistrationDeadlineUnixMs = info.RegistrationDeadlineUnixMs,
        RegistrationClosed = info.RegistrationClosed,
        InProgress = info.InProgress,
        EndsUnixMs = info.EndsUnixMs,
        Clans = info.Clans
            .Select(row => new Fineage.Net.Contract.SiegeClanNet { ClanId = row.ClanId, Side = row.Side })
            .ToArray()
    };

    /// <summary>Net admin entry point: the listener checks the flag before calling this.</summary>
    public async Task<string?> TryAdminCaptureNetAsync(ZoneSession session, Fineage.Net.Contract.AdminCaptureCastleNetMsg request, CancellationToken ct) =>
        await TryAdminCaptureAsync(session, request.CastleId, request.ClanId, ct).ConfigureAwait(false);

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
            NetBridge?.FanoutSystemMessage(live, ToNetSystemLine(line));
    }

    /// <summary>One siege line to one session over Net, next to the V2 frame the same call queues.</summary>
    void FanSystemLine(ZoneSession session, SystemMessageMsg line) =>
        NetBridge?.FanoutSystemMessage([session.ObjectId], ToNetSystemLine(line));

    static Fineage.Net.Contract.SystemMessageNetMsg ToNetSystemLine(SystemMessageMsg line) => new()
    {
        Id = line.Id,
        Text = line.Text,
        Params = line.Params
    };
}
